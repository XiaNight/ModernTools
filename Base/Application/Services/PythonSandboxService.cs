using Base.Core;
using Base.Services.APIService;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text;

namespace Base.Services
{
    /// <summary>
    /// Background Python sandbox service.
    /// Manages a self-contained embedded Python runtime (downloaded on first setup, stored in app
    /// data) and a set of independently named <see cref="PythonExecution"/> instances.
    /// No UI is involved; the service is lifecycle-managed by the application.
    ///
    /// HTTP API (auto-registered by <c>APIService</c> under the class namespace path):
    /// <code>
    ///   GET  …/PythonSandboxService/status?name=default    – execution state + exit code
    ///   POST …/PythonSandboxService/setup                  – download/verify embedded Python
    ///   POST …/PythonSandboxService/run       {name, body}               – start a named execution
    ///   POST …/PythonSandboxService/runwait   {name, body, timeoutMs}    – run and wait for result
    ///   GET  …/PythonSandboxService/read?name=default      – read stdout since last call
    ///   POST …/PythonSandboxService/write  {name, input}   – write a line to stdin
    ///   POST …/PythonSandboxService/close  {name}          – kill a named execution
    /// </code>
    /// </summary>
    public class PythonSandboxService : WpfBehaviourSingleton<PythonSandboxService>
    {
        // ── Embedded Python location (app-data, never touches host Python) ─────────

        private static readonly string EmbeddedPythonDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModernTools", "PythonEmbedded");

        private const string EmbedVersion = "3.11.9";

        // Windows x64 embeddable package – self-contained, no installer, no PATH changes.
        private const string EmbedDownloadUrl =
            "https://www.python.org/ftp/python/3.11.9/python-3.11.9-embed-amd64.zip";

        private string PythonExe => Path.Combine(EmbeddedPythonDir, "python.exe");

        /// <summary>True once the embedded Python executable is present on disk.</summary>
        public bool IsReady => File.Exists(PythonExe);

        // ── Named executions ──────────────────────────────────────────────────────

        private readonly Dictionary<string, PythonExecution> _executions = new();
        private readonly object _executionsLock = new();

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        public override void OnApplicationQuit(CancelEventArgs e)
        {
            base.OnApplicationQuit(e);
            lock (_executionsLock)
            {
                foreach (var exec in _executions.Values)
                    exec.Kill();
                _executions.Clear();
            }
        }

        // ── HTTP API ──────────────────────────────────────────────────────────────

        /// <summary>Returns the status of a named execution (state + exit code).</summary>
        [GET("status")]
        private ApiResponse GetStatusApi(string name = "default")
        {
            if (string.IsNullOrEmpty(name)) name = "default";
            lock (_executionsLock)
            {
                if (!_executions.TryGetValue(name, out var exec))
                    return new ApiResponse { Status = 200, Data = new { name, isReady = IsReady, state = "NotStarted", exitCode = (int?)null } };
                return new ApiResponse { Status = 200, Data = exec.GetStatus() };
            }
        }

        /// <summary>Downloads and verifies the embedded Python runtime.</summary>
        [POST("setup")]
        private async Task<ApiResponse> SetupApi()
        {
            var result = await SetupEnvironmentAsync();
            return new ApiResponse { Status = result.Success ? 200 : 500, Data = result };
        }

        /// <summary>Starts a named Python execution and returns immediately.</summary>
        [POST("run")]
        private ApiResponse RunApi(RunRequest request)
        {
            if (request == null) return new ApiResponse { Status = 400, Data = new { error = "Request body required." } };
            if (string.IsNullOrEmpty(request.Name)) request.Name = "default";
            var result = StartExecution(request.Name, request.body);
            // StartExecution returns anonymous { success, error? } or { success, name }
            // Reflect success to pick status code
            bool success = (bool)(result.GetType().GetProperty("success")?.GetValue(result) ?? false);
            return new ApiResponse { Status = success ? 200 : 400, Data = result };
        }

