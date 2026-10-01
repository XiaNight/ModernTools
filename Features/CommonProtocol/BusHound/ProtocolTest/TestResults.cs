namespace CommonProtocol.BusHound.ProtocolTest;

using System.Collections.Generic;

public enum TestVerdict
{
	Pass,
	Timeout,
	Mismatch,
	Error,
	Skipped,
}

public sealed class StepRunResult
{
	public TestVerdict Verdict { get; set; }

	public string Message { get; set; } = string.Empty;

	public double ElapsedMs { get; set; }

	public List<byte[]> Received { get; set; } = new();
}

public sealed class SuiteRunResult
{
	public TestVerdict Verdict { get; set; }

	public string Message { get; set; } = string.Empty;

	public double TotalElapsedMs { get; set; }

	public List<StepRunResult> StepResults { get; set; } = new();
}
