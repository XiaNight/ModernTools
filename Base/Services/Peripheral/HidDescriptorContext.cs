using Base.Services.Peripheral.Native;

namespace Base.Services.Peripheral
{
    internal sealed class HidDescriptorContext : IDisposable
    {
        public IntPtr PreparsedData { get; private set; } = IntPtr.Zero;
        public HidNative.HIDP_CAPS Caps { get; private set; }
        public HidNative.HIDP_BUTTON_CAPS[] ButtonCaps { get; private set; }
        public HidNative.HIDP_VALUE_CAPS[] ValueCaps { get; private set; }

        private static bool NtSuccess(int status) => status >= 0;

        public HidDescriptorContext(IntPtr deviceHandle)
        {
            PreparsedData = GetPreparsedData(deviceHandle);
            Caps = GetCaps();
            ButtonCaps = GetInputButtonCaps();
            ValueCaps = GetInputValueCaps();
        }

        private IntPtr GetPreparsedData(IntPtr deviceHandle)
        {
            IntPtr pp = IntPtr.Zero;
            if (!HidNative.HidD_GetPreparsedData(deviceHandle, ref pp))
                throw new InvalidOperationException("HidD_GetPreparsedData failed.");
            return pp;
        }

        private HidNative.HIDP_CAPS GetCaps()
        {
            var caps = new HidNative.HIDP_CAPS { Reserved = new ushort[17] };
            var st = HidNative.HidP_GetCaps(PreparsedData, ref caps);
            if (!NtSuccess(st))
                throw new InvalidOperationException($"HidP_GetCaps failed, NTSTATUS=0x{st:X8}");
            return caps;
        }

        private HidNative.HIDP_BUTTON_CAPS[] GetInputButtonCaps()
        {
            ushort len = Caps.NumberInputButtonCaps;
            if (len == 0) return Array.Empty<HidNative.HIDP_BUTTON_CAPS>();
            var arr = new HidNative.HIDP_BUTTON_CAPS[Math.Max(len, (ushort)32)];
            _ = HidNative.HidP_GetButtonCaps(HidNative.HIDP_REPORT_TYPE.HidP_Input, arr, ref len, PreparsedData);
            Array.Resize(ref arr, len);
            return arr;
        }

        private HidNative.HIDP_VALUE_CAPS[] GetInputValueCaps()
        {
            ushort len = Caps.NumberInputValueCaps;
            if (len == 0) return Array.Empty<HidNative.HIDP_VALUE_CAPS>();
            var arr = new HidNative.HIDP_VALUE_CAPS[Math.Max(len, (ushort)32)];
            _ = HidNative.HidP_GetValueCaps(HidNative.HIDP_REPORT_TYPE.HidP_Input, arr, ref len, PreparsedData);
            Array.Resize(ref arr, len);
            return arr;
        }

        public ushort[] GetPressedButtons(byte[] report, ushort usagePage = 0x09, ushort linkCollection = 0)
        {
            var capIndex = Array.FindIndex(ButtonCaps, c => c.UsagePage == usagePage && c.LinkCollection == linkCollection);
            if (capIndex < 0) return Array.Empty<ushort>();
            HidNative.HIDP_BUTTON_CAPS cap = ButtonCaps[capIndex];
            ushort[] usages = new ushort[32];
            var len = (uint)usages.Length;

            var st = HidNative.HidP_GetUsages(HidNative.HIDP_REPORT_TYPE.HidP_Input, usagePage, linkCollection, usages, ref len, PreparsedData, report, (uint)report.Length);
            if (!NtSuccess(st)) return Array.Empty<ushort>();
            Array.Resize(ref usages, usages.Length);
            return usages;
        }

        public bool TryGetUsageValue(byte[] report, ushort usagePage, ushort usage, out int value, ushort linkCollection = 0)
        {
            var st = HidNative.HidP_GetUsageValue(HidNative.HIDP_REPORT_TYPE.HidP_Input, usagePage, linkCollection, usage, out value, PreparsedData, report, (uint)report.Length);
            return NtSuccess(st);
        }

        public void Dispose()
        {
            if (PreparsedData != IntPtr.Zero)
            {
                var tmp = PreparsedData;
                HidNative.HidD_FreePreparsedData(ref tmp);
                PreparsedData = IntPtr.Zero;
            }
        }
    }
}