        /// <summary>
        /// Runs a Python script, waits until it finishes (or the timeout elapses), and returns
        /// all stdout/stderr output in a single response.
        /// If the timeout elapses the process is killed and <c>timedOut</c> is <c>true</c>.
        /// </summary>
        [POST("runwait")]
        private async Task<ApiResponse> RunWaitApi(RunWaitRequest request)
        {
            if (request == null) return new ApiResponse { Status = 400, Data = new { error = "Request body required." } };
            if (string.IsNullOrEmpty(request.Name)) request.Name = "default";
            RunWaitResult result = await RunAndWaitAsync(request.Name, request.body, request.TimeoutMs);
            int status = result.Success ? 200 : result.TimedOut ? 408 : 400;
            return new ApiResponse { Status = status, Data = result };
        }

        /// <summary>Returns stdout written since the last Read call for a named execution.</summary>
        [GET("read")]
        private ApiResponse ReadApi(string name = "default")
        {
            if (string.IsNullOrEmpty(name)) name = "default";
            lock (_executionsLock)
            {
                if (!_executions.TryGetValue(name, out var exec))
                    return new ApiResponse { Status = 404, Data = new { error = $"No execution named '{name}' found." } };
                return new ApiResponse { Status = 200, Data = new { name, output = exec.Read() } };
            }
        }

        /// <summary>Writes a line to the stdin of a named execution.</summary>
        [POST("write")]
        private ApiResponse WriteApi(WriteRequest request)
        {
            if (request == null) return new ApiResponse { Status = 400, Data = new { error = "Request body required." } };
            string name = string.IsNullOrEmpty(request.Name) ? "default" : request.Name;
            lock (_executionsLock)
            {
                if (!_executions.TryGetValue(name, out var exec))
                    return new ApiResponse { Status = 404, Data = new { error = $"No execution named '{name}' found." } };
                exec.Write(request.Input ?? string.Empty);
                return new ApiResponse { Status = 200, Data = new { success = true } };
            }
        }

        /// <summary>Kills a named execution and removes it.</summary>
        [POST("close")]
        private ApiResponse CloseApi(string name = "default")
        {
            if (string.IsNullOrEmpty(name)) name = "default";
            CloseExecution(name);
            return new ApiResponse { Status = 200, Data = new { success = true, name } };
        }

        // ── Public programmatic API ───────────────────────────────────────────────

