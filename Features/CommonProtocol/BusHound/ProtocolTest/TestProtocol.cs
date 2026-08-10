namespace CommonProtocol.BusHound.ProtocolTest;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// A single Bus Hound protocol test: a request frame plus the sequence of USB packets its reply
/// must match. Matching is strict — the returned bytes must equal the expected bytes exactly, with
/// two relaxations: the wildcard nibble <c>X</c> matches any hex value in that nibble position, and
/// (when <see cref="AllowTrailingWildcard"/> is set) any extra trailing bytes on a received packet
/// are ignored. Each expected line describes one USB packet, so a three-line expectation requires
/// three packets, each matching its line in order.
/// </summary>
public sealed class TestProtocol
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "New test";

	/// <summary>Request bytes as a hex string, e.g. "02 00 B5 00".</summary>
	public string RequestHex { get; set; } = string.Empty;

	/// <summary>
	/// One expected packet per line. Each line is hex bytes where any nibble may be the wildcard
	/// <c>X</c> (e.g. "02 F4 XX 1X"). Whitespace is ignored, matching ParseCommand.
	/// </summary>
	public List<string> ExpectedLines { get; set; } = new();

	/// <summary>Total time budget, in milliseconds, to receive and match every expected packet.</summary>
	public int TotalTimeoutMs { get; set; } = 10;

	/// <summary>
	/// When true, a received packet that is longer than its expected line still passes as long as
	/// the leading bytes match — the extra trailing bytes are treated as wildcards. Default true.
	/// </summary>
	public bool AllowTrailingWildcard { get; set; } = true;

	/// <summary>Short, single-line summary of the request and expected packet count for the row preview.</summary>
	public string BuildPreview()
	{
		string request;
		if (string.IsNullOrWhiteSpace(RequestHex))
			request = "(no request)";
		else if (HexBytes.TryParse(RequestHex, out byte[] bytes, out _))
			request = FormatBrief(bytes);
		else
			request = RequestHex.Trim();

		int count = ExpectedLines?.Count ?? 0;
		return $"OUT  {request}";
	}

	/// <summary>
	/// Formats a packet as uppercase hex bytes with trailing zero bytes trimmed. No length cap — the
	/// displaying TextBlock is expected to trim for width (TextTrimming), so this stays width-agnostic.
	/// </summary>
	public static string FormatBrief(byte[] bytes)
	{
		if (bytes == null || bytes.Length == 0)
			return "(empty)";

		int end = bytes.Length;
		while (end > 1 && bytes[end - 1] == 0)
			end--;

		StringBuilder sb = new();
		for (int i = 0; i < end; i++)
		{
			if (i > 0) sb.Append(' ');
			sb.Append(bytes[i].ToString("X2"));
		}

		return sb.ToString();
	}
}

/// <summary>
/// Outcome of running a <see cref="TestProtocol"/> once: the verdict, a human-readable message, the
/// measured device response time, and the packets received (raw, in arrival order). UI-agnostic so it
/// can be produced by the panel or returned over the API.
/// </summary>
public sealed class TestRunResult
{
	public TestVerdict Verdict { get; set; }

	public string Message { get; set; } = string.Empty;

	/// <summary>Measured send → last-matched-packet latency in milliseconds; 0 when not measured.</summary>
	public double ElapsedMs { get; set; }

	/// <summary>Received packets in arrival order (report-id stripped, as compared against expectations).</summary>
	public List<byte[]> Received { get; set; } = new();
}

/// <summary>
/// Parses plain hex strings the same way as the Bus Hound page's <c>ParseCommand</c>: whitespace is
/// ignored anywhere, and hex digits are paired into bytes (an odd count is invalid). No wildcards.
/// </summary>
public static class HexBytes
{
	public static bool TryParse(string text, out byte[] bytes, out string error)
	{
		bytes = null;
		error = null;

		if (string.IsNullOrWhiteSpace(text))
		{
			error = "enter at least one byte.";
			return false;
		}

		List<byte> result = new();
		int hi = -1;

		foreach (char c in text)
		{
			if (char.IsWhiteSpace(c))
				continue;

			int val = HexValue(c);
			if (val < 0)
			{
				error = $"'{c}' is not a hex digit.";
				return false;
			}

			if (hi < 0)
			{
				hi = val;
			}
			else
			{
				result.Add((byte)(hi << 4 | val));
				hi = -1;
			}
		}

		if (hi >= 0)
		{
			error = "odd number of hex digits.";
			return false;
		}

		if (result.Count == 0)
		{
			error = "enter at least one byte.";
			return false;
		}

		bytes = result.ToArray();
		return true;
	}

	private static int HexValue(char c)
	{
		if (c >= '0' && c <= '9') return c - '0';
		if (c >= 'a' && c <= 'f') return c - 'a' + 10;
		if (c >= 'A' && c <= 'F') return c - 'A' + 10;
		return -1;
	}
}

