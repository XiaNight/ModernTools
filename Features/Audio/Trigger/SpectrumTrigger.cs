using Audio.Util;

namespace Audio.Trigger
{
    internal class SpectrumTrigger
    {
        public long triggerDebounce = TimeSpan.FromSeconds(1).Ticks;
        public long lastTriggerTick = 0;
        public event Action OnTriggered;

        public bool isLeftTriggered = false;
        public bool isRightTriggered = false;

        private readonly int bandWidth;
        public readonly float[] leftUpperThresholds;
        public readonly float[] leftLowerThresholds;

        public readonly float[] rightUpperThresholds;
        public readonly float[] rightLowerThresholds;

        public float AdaptationStdFactor { get; set; }

        private readonly MeanCalculator[] leftStats;
        private readonly MeanCalculator[] rightStats;

        public TriggerType triggerType = TriggerType.UpperAndLower;

        public SpectrumTrigger(int fftWidth, float adaptStd = 3f)
        {
            this.bandWidth = fftWidth;
            this.AdaptationStdFactor = adaptStd;

            leftUpperThresholds = new float[fftWidth];
            leftLowerThresholds = new float[fftWidth];
            rightUpperThresholds = new float[fftWidth];
            rightLowerThresholds = new float[fftWidth];

            leftStats = new MeanCalculator[bandWidth];
            rightStats = new MeanCalculator[bandWidth];
            for (int i = 0; i < bandWidth; i++)
            {
                leftStats[i] = new MeanCalculator();
                rightStats[i] = new MeanCalculator();
            }
        }

        public void StartAdapting()
        {
            for (int band = 0; band < bandWidth; band++)
            {
                leftStats[band].Reset();
                rightStats[band].Reset();
            }
        }

        public void Adapt(float[] left, float[] right)
        {
            for (int band = 0; band < bandWidth; band++)
            {
                leftStats[band].Push(left[band]);
                rightStats[band].Push(right[band]);
            }
        }

        public void CalculateThresholds()
        {
            for (int band = 0; band < bandWidth; band++)
            {
                float leftMean = leftStats[band].Mean;
                float leftMax = leftStats[band].Max;
                float leftMin = leftStats[band].Min;
                leftUpperThresholds[band] = leftMean + AdaptationStdFactor * (leftMax - leftMean);
                leftLowerThresholds[band] = leftMean - AdaptationStdFactor * (leftMean - leftMin);

                float rightMean = rightStats[band].Mean;
                float rightMax = rightStats[band].Max;
                float rightMin = rightStats[band].Min;
                rightUpperThresholds[band] = rightMean + AdaptationStdFactor * (rightMax - rightMean);
                rightLowerThresholds[band] = rightMean - AdaptationStdFactor * (rightMean - rightMin);
            }
        }

        public void FinishAdapting()
        {
            for (int band = 0; band < bandWidth; band++)
            {
                leftStats[band].Reset();
                rightStats[band].Reset();
            }
        }

        public void Parse(float[] left, float[] right)
        {
            long now = DateTime.Now.Ticks;

            bool checkUpper = (triggerType & TriggerType.UpperOnly) != 0;
            bool checkLower = (triggerType & TriggerType.LowerOnly) != 0;

            bool isLeftOutside = false;
            for (int i = 0; i < bandWidth; i++)
            {
                float v = left[i];
                if ((checkLower && v < leftLowerThresholds[i]) ||
                    (checkUpper && v > leftUpperThresholds[i]))
                {
                    isLeftOutside = true;
                    break;
                }
            }

            if (!isLeftOutside)
            {
                isLeftTriggered = false;
            }
            else if (!isLeftTriggered)
            {
                if (now - lastTriggerTick > triggerDebounce)
                {
                    lastTriggerTick = now;
                    OnTriggered?.Invoke();
                }
                isLeftTriggered = true;
            }

            bool isRightOutside = false;
            for (int i = 0; i < bandWidth; i++)
            {
                float v = right[i];
                if ((checkLower && v < rightLowerThresholds[i]) ||
                    (checkUpper && v > rightUpperThresholds[i]))
                {
                    isRightOutside = true;
                    break;
                }
            }

            if (!isRightOutside)
            {
                isRightTriggered = false;
            }
            else if (!isRightTriggered)
            {
                if (now - lastTriggerTick > triggerDebounce)
                {
                    lastTriggerTick = now;
                    OnTriggered?.Invoke();
                }
                isRightTriggered = true;
            }
        }

        [Flags]
        public enum TriggerType
        {
            UpperOnly = 1,
            LowerOnly = 2,
            UpperAndLower = UpperOnly | LowerOnly
        }
    }
}
