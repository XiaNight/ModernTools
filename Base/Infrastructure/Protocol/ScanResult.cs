namespace Base.Protocol;

/// <summary>How strictly a scanned reply is judged.</summary>
public enum ValidationMode
{
	/// <summary>Reply arrived, is not an error-ack, and echoes the request's Command/Key.</summary>
	Structural = 0,
	/// <summary>Structural, plus every configured data field falls in its allowed range/set.</summary>
	ValidRange = 1,
	/// <summary>Structural, plus the reply's data matches a previously captured baseline exactly.</summary>
	ExactMatch = 2,
}

public enum ScanVerdict
{
	Pass = 0,
	Fail = 1,
	Skipped = 2,
}

/// <summary>Why a scanned entry failed. Recorded on every non-passing result.</summary>
public enum ScanFailure
{
	None = 0,
	/// <summary>No reply within the timeout.</summary>
	Timeout = 1,
	/// <summary>Device returned the FF AA error prefix.</summary>
	ErrorAck = 2,
	/// <summary>Reply did not echo the expected Command/Key(/Index).</summary>
	EchoMismatch = 3,
	/// <summary>Reply was too short to contain the fields being checked.</summary>
	LengthMismatch = 4,
	/// <summary>A data field fell outside its allowed range/set.</summary>
	OutOfRange = 5,
	/// <summary>Reply data differed from the captured baseline.</summary>
	BaselineMismatch = 6,
	/// <summary>ExactMatch was requested but no baseline has been captured yet.</summary>
	NoBaseline = 7,
	/// <summary>An exception was thrown while sending/validating.</summary>
	Exception = 8,
}

/// <summary>
/// One allowed-value check against the received frame. <see cref="Offset"/> is absolute
/// within the protocol frame (Command = 0, Data starts at 4). When
/// <see cref="AllowedValues"/> is set the single byte at Offset must be one of them;
/// otherwise the <see cref="Length"/> little-endian bytes at Offset must satisfy
/// Min..Max (inclusive).
/// </summary>
public sealed class RangeRule
{
	public string Label { get; set; } = "";
	public int Offset { get; set; }
	public int Length { get; set; } = 1;
	public long Min { get; set; }
	public long Max { get; set; }
	public byte[] AllowedValues { get; set; }

	public RangeRule() { }

	public RangeRule(string label, int offset, long min, long max, int length = 1)
	{
		Label = label;
		Offset = offset;
		Min = min;
		Max = max;
		Length = length;
	}

	public static RangeRule Allowed(string label, int offset, params byte[] allowed)
		=> new() { Label = label, Offset = offset, Length = 1, AllowedValues = allowed };
}

/// <summary>Outcome of scanning a single protocol entry.</summary>
public sealed class ScanResult
{
	public string Name { get; set; } = "";
	public string Group { get; set; } = "";
	public ScanVerdict Verdict { get; set; }
	public ScanFailure Failure { get; set; }
	public ValidationMode Mode { get; set; }
	public string Message { get; set; } = "";
	public byte[] Request { get; set; }
	public byte[] Response { get; set; }
	public double DurationMs { get; set; }
	public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;

	public string RequestHex => ProtocolFrame.ToHex(Request);
	public string ResponseHex => ProtocolFrame.ToHex(Response);

	public static string Describe(ScanFailure failure) => failure switch
	{
		ScanFailure.None => "OK",
		ScanFailure.Timeout => "No reply (timeout)",
		ScanFailure.ErrorAck => "Device error-ack (FF AA)",
		ScanFailure.EchoMismatch => "Reply did not echo Command/Key",
		ScanFailure.LengthMismatch => "Reply too short",
		ScanFailure.OutOfRange => "Value out of allowed range",
		ScanFailure.BaselineMismatch => "Differs from saved baseline",
		ScanFailure.NoBaseline => "No baseline captured yet",
		ScanFailure.Exception => "Exception during scan",
		_ => failure.ToString(),
	};
}
