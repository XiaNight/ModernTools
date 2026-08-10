using Base.Helpers;

namespace Base.Protocol;

// Helpers for building an outgoing request frame and for reading a received one.
//
// Frame layout (matches the ASUS common protocol used across ROG peripherals):
//   Byte 0      Command
//   Byte 1      Key
//   Byte 2..3   Index
//   Byte 4..    Data / parameters
//
// A received HID report additionally carries a leading Report-ID byte at index 0,
// so the protocol frame begins at response[1]. This mirrors the convention used by
// Listener / ProtocolService.IsCmdMatch, which slice one leading byte before matching.
public static class ProtocolFrame
{
	public const int CommandOffset = 0;
	public const int KeyOffset = 1;
	public const int IndexOffset = 2;
	public const int DataOffset = 4;

	/// <summary>
	/// Builds a request frame: [Command, Key] (+ index bytes) (+ parameter bytes).
	/// When parameters are present but no index is supplied, two zero index bytes are
	/// inserted so the parameters still land at Byte 4 as the protocol requires.
	/// </summary>
	public static byte[] Build(byte command, byte key, ushort index, byte[] paramBytes = null)
	{
		byte[] param = paramBytes ?? Array.Empty<byte>();

		byte[] frame = new byte[2 + 2 + param.Length];
		frame[CommandOffset] = command;
		frame[KeyOffset] = key;

		frame[IndexOffset] = index.LowByte();
		frame[IndexOffset + 1] = index.HighByte();

		if (param.Length > 0)
			Buffer.BlockCopy(param, 0, frame, 2 + 2, param.Length);

		return frame;
	}

	/// <summary>
	/// Returns the protocol frame carried by a received report (drops the leading
	/// Report-ID byte). Returns an empty span when the report is null/empty.
	/// </summary>
	public static ReadOnlySpan<byte> BodyOf(ReadOnlySpan<byte> report)
		=> report.Length <= 1 ? ReadOnlySpan<byte>.Empty : report[1..];

	public static string ToHex(byte[] bytes)
		=> bytes == null || bytes.Length == 0 ? string.Empty : BitConverter.ToString(bytes);

	public static string ToHex(ReadOnlySpan<byte> bytes)
		=> bytes.Length == 0 ? string.Empty : BitConverter.ToString(bytes.ToArray());
}
