namespace CommonProtocol;

using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// A single Bus Hound protocol test: a request frame plus the sequence of USB packets
/// its reply must match. Matching is strict — the returned bytes must equal the expected
/// bytes exactly, with the single exception of the wildcard nibble <c>X</c>, which matches
/// any hex value in that nibble position. Each expected line describes one USB packet, so
/// a three-line expectation requires three packets, each matching its line in order.
/// </summary>
public sealed class ProtocolTest
{
	public string Id { get; set; } = Guid.NewGuid().ToString("N");

	public string Name { get; set; } = "New test";

	/// <summary>Request bytes as a hex string, e.g. "02 00 B5 00".</summary>
	public string RequestHex { get; set; } = string.Empty;

	/// <summary>
	/// One expected packet per line. Each line is space-separated hex bytes where any
	/// nibble may be the wildcard <c>X</c> (e.g. "02 F4 XX 1X").
	/// </summary>
	public List<string> ExpectedLines { get; set; } = new();

	/// <summary>Total time budget, in milliseconds, to receive and match every expected packet.</summary>
	public int TotalTimeoutMs { get; set; } = 1000;

	/// <summary>Short, single-line summary of the request and expected packet count for the row preview.</summary>
	public string BuildPreview()
	{
		string request = string.IsNullOrWhiteSpace(RequestHex) ? "(no request)" : RequestHex.Trim();
		int count = ExpectedLines?.Count ?? 0;
		return $"TX {request}   →   {count} packet(s)   ·   {TotalTimeoutMs} ms";
	}
}

/// <summary>
/// A parsed expected packet: the fixed byte values together with a per-nibble mask marking
/// which nibbles are fixed (0xF) versus wildcard (0x0). A received packet matches when it has
/// the same length and every fixed nibble is equal.
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
	/// Parses one expected line into an <see cref="ExpectedPacket"/>. Tokens are separated by
	/// whitespace and each must be exactly two characters, where each character is a hex digit
	/// or the wildcard <c>X</c>/<c>x</c>. Returns false with a reason on malformed input.
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

		string[] tokens = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
		if (tokens.Length == 0)
		{
			error = "Line has no bytes.";
			return false;
		}

		byte[] values = new byte[tokens.Length];
		byte[] masks = new byte[tokens.Length];

		for (int i = 0; i < tokens.Length; i++)
		{
			string token = tokens[i];
			if (token.Length != 2)
			{
				error = $"'{token}' is not a two-character byte.";
				return false;
			}

			if (!TryNibble(token[0], out int hiValue, out int hiMask, out error))
				return false;
			if (!TryNibble(token[1], out int loValue, out int loMask, out error))
				return false;

			values[i] = (byte)((hiValue << 4) | loValue);
			masks[i] = (byte)((hiMask << 4) | loMask);
		}

		packet = new ExpectedPacket(values, masks);
		return true;
	}

	/// <summary>True when <paramref name="actual"/> has the same length and matches every fixed nibble.</summary>
	public bool Matches(byte[] actual)
	{
		if (actual == null || actual.Length != value.Length)
			return false;

		for (int i = 0; i < value.Length; i++)
		{
			if ((actual[i] & mask[i]) != value[i])
				return false;
		}

		return true;
	}

	/// <summary>Human-readable form, e.g. "02 F4 XX 1X", for failure messages.</summary>
	public override string ToString()
	{
		StringBuilder sb = new();
		for (int i = 0; i < value.Length; i++)
		{
			if (i > 0) sb.Append(' ');
			sb.Append(NibbleChar((value[i] >> 4) & 0xF, (mask[i] >> 4) & 0xF));
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