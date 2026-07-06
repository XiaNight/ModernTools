using Base.Framework.Utilities;
using Xunit;

namespace ModernTools.Tests;

/// <summary>
/// Tests for <see cref="RingBuffer{T}"/> — a fixed-capacity, power-of-two,
/// overwrite-on-full circular buffer. Focuses on the wraparound, full/empty,
/// peek-by-index and AdvanceTail edge cases that are easy to get wrong.
/// </summary>
public class RingBufferTests
{
    [Fact]
    public void Constructor_RejectsNonPositiveSize()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new RingBuffer<int>(-4));
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(100)]
    public void Constructor_RejectsNonPowerOfTwoSize(int size)
    {
        Assert.Throws<ArgumentException>(() => new RingBuffer<int>(size));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(8)]
    [InlineData(1024)]
    public void Constructor_AcceptsPowerOfTwoSize(int size)
    {
        var rb = new RingBuffer<int>(size);
        Assert.Equal(size, rb.Capacity);
        Assert.Equal(0, rb.Count);
        Assert.True(rb.IsEmpty);
        Assert.False(rb.IsFull);
    }

    [Fact]
    public void Enqueue_IncrementsCountAndReportsFull()
    {
        var rb = new RingBuffer<int>(4);

        rb.Enqueue(1);
        rb.Enqueue(2);
        Assert.Equal(2, rb.Count);
        Assert.False(rb.IsEmpty);
        Assert.False(rb.IsFull);

        rb.Enqueue(3);
        rb.Enqueue(4);
        Assert.Equal(4, rb.Count);
        Assert.True(rb.IsFull);
    }

    [Fact]
    public void TryDequeue_ReturnsItemsInFifoOrder()
    {
        var rb = new RingBuffer<int>(4);
        rb.Enqueue(10);
        rb.Enqueue(20);
        rb.Enqueue(30);

        Assert.True(rb.TryDequeue(out int a));
        Assert.Equal(10, a);
        Assert.True(rb.TryDequeue(out int b));
        Assert.Equal(20, b);
        Assert.True(rb.TryDequeue(out int c));
        Assert.Equal(30, c);

        Assert.False(rb.TryDequeue(out int _));
        Assert.True(rb.IsEmpty);
    }

    [Fact]
    public void TryDequeue_OnEmpty_ReturnsFalseAndDefault()
    {
        var rb = new RingBuffer<int>(2);
        Assert.False(rb.TryDequeue(out int item));
        Assert.Equal(0, item);
    }

    [Fact]
    public void Enqueue_WhenFull_OverwritesOldest()
    {
        var rb = new RingBuffer<int>(4);
        for (int i = 1; i <= 4; i++) rb.Enqueue(i); // [1,2,3,4]

        rb.Enqueue(5); // overwrites 1 -> oldest is now 2
        rb.Enqueue(6); // overwrites 2 -> oldest is now 3

        Assert.Equal(4, rb.Count);
        Assert.True(rb.IsFull);

        Assert.True(rb.TryDequeue(out int oldest));
        Assert.Equal(3, oldest);

        Assert.True(rb.TryDequeue(out int next));
        Assert.Equal(4, next);
        Assert.True(rb.TryDequeue(out _)); // 5
        Assert.True(rb.TryDequeue(out int last));
        Assert.Equal(6, last);
    }

    [Fact]
    public void EnqueueDequeue_WrapsAroundBufferBoundary()
    {
        // Advance head/tail past the end so subsequent writes wrap.
        var rb = new RingBuffer<int>(4);
        for (int i = 0; i < 3; i++) { rb.Enqueue(i); rb.TryDequeue(out _); }

        // head/tail now at index 3; these two enqueues wrap to index 0.
        rb.Enqueue(100);
        rb.Enqueue(200);

        Assert.Equal(2, rb.Count);
        Assert.True(rb.TryDequeue(out int first));
        Assert.Equal(100, first);
        Assert.True(rb.TryDequeue(out int second));
        Assert.Equal(200, second);
    }

    [Fact]
    public void TryPeek_ReturnsOldestWithoutRemoving()
    {
        var rb = new RingBuffer<int>(4);
        rb.Enqueue(7);
        rb.Enqueue(8);

        Assert.True(rb.TryPeek(out int peeked));
        Assert.Equal(7, peeked);
        Assert.Equal(2, rb.Count); // unchanged
    }

    [Fact]
    public void TryPeek_OnEmpty_ReturnsFalse()
    {
        var rb = new RingBuffer<int>(2);
        Assert.False(rb.TryPeek(out int item));
        Assert.Equal(0, item);
    }

    [Fact]
    public void TryPeekByIndex_ReturnsItemAtLogicalOffset()
    {
        var rb = new RingBuffer<int>(4);
        rb.Enqueue(10);
        rb.Enqueue(20);
        rb.Enqueue(30);

        Assert.True(rb.TryPeek(out int i0, 0));
        Assert.Equal(10, i0);
        Assert.True(rb.TryPeek(out int i2, 2));
        Assert.Equal(30, i2);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(3)]
    [InlineData(99)]
    public void TryPeekByIndex_OutOfRange_ReturnsFalse(int index)
    {
        var rb = new RingBuffer<int>(4);
        rb.Enqueue(10);
        rb.Enqueue(20);
        rb.Enqueue(30);

        Assert.False(rb.TryPeek(out int item, index));
        Assert.Equal(0, item);
    }

    [Fact]
    public void AdvanceTail_DropsOldestItems()
    {
        var rb = new RingBuffer<int>(8);
        for (int i = 1; i <= 5; i++) rb.Enqueue(i);

        rb.AdvanceTail(2); // drop 1, 2

        Assert.Equal(3, rb.Count);
        Assert.True(rb.TryDequeue(out int head));
        Assert.Equal(3, head);
    }

    [Fact]
    public void AdvanceTail_NegativeThrows()
    {
        var rb = new RingBuffer<int>(4);
        rb.Enqueue(1);
        Assert.Throws<ArgumentOutOfRangeException>(() => rb.AdvanceTail(-1));
    }

    [Fact]
    public void AdvanceTail_BeyondCountThrows()
    {
        var rb = new RingBuffer<int>(4);
        rb.Enqueue(1);
        rb.Enqueue(2);
        Assert.Throws<InvalidOperationException>(() => rb.AdvanceTail(3));
    }

    [Fact]
    public void Clear_ResetsToEmpty()
    {
        var rb = new RingBuffer<int>(4);
        rb.Enqueue(1);
        rb.Enqueue(2);

        rb.Clear();

        Assert.Equal(0, rb.Count);
        Assert.True(rb.IsEmpty);
        Assert.False(rb.TryPeek(out _));
    }

    [Fact]
    public void EnqueueValue_MutatesSlotByRefAndReturnsValue()
    {
        var rb = new RingBuffer<int>(4);
        int returned = rb.EnqueueValue((ref int v) => v = 42);

        Assert.Equal(42, returned);
        Assert.Equal(1, rb.Count);
        Assert.True(rb.TryPeek(out int peeked));
        Assert.Equal(42, peeked);
    }

    [Fact]
    public void EnqueueValue_NullSetterThrows()
    {
        var rb = new RingBuffer<int>(4);
        Assert.Throws<ArgumentNullException>(() => rb.EnqueueValue(null));
    }

    [Fact]
    public void Fill_SeedsEverySlotAndResetsToEmpty()
    {
        var rb = new RingBuffer<int>(4);
        rb.Fill((ref int v) => v = -1);

        // Fill resets logical state to empty even though slots are seeded.
        Assert.Equal(0, rb.Count);
        Assert.True(rb.IsEmpty);
    }

    [Fact]
    public void Fill_NullSetterThrows()
    {
        var rb = new RingBuffer<int>(4);
        Assert.Throws<ArgumentNullException>(() => rb.Fill(null));
    }
}
