using Base.Core;
using Base.Services.APIService;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Base.Services
{
    /// <summary>
    /// Background Python sandbox service. Provides an API to set up an isolated virtual
    /// environment, execute raw Python code strings, and tear down the environment.
    /// No UI is involved. The environment is only created on demand and is automatically
    /// terminated when the application closes.
    /// </summary>
    public class PythonSandboxService : WpfBehaviourSingleton<PythonSandboxService>
    {
        private string _envPath;
        private string _pythonExe;
        private readonly List<Process> _activeProcesses = new();
        private readonly object _processLock = new();

        /// <summary>Whether a virtual environment has been set up and is ready.</summary>
        public bool IsSetup { get; private set; }

        /// <summary>Absolute path to the active virtual environment directory, or null if not set up.</summary>
        public string EnvironmentPath => _envPath;

        // ── Lifecycle ──────────────────────────────────────────────────────────────

        /// <summary>Ensures the environment is closed when the application exits.</summary>
        public override void OnApplicationQuit(CancelEventArgs e)
        {
            base.OnApplicationQuit(e);
            CloseEnvironment();
        }

        // ── HTTP API ───────────────────────────────────────────────────────────────

        /// <summary>Returns the current sandbox status.</summary>
        [GET("~/python/status", false)]
        private object GetStatus() => new
        {
            IsSetup,
            EnvironmentPath = _envPath,
            PythonExecutable = _pythonExe,
        };

        /// <summary>Sets up (or reuses) a Python virtual environment.</summary>
        /// <param name="envPath">Optional path for the venv. Omit to use the default location.</param>
        [POST("~/python/setup", false)]
        private SetupResult SetupApi(string envPath = null)
            => SetupEnvironment(envPath);

        /// <summary>Executes raw Python code inside the sandbox.</summary>
        /// <param name="code">Python source code to run.</param>
        /// <param name="timeoutSeconds">Maximum execution time in seconds (default 30).</param>
        [POST("~/python/run", false)]
        private async Task<RunResult> RunApi(string code, int timeoutSeconds = 30)
            => await RunCodeAsync(code, timeoutSeconds);

        /// <summary>Tears down the active environment and terminates any running processes.</summary>
        [POST("~/python/close", false)]
        private void CloseApi() => CloseEnvironment();

        // ── Public programmatic API ────────────────────────────────────────────────

        /// <summary>
        /// Creates (or reuses) a Python virtual environment at <paramref name="envPath"/>.
        /// If <paramref name="envPath"/> is null or empty, a default path under
        /// <c>%LOCALAPPDATA%\ModernTools\PythonSandbox</c> is used.
        /// </summary>
        public SetupResult SetupEnvironment(string envPath = null)
        {
            string systemPython = FindSystemPython();
            if (systemPython == null)
            {
                return new SetupResult
                {
                    Success = false,
                    Message = "Python executable not found. Ensure Python is installed and available in PATH.",
                };
            }

            if (string.IsNullOrWhiteSpace(envPath))
            {
                envPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ModernTools",
                    "PythonSandbox");
            }

            try
            {
                string parentDir = Path.GetDirectoryName(envPath);
                if (!string.IsNullOrEmpty(parentDir))
                    Directory.CreateDirectory(parentDir);
            }
            catch (Exception ex)
            {
                return new SetupResult { Success = false, Message = $"Failed to create parent directory: {ex.Message}" };
            }

            // Create the venv only if it does not already exist.
            if (!Directory.Exists(envPath))
            {
                var createResult = RunProcessSync(systemPython, $"-m venv \"{envPath}\"", 60);
                if (!createResult.Success || createResult.ExitCode != 0)
                {
                    string detail = string.IsNullOrWhiteSpace(createResult.Error)
                        ? createResult.Output
                        : createResult.Error;
                    return new SetupResult
                    {
                        Success = false,
                        Message = $"Failed to create virtual environment: {detail}",
                    };
                }
            }

            string venvPython = GetVenvPython(envPath);
            if (!File.Exists(venvPython))
            {
                return new SetupResult
                {
                    Success = false,
                    Message = $"Virtual environment Python executable not found at expected path: {venvPython}",
                };
            }

            // Query Python version for reporting.
            var versionResult = RunProcessSync(venvPython, "--version", 10);
            string version = versionResult.Output.Trim();
            if (string.IsNullOrWhiteSpace(version))
                version = versionResult.Error.Trim();

            _envPath = envPath;
            _pythonExe = venvPython;
            IsSetup = true;

            Debug.Log($"[PythonSandbox] Environment ready at '{envPath}' ({version})");

            return new SetupResult
            {
                Success = true,
                Message = "Environment ready.",
                EnvironmentPath = envPath,
                PythonVersion = version,
            };
        }

        /// <summary>
        /// Executes raw Python <paramref name="code"/> inside the sandbox and returns
        /// stdout, stderr, exit code, and elapsed time.
        /// If the environment has not been set up, it is created automatically using
        /// the default location before the code is run.
        /// </summary>
        public async Task<RunResult> RunCodeAsync(string code, int timeoutSeconds = 30)
        {
            if (string.IsNullOrWhiteSpace(code))
                return new RunResult { Success = false, Error = "Code must not be empty." };

            // Auto-setup if no environment is active.
            if (!IsSetup)
            {
                var setup = SetupEnvironment();
                if (!setup.Success)
                    return new RunResult { Success = false, Error = $"Environment setup failed: {setup.Message}" };
            }

            // Write the code to a dedicated sandbox temp directory to reduce the chance
            // of other processes observing or interfering with the temporary script file.
            string sandboxTempDir = Path.Combine(Path.GetTempPath(), "ModernTools_PySandbox");
            Directory.CreateDirectory(sandboxTempDir);
            string tempFile = Path.Combine(sandboxTempDir, $"{Guid.NewGuid():N}.py");
            try
            {
                await File.WriteAllTextAsync(tempFile, code, Encoding.UTF8);
                return await RunFileAsync(tempFile, timeoutSeconds);
            }
            finally
            {
                try { File.Delete(tempFile); } catch { }
            }
        }

        /// <summary>
        /// Kills all active Python processes and resets the sandbox to the uninitialized state.
        /// The virtual environment directory on disk is preserved so it can be reused.
        /// </summary>
        public void CloseEnvironment()
        {
            lock (_processLock)
            {
                foreach (var p in _activeProcesses)
                {
                    try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch { }
                    try { p.Dispose(); } catch { }
                }
                _activeProcesses.Clear();
            }

            _envPath = null;
            _pythonExe = null;
            IsSetup = false;

            Debug.Log("[PythonSandbox] Environment closed.");
        }

        // ── Private helpers ────────────────────────────────────────────────────────

        private async Task<RunResult> RunFileAsync(string filePath, int timeoutSeconds)
        {
            var sw = Stopwatch.StartNew();
            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));

            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonExe,
                    Arguments = $"\"{filePath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };

            process.OutputDataReceived += (_, e) => { if (e.Data != null) outputSb.AppendLine(e.Data); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) errorSb.AppendLine(e.Data); };

            lock (_processLock)
                _activeProcesses.Add(process);

            try
            {
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                // Capture a local reference to avoid a potential race between the cancellation
                // callback and the finally block that disposes the process object.
                var capturedProcess = process;
                cts.Token.Register(() =>
                {
                    try { if (!capturedProcess.HasExited) capturedProcess.Kill(entireProcessTree: true); } catch { }
                });

                await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
                process.WaitForExit(); // drain async output buffers

                sw.Stop();

                return new RunResult
                {
                    Success = process.ExitCode == 0,
                    ExitCode = process.ExitCode,
                    Output = outputSb.ToString(),
                    Error = errorSb.ToString(),
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                };
            }
            catch (OperationCanceledException)
            {
                sw.Stop();
                return new RunResult
                {
                    Success = false,
                    ExitCode = -1,
                    Error = $"Execution timed out after {timeoutSeconds} seconds.",
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                };
            }
            catch (Exception ex)
            {
                sw.Stop();
                return new RunResult
                {
                    Success = false,
                    ExitCode = -1,
                    Error = ex.Message,
                    ElapsedMs = sw.Elapsed.TotalMilliseconds,
                };
            }
            finally
            {
                lock (_processLock)
                    _activeProcesses.Remove(process);
                try { process.Dispose(); } catch { }
            }
        }

        /// <summary>Runs an external process synchronously and returns captured output.</summary>
        private static SimpleResult RunProcessSync(string exe, string args, int timeoutSeconds)
        {
            var outputSb = new StringBuilder();
            var errorSb = new StringBuilder();

            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };

            p.OutputDataReceived += (_, e) => { if (e.Data != null) outputSb.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) errorSb.AppendLine(e.Data); };

            try
            {
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                bool exited = p.WaitForExit(timeoutSeconds * 1000);
                if (!exited)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                }
                p.WaitForExit(); // drain buffers

                return new SimpleResult
                {
                    Success = exited && p.ExitCode == 0,
                    ExitCode = exited ? p.ExitCode : -1,
                    Output = outputSb.ToString(),
                    Error = errorSb.ToString(),
                };
            }
            catch (Exception ex)
            {
                return new SimpleResult { Success = false, ExitCode = -1, Output = string.Empty, Error = ex.Message };
            }
        }

        /// <summary>
        /// Attempts to locate a Python 3 executable on the current machine.
        /// Checks PATH first, then common Windows installation directories.
        /// Returns null if Python cannot be found.
        /// </summary>
        private static string FindSystemPython()
        {
            // Check well-known executable names that may be on PATH.
            foreach (var candidate in new[] { "python", "python3" })
            {
                try
                {
                    using var p = new Process
                    {
                        StartInfo = new ProcessStartInfo
                        {
                            FileName = candidate,
                            Arguments = "--version",
                            UseShellExecute = false,
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            CreateNoWindow = true,
                        },
                    };
                    p.Start();
                    bool exited = p.WaitForExit(5000);
                    if (exited && p.ExitCode == 0)
                        return candidate;
                }
                catch { }
            }

            // Fall back to common Windows install locations.
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);

            var searchRoots = new[]
            {
                Path.Combine(localAppData, "Programs", "Python"),
                Path.Combine(programFiles, "Python"),
                Path.Combine(programFilesX86, "Python"),
                @"C:\Python3",
            };

            foreach (var root in searchRoots)
            {
                if (!Directory.Exists(root)) continue;
                foreach (string subDir in Directory.GetDirectories(root, "Python3*"))
                {
                    string exe = Path.Combine(subDir, "python.exe");
                    if (File.Exists(exe)) return exe;
                }
                // Also check the root itself (e.g. C:\Python3\python.exe)
                string rootExe = Path.Combine(root, "python.exe");
                if (File.Exists(rootExe)) return rootExe;
            }

            return null;
        }

        /// <summary>Returns the path to the Python executable inside a virtual environment.</summary>
        private static string GetVenvPython(string envPath)
        {
            // Windows layout: <venv>/Scripts/python.exe
            string win = Path.Combine(envPath, "Scripts", "python.exe");
            if (File.Exists(win)) return win;

            // Unix layout: <venv>/bin/python3 or <venv>/bin/python
            string unix3 = Path.Combine(envPath, "bin", "python3");
            if (File.Exists(unix3)) return unix3;

            return Path.Combine(envPath, "bin", "python");
        }

        // ── Result types ───────────────────────────────────────────────────────────

        /// <summary>Result returned by <see cref="SetupEnvironment"/>.</summary>
        public class SetupResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string EnvironmentPath { get; set; }
            public string PythonVersion { get; set; }
        }

        /// <summary>Result returned by <see cref="RunCodeAsync"/>.</summary>
        public class RunResult
        {
            public bool Success { get; set; }
            public int ExitCode { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
            public double ElapsedMs { get; set; }
        }

        private struct SimpleResult
        {
            public bool Success;
            public int ExitCode;
            public string Output;
            public string Error;
        }
    }
}
