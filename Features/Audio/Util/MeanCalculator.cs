namespace Audio.Util
{
    public sealed class MeanCalculator
    {
        private long n;
        private double mean;
        public float Mean => (float)mean;
        public float Max { get; private set; }
        public float Min { get; private set; }

        public MeanCalculator()
        {
            Reset();
        }

        public void Reset()
        {
            n = 0;
            mean = 0.0;
            Max = float.MinValue;
            Min = float.MaxValue;
        }

        public void Push(float x)
        {
            n++;
            double dx = x - mean;
            mean += dx / n;
            double dx2 = x - mean;
            Max = Math.Max(Max, x);
            Min = Math.Min(Min, x);
        }
    }
}