        /// <summary>
        /// Downloads the embedded Python package to the app-data directory if it is not already
        /// present. Safe to call repeatedly; returns immediately if Python is already ready.
        /// </summary>
        public async Task<SetupResult> SetupEnvironmentAsync()
        {
            if (IsReady)
                return new SetupResult
                {
                    Success = true,
                    Message = "Embedded Python already available.",
                    PythonPath = EmbeddedPythonDir,
                    Version = EmbedVersion,
                };

            Debug.Log($"[PythonSandbox] Downloading embedded Python {EmbedVersion}...");

            try
            {
                Directory.CreateDirectory(EmbeddedPythonDir);
                string zipPath = Path.Combine(EmbeddedPythonDir, "python-embed.zip");

                using var http = new HttpClient();
                http.Timeout = TimeSpan.FromMinutes(2);
                using var httpStream = await http.GetStreamAsync(EmbedDownloadUrl).ConfigureAwait(false);
                using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    await httpStream.CopyToAsync(fileStream).ConfigureAwait(false);

                ZipFile.ExtractToDirectory(zipPath, EmbeddedPythonDir, overwriteFiles: true);
                try { File.Delete(zipPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }

                if (!IsReady)
                    return new SetupResult { Success = false, Message = "python.exe not found after extraction." };

                Debug.Log($"[PythonSandbox] Embedded Python {EmbedVersion} ready at '{EmbeddedPythonDir}'");
                return new SetupResult
                {
                    Success = true,
                    Message = "Embedded Python setup complete.",
                    PythonPath = EmbeddedPythonDir,
                    Version = EmbedVersion,
                };
            }
            catch (Exception ex)
            {
                return new SetupResult { Success = false, Message = $"Setup failed: {ex.Message}" };
            }
        }

        /// <summary>
        /// Starts executing <paramref name="code"/> under the given <paramref name="name"/>.
        /// Returns immediately; use <see cref="RunAndWaitAsync"/> if you need the output inline.
        /// </summary>
        public object StartExecution(string name, string code)
        {
            if (string.IsNullOrEmpty(name)) name = "default";

            if (!IsReady)
                return new { success = false, error = "Embedded Python not ready. Call setup first." };

            if (string.IsNullOrWhiteSpace(code))
                return new { success = false, error = "Code must not be empty." };

            lock (_executionsLock)
            {
                if (_executions.TryGetValue(name, out var existing) && existing.IsRunning)
                    return new { success = false, error = $"Execution '{name}' is already running." };

                var exec = new PythonExecution(name, PythonExe, code);
                _executions[name] = exec;
                exec.Start();
                return new { success = true, name = name };
            }
        }

        /// <summary>
        /// Runs <paramref name="code"/> under <paramref name="name"/>, waits for it to finish
        /// (up to <paramref name="timeoutMs"/> milliseconds), then returns all stdout/stderr output.
        /// If the timeout elapses the process is killed and <c>timedOut</c> is <c>true</c> in the
        /// result. Pass <c>0</c> or a negative value to wait indefinitely.
        /// </summary>
        public async Task<RunWaitResult> RunAndWaitAsync(string name, string code, int timeoutMs = 30_000)
        {
            if (string.IsNullOrEmpty(name)) name = "default";

            if (!IsReady)
                return new RunWaitResult { Success = false, TimedOut = false, Error = "Embedded Python not ready. Call setup first." };

            if (string.IsNullOrWhiteSpace(code))
                return new RunWaitResult { Success = false, TimedOut = false, Error = "Code must not be empty." };

            PythonExecution exec;
            lock (_executionsLock)
            {
                if (_executions.TryGetValue(name, out var existing) && existing.IsRunning)
                    return new RunWaitResult { Success = false, TimedOut = false, Error = $"Execution '{name}' is already running." };

                exec = new PythonExecution(name, PythonExe, code);
                _executions[name] = exec;
                exec.Start();
            }

            bool timedOut = false;
            try
            {
                using var cts = timeoutMs > 0
                    ? new CancellationTokenSource(timeoutMs)
                    : new CancellationTokenSource();

                while (exec.IsRunning)
                {
                    if (cts.IsCancellationRequested)
                    {
                        timedOut = true;
                        break;
                    }
                    await Task.Delay(50, cts.Token).ContinueWith(_ => { });
                }
            }
            catch (OperationCanceledException)
            {
                timedOut = true;
            }

            if (timedOut)
            {
                exec.Kill();
                lock (_executionsLock) _executions.Remove(name);
                Debug.Log($"[PythonSandbox] Execution '{name}' timed out after {timeoutMs} ms.");
                return new RunWaitResult
                {
                    Success = false,
                    TimedOut = true,
                    Name = name,
                    Output = exec.ReadAll(),
                    ExitCode = null,
                    Error = $"Execution timed out after {timeoutMs} ms.",
                };
            }

            string output = exec.ReadAll();
            int? exitCode = exec.ExitCode;
            lock (_executionsLock) _executions.Remove(name);
            Debug.Log($"[PythonSandbox] Execution '{name}' finished with exit code {exitCode}.");

            return new RunWaitResult
            {
                Success = true,
                TimedOut = false,
                Name = name,
                Output = output,
                ExitCode = exitCode,
            };
        }

        /// <summary>Kills the named execution and removes it from the registry.</summary>
        public void CloseExecution(string name)
        {
            lock (_executionsLock)
            {
                if (_executions.TryGetValue(name, out var exec))
                {
                    exec.Kill();
                    _executions.Remove(name);
                }
            }
            Debug.Log($"[PythonSandbox] Execution '{name}' closed.");
        }

        // ── Request / Result DTOs ─────────────────────────────────────────────────

        public class RunWaitResult
        {
            public bool Success { get; set; }
            public bool TimedOut { get; set; }
            public string Name { get; set; }
            public string Output { get; set; }
            public int? ExitCode { get; set; }
            public string Error { get; set; }
        }

        public class RunRequest
        {
            public string Name { get; set; } = "default";
            public string body { get; set; }
        }

        public class RunWaitRequest
        {
            public string Name { get; set; } = "default";
            public string body { get; set; }
            /// <summary>Maximum time to wait in milliseconds. Use 0 or negative to wait indefinitely. Default: 30 000 ms.</summary>
            public int TimeoutMs { get; set; } = 30_000;
        }

        public class WriteRequest
        {
            public string Name { get; set; } = "default";
            public string Input { get; set; }
        }

        public class SetupResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public string PythonPath { get; set; }
            public string Version { get; set; }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // PythonExecution – a single named, long-lived Python process
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Manages a single Python process: its script file, redirected stdout/stderr (accumulated in
    /// an output buffer), and stdin (for interactive <c>input()</c> calls).
    /// </summary>
    public class PythonExecution
    {
        private static readonly string ScriptTempDir =
            Path.Combine(Path.GetTempPath(), "ModernTools_PySandbox");

        public string Name { get; }
        private readonly string _pythonExe;
        private readonly string _code;
        private string _scriptFile;

        private Process _process;
        private readonly StringBuilder _outputBuffer = new();
        private int _lastReadPos;
        private readonly object _bufferLock = new();

        public ExecutionState State { get; private set; } = ExecutionState.NotStarted;
        public int? ExitCode { get; private set; }

        /// <summary>True while the underlying Python process is still running.</summary>
        public bool IsRunning => State == ExecutionState.Running;

        public PythonExecution(string name, string pythonExe, string code)
        {
            Name = name;
            _pythonExe = pythonExe;
            _code = code;
        }

        /// <summary>Writes the script to a temp file and launches the Python process.</summary>
        public void Start()
        {
            Directory.CreateDirectory(ScriptTempDir);
            _scriptFile = Path.Combine(ScriptTempDir, $"{Name}_{Guid.NewGuid():N}.py");
            File.WriteAllText(_scriptFile, _code, Encoding.UTF8);

            _process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = _pythonExe,
                    Arguments = $"\"{_scriptFile}\"",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                },
                EnableRaisingEvents = true,
            };

            _process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    lock (_bufferLock) _outputBuffer.AppendLine(e.Data);
            };
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                    lock (_bufferLock) _outputBuffer.AppendLine(e.Data);
            };
            _process.Exited += (_, _) =>
            {
                lock (_bufferLock)
                {
                    ExitCode = _process.ExitCode;
                    State = ExecutionState.Finished;
                }
                try { if (_scriptFile != null && File.Exists(_scriptFile)) File.Delete(_scriptFile); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            };

            State = ExecutionState.Running;
            _process.Start();
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        /// <summary>
        /// Returns all output written to stdout/stderr since the last call to this method.
        /// Non-blocking: returns an empty string if no new output has appeared.
        /// </summary>
        public string Read()
        {
            lock (_bufferLock)
            {
                string all = _outputBuffer.ToString();
                if (_lastReadPos >= all.Length) return string.Empty;
                string newData = all[_lastReadPos..];
                _lastReadPos = all.Length;
                return newData;
            }
        }

        /// <summary>Returns the entire accumulated output buffer regardless of read position.</summary>
        public string ReadAll()
        {
            lock (_bufferLock)
            {
                string all = _outputBuffer.ToString();
                _lastReadPos = all.Length;
                return all;
            }
        }

        public bool HasNext()
        {
            lock (_bufferLock)
            {
                string all = _outputBuffer.ToString();
                return _lastReadPos < all.Length;
            }
        }

        /// <summary>Sends <paramref name="input"/> as a line to the process's stdin.</summary>
        public void Write(string input)
        {
            if (_process == null || _process.HasExited) return;
            try
            {
                _process.StandardInput.WriteLine(input);
                _process.StandardInput.Flush();
            }
            catch (InvalidOperationException) { }
            catch (IOException) { }
        }

        /// <summary>Terminates the process and cleans up the script file.</summary>
        public void Kill()
        {
            lock (_bufferLock)
            {
                State = ExecutionState.Killed;
                ExitCode = null;
            }
            try { if (_process != null && !_process.HasExited) _process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            try { _process?.Dispose(); } catch (ObjectDisposedException) { }
            try { if (_scriptFile != null && File.Exists(_scriptFile)) File.Delete(_scriptFile); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        /// <summary>Returns a snapshot of this execution's status.</summary>
        public object GetStatus()
        {
            lock (_bufferLock)
            {
                return new
                {
                    name = Name,
                    state = State.ToString(),
                    exitCode = ExitCode,
                    isRunning = IsRunning,
                };
            }
        }

        public enum ExecutionState { NotStarted, Running, Finished, Killed }
    }
}