/// <summary>
/// A parsed expected packet: the fixed byte values together with a per-nibble mask marking which
/// nibbles are fixed (0xF) versus wildcard (0x0). Parsing is whitespace-insensitive, mirroring
/// ParseCommand, with the addition of the <c>X</c> wildcard nibble.
/// </summary>
public sealed class ExpectedPacket
{
	private readonly byte[] value;
	private readonly byte[] mask;

	private ExpectedPacket(byte[] value, byte[] mask)
	{
		this.value = value;
		this.mask = mask;
	}

	public int Length => value.Length;

	/// <summary>
	/// Parses one expected line. Whitespace is ignored anywhere; the remaining characters must each
	/// be a hex digit or the wildcard <c>X</c>/<c>x</c>, paired into bytes (an odd count is invalid).
	/// </summary>
	public static bool TryParse(string line, out ExpectedPacket packet, out string error)
	{
		packet = null;
		error = null;

		if (line == null)
		{
			error = "Line is null.";
			return false;
		}

		List<byte> values = new();
		List<byte> masks = new();
		int hiValue = 0;
		int hiMask = 0;
		bool pending = false;

		foreach (char c in line)
		{
			if (char.IsWhiteSpace(c))
				continue;

			if (!TryNibble(c, out int nibbleValue, out int nibbleMask, out error))
				return false;

			if (!pending)
			{
				hiValue = nibbleValue;
				hiMask = nibbleMask;
				pending = true;
			}
			else
			{
				values.Add((byte)(hiValue << 4 | nibbleValue));
				masks.Add((byte)(hiMask << 4 | nibbleMask));
				pending = false;
			}
		}

		if (pending)
		{
			error = "odd number of hex digits.";
			return false;
		}

		if (values.Count == 0)
		{
			error = "line has no bytes.";
			return false;
		}

		packet = new ExpectedPacket(values.ToArray(), masks.ToArray());
		return true;
	}

	/// <summary>
	/// True when <paramref name="actual"/> matches every fixed nibble. Length must be equal unless
	/// <paramref name="allowTrailing"/> is set, in which case <paramref name="actual"/> may be longer
	/// (the extra trailing bytes are ignored).
	/// </summary>
	public bool Matches(byte[] actual, bool allowTrailing)
	{
		if (actual == null)
			return false;

		if (allowTrailing)
		{
			if (actual.Length < value.Length)
				return false;
		}
		else if (actual.Length != value.Length)
		{
			return false;
		}

		for (int i = 0; i < value.Length; i++)
		{
			if ((actual[i] & mask[i]) != value[i])
				return false;
		}

		return true;
	}

	/// <summary>
	/// True when the byte at <paramref name="index"/> of a received packet matches this expected
	/// packet's fixed nibbles. Indices beyond the expected length are considered a match only when
	/// <paramref name="allowTrailing"/> is set (extra trailing bytes treated as wildcards).
	/// </summary>
	public bool ByteMatches(int index, byte actual, bool allowTrailing)
	{
		if (index >= value.Length)
			return allowTrailing;

		return (actual & mask[index]) == value[index];
	}

	/// <summary>Human-readable form, e.g. "02 F4 XX 1X", for failure messages.</summary>
	public override string ToString()
	{
		StringBuilder sb = new();
		for (int i = 0; i < value.Length; i++)
		{
			if (i > 0) sb.Append(' ');
			sb.Append(NibbleChar(value[i] >> 4 & 0xF, mask[i] >> 4 & 0xF));
			sb.Append(NibbleChar(value[i] & 0xF, mask[i] & 0xF));
		}

		return sb.ToString();
	}

	private static bool TryNibble(char c, out int nibbleValue, out int nibbleMask, out string error)
	{
		error = null;

		if (c == 'X' || c == 'x')
		{
			nibbleValue = 0;
			nibbleMask = 0x0;
			return true;
		}

		if (c >= '0' && c <= '9')
		{
			nibbleValue = c - '0';
			nibbleMask = 0xF;
			return true;
		}

		if (c >= 'a' && c <= 'f')
		{
			nibbleValue = c - 'a' + 10;
			nibbleMask = 0xF;
			return true;
		}

		if (c >= 'A' && c <= 'F')
		{
			nibbleValue = c - 'A' + 10;
			nibbleMask = 0xF;
			return true;
		}

		nibbleValue = 0;
		nibbleMask = 0;
		error = $"'{c}' is not a hex digit or wildcard X.";
		return false;
	}

	private static char NibbleChar(int nibbleValue, int nibbleMask)
		=> nibbleMask == 0 ? 'X' : "0123456789ABCDEF"[nibbleValue & 0xF];
}