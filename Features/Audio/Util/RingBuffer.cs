using System;
using System.Threading;

namespace Audio.Util
{
    public class RingBuffer<T>
    {
        public delegate void RefSetter(ref T value);

        private readonly T[] buffer;
        private readonly int mask;

        // _head: next write position
        // _tail: next read position (oldest item)
        private int head;
        private int tail;
        private int count;

        private readonly object gate = new object();

        public RingBuffer(int size, bool fill = false)
        {
            if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), "Size must be > 0.");
            if ((size & (size - 1)) != 0) throw new ArgumentException("Size must be a power of two.", nameof(size));

            buffer = new T[size];
            mask = size - 1;
            head = 0;
            tail = 0;
            count = 0;

            if(fill)
            {
                FillEmpty();
            }
        }

        private void FillEmpty()
        {
            lock (gate)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    if(typeof(T).IsValueType)
                    {
                        buffer[i] = default;
                    }
                    else
                    {
                        buffer[i] = Activator.CreateInstance<T>();
                    }
                }
                head = 0;
                tail = 0;
                count = 0;
            }
        }

        public void Fill(RefSetter setter)
        {
            if (setter == null) throw new ArgumentNullException(nameof(setter));
            lock (gate)
            {
                for (int i = 0; i < buffer.Length; i++)
                {
                    setter(ref buffer[i]);
                }
                head = 0;
                tail = 0;
                count = 0;
            }
        }

        public int Capacity => buffer.Length;

        public int Count => Volatile.Read(ref count);

        public bool IsEmpty => Count == 0;

        public bool IsFull => Count == buffer.Length;

        public void Enqueue(in T item)
        {
            lock (gate)
            {
                buffer[head] = item;
                head = (head + 1) & mask;

                if (count == buffer.Length)
                {
                    // overwrite oldest
                    tail = (tail + 1) & mask;
                }
                else
                {
                    count++;
                }
            }
        }

        /// <summary>
        /// In-place enqueue that can mutate/assign the slot by ref.
        /// Works for structs, classes, and primitives.
        /// </summary>
        public T EnqueueValue(RefSetter setter)
        {
            if (setter == null) throw new ArgumentNullException(nameof(setter));

            lock (gate)
            {
                var item = buffer[head];
                setter(ref buffer[head]);

                head = (head + 1) & mask;

                if (count == buffer.Length)
                {
                    // overwrite oldest
                    tail = (tail + 1) & mask;
                }
                else
                {
                    count++;
                }

                return item;
            }
        }

        public virtual bool TryPeek(out T item)
        {
            lock (gate)
            {
                if (count == 0)
                {
                    item = default!;
                    return false;
                }

                item = buffer[tail];
                return true;
            }
        }

        public bool TryPeek(out T item, int index)
        {
            lock (gate)
            {
                if (index < 0 || index >= count)
                {
                    item = default!;
                    return false;
                }
                int actualIndex = (tail + index) & mask;
                item = buffer[actualIndex];
                return true;
            }
        }

        public virtual bool TryDequeue(out T item)
        {
            lock (gate)
            {
                if (count == 0)
                {
                    item = default!;
                    return false;
                }

                item = buffer[tail];
                tail = (tail + 1) & mask;
                count--;
                return true;
            }
        }

        public void AdvanceTail(int n = 1)
        {
            if (n < 0) throw new ArgumentOutOfRangeException(nameof(n), "n must be non-negative.");
            lock (gate)
            {
                if (n > count) throw new InvalidOperationException("Cannot advance tail beyond the number of stored items.");
                tail = (tail + n) & mask;
                count -= n;
            }
        }

        public void Clear()
        {
            lock (gate)
            {
                head = 0;
                tail = 0;
                count = 0;
                Array.Clear(buffer, 0, buffer.Length);
            }
        }
    }
}
