using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;

namespace Base.Services
{
    /// <summary>
    /// Registers/unregisters the app to run at Windows startup and (optionally) schedule a reboot.
    /// Designed for WPF developer tools (no dependencies).
    /// </summary>
    public static class WindowsRebootHandler
    {
        /// <summary>
        /// Standard per-user Run key: starts after user logon (no admin required).
        /// </summary>
        public static bool RegisterRunAtStartupCurrentUser(string appName, string executablePath, string? arguments = null, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(appName)) throw new ArgumentException("App name is required.", nameof(appName));
            if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("Executable path is required.", nameof(executablePath));

            executablePath = NormalizeExecutablePath(executablePath);
            EnsureFileExists(executablePath);

            var value = BuildCommandLine(executablePath, arguments);

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                         ?? throw new InvalidOperationException("Failed to open HKCU Run key.");

            if (!overwrite && key.GetValue(appName) is not null) return false;

            key.SetValue(appName, value, RegistryValueKind.String);
            return true;
        }

        /// <summary>
        /// Standard per-machine Run key: starts after user logon for all users (admin required).
        /// </summary>
        public static bool RegisterRunAtStartupLocalMachine(string appName, string executablePath, string? arguments = null, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(appName)) throw new ArgumentException("App name is required.", nameof(appName));
            if (string.IsNullOrWhiteSpace(executablePath)) throw new ArgumentException("Executable path is required.", nameof(executablePath));

            executablePath = NormalizeExecutablePath(executablePath);
            EnsureFileExists(executablePath);

            var value = BuildCommandLine(executablePath, arguments);

            using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true)
                         ?? throw new InvalidOperationException("Failed to open HKLM Run key.");

            if (!overwrite && key.GetValue(appName) is not null) return false;

            key.SetValue(appName, value, RegistryValueKind.String);
            return true;
        }

        /// <summary>
        /// Removes from HKCU Run key.
        /// </summary>
        public static bool UnregisterRunAtStartupCurrentUser(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) throw new ArgumentException("App name is required.", nameof(appName));

            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return false;

            if (key.GetValue(appName) is null) return false;

            key.DeleteValue(appName, throwOnMissingValue: false);
            return true;
        }

        /// <summary>
        /// Removes from HKLM Run key (admin required).
        /// </summary>
        public static bool UnregisterRunAtStartupLocalMachine(string appName)
        {
            if (string.IsNullOrWhiteSpace(appName)) throw new ArgumentException("App name is required.", nameof(appName));

            using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: true);
            if (key is null) return false;

            if (key.GetValue(appName) is null) return false;

            key.DeleteValue(appName, throwOnMissingValue: false);
            return true;
        }

        /// <summary>
        /// Checks whether a Run-at-startup registration exists.
        /// </summary>
        public static bool IsRegisteredRunAtStartupCurrentUser(string appName)
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return key?.GetValue(appName) is not null;
        }

        /// <summary>
        /// Checks whether a Run-at-startup registration exists (all users).
        /// </summary>
        public static bool IsRegisteredRunAtStartupLocalMachine(string appName)
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", writable: false);
            return key?.GetValue(appName) is not null;
        }

        /// <summary>
        /// Schedules a reboot using shutdown.exe (admin required).
        /// secondsDelay: 0 for immediate reboot.
        /// </summary>
        public static void Reboot(int secondsDelay = 0, string? reasonComment = null, bool forceCloseApps = false)
        {
            if (secondsDelay < 0) throw new ArgumentOutOfRangeException(nameof(secondsDelay), "Delay must be >= 0.");

            var args = $"/r /t {secondsDelay}";
            if (forceCloseApps) args += " /f";
            if (!string.IsNullOrWhiteSpace(reasonComment))
            {
                // /c supports up to 512 chars; keep it safe.
                var c = reasonComment.Length > 512 ? reasonComment.Substring(0, 512) : reasonComment;
                args += $" /c \"{EscapeQuotes(c)}\"";
            }

            StartProcessNoWindow("shutdown.exe", args, requireAdmin: true);
        }

        /// <summary>
        /// Aborts a scheduled shutdown/reboot.
        /// </summary>
        public static void AbortRebootOrShutdown()
        {
            StartProcessNoWindow("shutdown.exe", "/a", requireAdmin: false);
        }

        /// <summary>
        /// Best-effort admin check for UI gating (does not elevate).
        /// </summary>
        public static bool IsRunningAsAdministrator()
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        private static void StartProcessNoWindow(string fileName, string arguments, bool requireAdmin)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = true,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            if (requireAdmin && !IsRunningAsAdministrator())
            {
                // Will prompt UAC if app is not elevated.
                psi.Verb = "runas";
            }

            try
            {
                Process.Start(psi);
            }
            catch (Win32Exception ex) when ((uint)ex.NativeErrorCode == 1223) // user canceled UAC
            {
                throw new UnauthorizedAccessException("Operation requires elevation and was canceled by the user.", ex);
            }
        }

        private static string BuildCommandLine(string exePath, string? args)
        {
            exePath = exePath.Trim();
            var quotedExe = exePath.StartsWith("\"", StringComparison.Ordinal) ? exePath : $"\"{exePath}\"";
            if (string.IsNullOrWhiteSpace(args)) return quotedExe;
            return $"{quotedExe} {args}";
        }

        private static string NormalizeExecutablePath(string path)
        {
            path = Environment.ExpandEnvironmentVariables(path);
            path = Path.GetFullPath(path);
            return path;
        }

        private static void EnsureFileExists(string exePath)
        {
            if (!File.Exists(exePath))
                throw new FileNotFoundException("Executable not found.", exePath);
        }

        private static string EscapeQuotes(string s) => s.Replace("\"", "\\\"");
    }
}