using System.IO;
using System.Text;

namespace Base.Services
{
	public static class BatchService
	{
		public class BatchExecution
		{
			public readonly string filePath;

			public event Action<string> OnOutputDataReceived;
			public event Action<string> OnErrorDataReceived;
			public event Action<State> OnStateChanged;

			private Task<Result> runTask;
			private CancellationTokenSource cts;
			private State currentState = State.Idle;
			private DateTime lastResponse;

			public BatchExecution(string path)
			{
				filePath = path;
			}

			private bool CheckFileExists(string filepath)
			{
				if (string.IsNullOrEmpty(filepath)) return false;
				return File.Exists(filepath);
			}

			public void Stop()
			{
				if (runTask != null && !runTask.IsCompleted)
				{
					cts?.Cancel();
					runTask = null;
					SetState(State.Idle);
				}
			}

			public Result Start(int timeoutSeconds)
			{
				var task = StartAsync(timeoutSeconds);
				task.Wait();
				return task.Result;
			}

			public async Task<Result> StartAsync(int timeoutSeconds)
			{
				if (currentState != State.Idle) return new Result(-1, "Invalid state", false);
				if (!CheckFileExists(filePath)) return new Result(-2, "File not found", false);

				int timeoutMs = timeoutSeconds * 1000;

				cts = new CancellationTokenSource();
				var runTask = RunAndCheckTimer();
				var delayTask = CreateInactivityTimeoutTask(timeoutMs, cts.Token);
				var winner = await Task.WhenAny(runTask, delayTask);

				if (winner == delayTask)
				{
					cts.Cancel();
					return new Result(-3, "Timeout reached", false);
				}

				return await runTask;
			}

			private void SetState(State state, bool force = false)
			{
				if (currentState == state && !force) return;
				currentState = state;
				OnStateChanged?.Invoke(state);
			}

			public void UpdateLastResponse()
			{
				lastResponse = DateTime.UtcNow;
			}

			public async Task<Result> RunAndCheckTimer()
			{
				SetState(State.Running);
				var timer = new System.Diagnostics.Stopwatch();
				timer.Start();

				runTask = RunBatAsync();
				Result result = await runTask;
				result.elapsed = timer.Elapsed;

				SetState(State.Saving);

				SetState(result.success ? State.Completed : State.Error);
				return result;
			}

			private async Task<Result> RunBatAsync()
			{
				Encoding encoding = Encoding.UTF8;
				var process = new System.Diagnostics.Process
				{
					StartInfo = new System.Diagnostics.ProcessStartInfo
					{
						FileName = "cmd.exe",
						Arguments = $"/C \"\"{filePath}\"\"",
						UseShellExecute = false,
						RedirectStandardInput = true,
						RedirectStandardOutput = true,
						RedirectStandardError = true,
						CreateNoWindow = true,
						WorkingDirectory = Path.GetDirectoryName(filePath),
						StandardOutputEncoding = encoding,
						StandardErrorEncoding = encoding,
					},
					EnableRaisingEvents = true,
				};

				try
				{
					var tcs = new TaskCompletionSource<(int, string)>();
					StringBuilder outputBuilder = new StringBuilder();

					//- Register cancellation token to kill the process if it is still running
					cts.Token.Register(() =>
					{
						if (!process.HasExited)
						{
							try { process.Kill(); } catch { }
						}
						tcs.TrySetCanceled();
					});

					process.OutputDataReceived += (s, e) =>
					{
						if (e.Data != null)
						{
							OnOutputDataReceived?.Invoke(e.Data);
							outputBuilder.AppendLine(e.Data);
						}
					};
					process.ErrorDataReceived += (s, e) =>
					{
						string message = e.Data;
						if (message != null)
						{
							OnErrorDataReceived?.Invoke(message);
							outputBuilder.AppendLine(message);
						}
					};

					process.Start();
					process.StandardInput.WriteLine();
					process.StandardInput.Close();

					process.BeginOutputReadLine();
					process.BeginErrorReadLine();

					await process.WaitForExitAsync(cts.Token);

					// Ensure all output is processed
					process.WaitForExit();

					int exitCode = process.ExitCode;
					return new Result(exitCode, outputBuilder.ToString(), true);
				}
				catch (Exception ex)
				{
					process.Dispose();
					return new Result(0, $"Error: {ex.Message}", false);
				}
			}

			/// <summary>
			/// Creates a task that will complete when the specified timeout is reached or when the token is cancelled.
			/// </summary>
			public Task CreateInactivityTimeoutTask(int timeoutSeconds, CancellationToken token)
			{
				lastResponse = DateTime.UtcNow;

				return Task.Run(async () =>
				{
					var timeout = TimeSpan.FromSeconds(timeoutSeconds);
					var sw = System.Diagnostics.Stopwatch.StartNew();

					while (!token.IsCancellationRequested)
					{
						await Task.Delay(500, token); // Check interval

						var elapsed = DateTime.UtcNow - lastResponse;
						if (elapsed > timeout)
							return;
					}

					token.ThrowIfCancellationRequested();
				}, token);
			}

			public struct Result(int exitCode, string output, bool success)
			{
				public int exitCode = exitCode;
				public string output = output;
				public bool success = success;
				public TimeSpan elapsed;
			}

			public enum State
			{
				Idle,
				Running,
				Saving,
				Completed,
				Error,
			}
		}
	}
}