namespace Camera;

public sealed class FrameSlot
{
	private byte[] data;

	public FrameSlot(int capacity)
	{
		data = new byte[capacity];
	}

	public byte[] Data => data;

	public int Width { get; set; }

	public int Height { get; set; }

	public int Stride { get; set; }

	public long Timestamp { get; set; }

	public Span<byte> Pixels => data.AsSpan(0, Stride * Height);

	public void EnsureCapacity(int requiredBytes)
	{
		if (data.Length >= requiredBytes) return;
		data = new byte[requiredBytes];
	}
}
