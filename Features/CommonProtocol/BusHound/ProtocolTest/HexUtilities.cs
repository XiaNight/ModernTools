namespace CommonProtocol.BusHound.ProtocolTest;

using System.Collections.Generic;
using System.Text;

public static class HexFormat
{
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

	public bool ByteMatches(int index, byte actual, bool allowTrailing)
	{
		if (index >= value.Length)
			return allowTrailing;

		return (actual & mask[index]) == value[index];
	}

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
