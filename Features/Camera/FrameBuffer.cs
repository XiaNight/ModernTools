using System.Diagnostics;
using Base.Framework.Utilities;

namespace Camera;

public sealed class FrameBuffer
{
	private readonly RingBuffer<FrameSlot> ring;

	public FrameBuffer(int frameCount, int pixelCapacity)
	{
		ring = new RingBuffer<FrameSlot>(frameCount);
		ring.Fill((ref FrameSlot slot) =>
		{
			slot = new FrameSlot(pixelCapacity);
		});
	}

	public int Count => ring.Count;

	public int Capacity => ring.Capacity;

	public FrameSlot Push(int width, int height, int stride)
	{
		return ring.EnqueueValue((ref FrameSlot slot) =>
		{
			slot.EnsureCapacity(stride * height);
			slot.Width = width;
			slot.Height = height;
			slot.Stride = stride;
			slot.Timestamp = Stopwatch.GetTimestamp();
		});
	}
}
