using System.Runtime.CompilerServices;
using Audio.Entries;
using Audio.Receiver;

namespace Audio.Trigger
{
    internal class VolumeTrigger
    {
        public long triggerDebounce = TimeSpan.FromSeconds(1).Ticks;
        public long lastTriggerTick = 0;
        public event Action<long> OnVolumeTriggered;

        public bool isLeftTriggered = false;
        public float leftUpperThreshold = -30f;
        public float leftLowerThreshold = -50f;

        public bool isRightTriggered = false;
        public float rightUpperThreshold = -30f;
        public float rightLowerThreshold = -50f;

        public TriggerType triggerType = TriggerType.UpperAndLower;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Parse(IEnumerable<AudioChannelHandler.TimedValue<float>> values)
        {
            bool checkUpper = (triggerType & TriggerType.UpperOnly) != 0;
            bool checkLower = (triggerType & TriggerType.LowerOnly) != 0;

            foreach (AudioChannelHandler.TimedValue<float> value in values)
            {
                long now = DateTime.Now.Ticks;
                bool canTrigger = now - lastTriggerTick > triggerDebounce;

                bool isLeftOutside =
                    (checkLower && value.Left < leftLowerThreshold) ||
                    (checkUpper && value.Left > leftUpperThreshold);

                if (!isLeftOutside)
                {
                    isLeftTriggered = false;
                }
                else if (!isLeftTriggered)
                {
                    if (canTrigger)
                    {
                        lastTriggerTick = now;
                        OnVolumeTriggered?.Invoke(value.Timestamp);
                    }
                    isLeftTriggered = true;
                }

                bool isRightOutside =
                    (checkLower && value.Right < rightLowerThreshold) ||
                    (checkUpper && value.Right > rightUpperThreshold);

                if (!isRightOutside)
                {
                    isRightTriggered = false;
                }
                else if (!isRightTriggered)
                {
                    if (canTrigger)
                    {
                        lastTriggerTick = now;
                        OnVolumeTriggered?.Invoke(value.Timestamp);
                    }
                    isRightTriggered = true;
                }
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
