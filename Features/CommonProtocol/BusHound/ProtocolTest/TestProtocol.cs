namespace CommonProtocol.BusHound.ProtocolTest;

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

public enum StepType
{
	Send,
	SendAndWait,
	SendAndAssert,
	Sleep,
}

[JsonDerivedType(typeof(SendStep), "send")]
[JsonDerivedType(typeof(SendAndWaitStep), "send_wait")]
[JsonDerivedType(typeof(SendAndAssertStep), "send_assert")]
[JsonDerivedType(typeof(SleepStep), "sleep")]
public abstract class TestStep
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "New step";

	[JsonIgnore]
	public abstract StepType StepType { get; }

	public abstract string BuildPreview();
}

public sealed class SendStep : TestStep
{
	[JsonIgnore]
	public override StepType StepType => StepType.Send;

	public string RequestHex { get; set; } = string.Empty;

	public override string BuildPreview()
	{
		if (string.IsNullOrWhiteSpace(RequestHex))
			return "OUT  (no request)";

		if (HexBytes.TryParse(RequestHex, out byte[] bytes, out _))
			return $"OUT  {HexFormat.FormatBrief(bytes)}";

		return $"OUT  {RequestHex.Trim()}";
	}
}

public sealed class SendAndWaitStep : TestStep
{
	[JsonIgnore]
	public override StepType StepType => StepType.SendAndWait;

	public string RequestHex { get; set; } = string.Empty;

	public int TotalTimeoutMs { get; set; } = 100;

	public int ExpectedCount { get; set; } = 1;

	public override string BuildPreview()
	{
		string timeout = $"[{TotalTimeoutMs}ms, ×{ExpectedCount}]";
		if (string.IsNullOrWhiteSpace(RequestHex))
			return $"OUT→WAIT  (no request) {timeout}";

		if (HexBytes.TryParse(RequestHex, out byte[] bytes, out _))
			return $"OUT→WAIT  {HexFormat.FormatBrief(bytes)} {timeout}";

		return $"OUT→WAIT  {RequestHex.Trim()} {timeout}";
	}
}

public sealed class SendAndAssertStep : TestStep
{
	[JsonIgnore]
	public override StepType StepType => StepType.SendAndAssert;

	public string RequestHex { get; set; } = string.Empty;

	public List<string> ExpectedLines { get; set; } = new();

	public int TotalTimeoutMs { get; set; } = 10;

	public bool AllowTrailingWildcard { get; set; } = true;

	public override string BuildPreview()
	{
		if (string.IsNullOrWhiteSpace(RequestHex))
			return "OUT→ASSERT  (no request)";

		if (HexBytes.TryParse(RequestHex, out byte[] bytes, out _))
			return $"OUT→ASSERT  {HexFormat.FormatBrief(bytes)}";

		return $"OUT→ASSERT  {RequestHex.Trim()}";
	}
}

public sealed class SleepStep : TestStep
{
	[JsonIgnore]
	public override StepType StepType => StepType.Sleep;

	public int DurationMs { get; set; } = 100;

	public override string BuildPreview()
	{
		return $"SLEEP  {DurationMs}ms";
	}
}

public sealed class TestSuite
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "New suite";

	public string Description { get; set; } = string.Empty;

	public bool ResetWhenDone { get; set; }

	public bool StopOnFirstFailure { get; set; } = true;

	public List<TestStep> Steps { get; set; } = new();
}

public sealed class TestDocument
{
	public int Version { get; set; } = 1;

	public List<TestSuite> Suites { get; set; } = new();
}
