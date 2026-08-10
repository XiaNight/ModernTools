using Base.Helpers;

namespace Base.Protocol;

/// <summary>Describes what a scanned reply is expected to satisfy.</summary>
public sealed class ProtocolCheck
{
	public string Name = "";
	public string Group = "";
	public byte Command;
	public byte Key;
	public ushort Index;
	public bool ExpectIndexEcho;
	public ValidationMode Mode = ValidationMode.Structural;
	public IReadOnlyList<RangeRule> Rules;
	public byte[] Baseline;
	// Number of data bytes (from Byte 4) compared in ExactMatch mode. 0 = compare the
	// whole captured baseline.
	public int CompareLength;
}

/// <summary>
/// Judges a received reply against a <see cref="ProtocolCheck"/> and produces a
/// <see cref="ScanResult"/> carrying a pass/fail verdict and, on failure, the reason.
/// </summary>
public static class ProtocolValidator
{
	public static ScanResult Validate(ProtocolCheck check, byte[] request, byte[] response, double durationMs)
	{
		ScanResult result = new()
		{
			Name = check.Name,
			Group = check.Group,
			Mode = check.Mode,
			Request = request,
			Response = response,
			DurationMs = durationMs,
		};

		// 1. No reply at all.
		if (response == null || response.Length == 0)
			return Fail(result, ScanFailure.Timeout);

		ReadOnlySpan<byte> frame = ProtocolFrame.BodyOf(response);
		if (frame.Length < 2)
			return Fail(result, ScanFailure.LengthMismatch);

		// 2. Explicit device error-ack.
		if (frame[0] == 0xFF && frame[1] == 0xAA)
			return Fail(result, ScanFailure.ErrorAck);

		// 3. Command/Key echo.
		if (frame[0] != check.Command || frame[1] != check.Key)
			return Fail(result, ScanFailure.EchoMismatch,
				$"got {frame[0]:X2} {frame[1]:X2}, expected {check.Command:X2} {check.Key:X2}");

		// 3b. Optional index echo.
		if (check.ExpectIndexEcho)
		{
			int need = ProtocolFrame.IndexOffset + 2;
			if (frame.Length < need)
				return Fail(result, ScanFailure.LengthMismatch);
			if (frame[ProtocolFrame.IndexOffset] != check.Index.LowByte())
				return Fail(result, ScanFailure.EchoMismatch, "index");
			if (frame[ProtocolFrame.IndexOffset + 1] != check.Index.HighByte())
				return Fail(result, ScanFailure.EchoMismatch, "index");
		}

		switch (check.Mode)
		{
			case ValidationMode.Structural:
				break;

			case ValidationMode.ValidRange:
				ScanResult rangeFail = CheckRanges(result, frame, check.Rules);
				if (rangeFail != null) return rangeFail;
				break;

			case ValidationMode.ExactMatch:
				ScanResult baseFail = CheckBaseline(result, frame, check);
				if (baseFail != null) return baseFail;
				break;
		}

		result.Verdict = ScanVerdict.Pass;
		result.Failure = ScanFailure.None;
		result.Message = ScanResult.Describe(ScanFailure.None);
		return result;
	}

	/// <summary>Extracts the data region (from Byte 4) of a reply to store as a baseline.</summary>
	public static byte[] CaptureBaseline(byte[] response, int compareLength = 0)
	{
		ReadOnlySpan<byte> frame = ProtocolFrame.BodyOf(response);
		if (frame.Length <= ProtocolFrame.DataOffset) return Array.Empty<byte>();

		int dataLen = frame.Length - ProtocolFrame.DataOffset;
		int len = compareLength > 0 ? Math.Min(compareLength, dataLen) : dataLen;
		return frame.Slice(ProtocolFrame.DataOffset, len).ToArray();
	}

	private static ScanResult CheckRanges(ScanResult result, ReadOnlySpan<byte> frame, IReadOnlyList<RangeRule> rules)
	{
		if (rules == null || rules.Count == 0) return null;

		foreach (RangeRule rule in rules)
		{
			int length = Math.Max(1, rule.Length);
			if (rule.Offset < 0 || rule.Offset + length > frame.Length)
				return Fail(result, ScanFailure.LengthMismatch, rule.Label);

			if (rule.AllowedValues is { Length: > 0 })
			{
				byte value = frame[rule.Offset];
				bool ok = false;
				foreach (byte allowed in rule.AllowedValues)
				{
					if (allowed == value) { ok = true; break; }
				}
				if (!ok)
					return Fail(result, ScanFailure.OutOfRange, $"{rule.Label}=0x{value:X2}");
			}
			else
			{
				ulong value = ReadLittleEndian(frame, rule.Offset, length);
				if ((long)value < rule.Min || (long)value > rule.Max)
					return Fail(result, ScanFailure.OutOfRange, $"{rule.Label}={value}");
			}
		}
		return null;
	}

	private static ScanResult CheckBaseline(ScanResult result, ReadOnlySpan<byte> frame, ProtocolCheck check)
	{
		if (check.Baseline == null || check.Baseline.Length == 0)
			return Fail(result, ScanFailure.NoBaseline);

		int dataLen = Math.Max(0, frame.Length - ProtocolFrame.DataOffset);
		int compare = check.CompareLength > 0 ? check.CompareLength : check.Baseline.Length;
		if (compare > check.Baseline.Length) compare = check.Baseline.Length;
		if (dataLen < compare)
			return Fail(result, ScanFailure.LengthMismatch);

		ReadOnlySpan<byte> data = frame.Slice(ProtocolFrame.DataOffset, compare);
		for (int i = 0; i < compare; i++)
		{
			if (data[i] != check.Baseline[i])
				return Fail(result, ScanFailure.BaselineMismatch, $"byte {i}: {data[i]:X2} != {check.Baseline[i]:X2}");
		}
		return null;
	}

	private static ulong ReadLittleEndian(ReadOnlySpan<byte> span, int offset, int length)
	{
		ulong value = 0;
		for (int i = 0; i < length && i < 8; i++)
			value |= (ulong)span[offset + i] << (8 * i);
		return value;
	}

	private static ScanResult Fail(ScanResult result, ScanFailure failure, string detail = null)
	{
		result.Verdict = ScanVerdict.Fail;
		result.Failure = failure;
		result.Message = detail == null
			? ScanResult.Describe(failure)
			: $"{ScanResult.Describe(failure)} — {detail}";
		return result;
	}
}
