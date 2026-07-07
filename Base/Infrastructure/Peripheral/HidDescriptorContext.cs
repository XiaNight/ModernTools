using Base.Services.Peripheral.Native;
using System.Text;

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
            // HidP_GetUsages sets `len` to the number of usages actually written;
            // trim to that instead of returning the full 32-slot scratch array.
            Array.Resize(ref usages, (int)len);
            return usages;
        }

        public bool TryGetUsageValue(byte[] report, ushort usagePage, ushort usage, out int value, ushort linkCollection = 0)
        {
            var st = HidNative.HidP_GetUsageValue(HidNative.HIDP_REPORT_TYPE.HidP_Input, usagePage, linkCollection, usage, out value, PreparsedData, report, (uint)report.Length);
            return NtSuccess(st);
        }

        public bool TryGetValueCap(ushort usagePage, ushort usage, out HidNative.HIDP_VALUE_CAPS cap)
        {
            var caps = new HidNative.HIDP_VALUE_CAPS[1];
            ushort length = 1;
            var st = HidNative.HidP_GetSpecificValueCaps(
                HidNative.HIDP_REPORT_TYPE.HidP_Input, usagePage, 0, usage, caps, ref length, PreparsedData);
            if (NtSuccess(st) && length > 0)
            {
                cap = caps[0];
                return true;
            }
            cap = default;
            return false;
        }

        /// <summary>
        /// Human-readable dump of what the device's report descriptor DECLARES for
        /// input reports (as parsed by the Windows HID parser). Compare this against
        /// the raw report bytes and the decoded usages to tell whether a wrong-place
        /// button/axis is caused by the device's descriptor/report layout or by the
        /// consumer's fixed usage→UI mapping.
        /// </summary>
        public string DescribeCapabilities()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"TopLevel usage=0x{Caps.UsagePage:X2}:0x{Caps.Usage:X2}  " +
                          $"InputReportByteLength={Caps.InputReportByteLength}  " +
                          $"ButtonCaps={Caps.NumberInputButtonCaps}  ValueCaps={Caps.NumberInputValueCaps}  " +
                          $"LinkCollectionNodes={Caps.NumberLinkCollectionNodes}");

            foreach (var b in ButtonCaps)
            {
                string usages = b.IsRange != 0
                    ? $"usages=0x{b.Range.UsageMin:X2}..0x{b.Range.UsageMax:X2}"
                    : $"usage=0x{b.NotRange.Usage:X2}";
                sb.AppendLine($"  BTN page=0x{b.UsagePage:X2} {usages} reportId={b.ReportID} " +
                              $"link={b.LinkCollection} linkUsage=0x{b.LinkUsagePage:X2}:0x{b.LinkUsage:X2} " +
                              $"abs={b.IsAbsolute}");
            }

            foreach (var v in ValueCaps)
            {
                string usages = v.IsRange != 0
                    ? $"usages=0x{v.Range.UsageMin:X2}..0x{v.Range.UsageMax:X2}"
                    : $"usage=0x{v.NotRange.Usage:X2}";
                sb.AppendLine($"  VAL page=0x{v.UsagePage:X2} {usages} bits={v.BitSize} count={v.ReportCount} " +
                              $"logical=[{v.LogicalMin}..{v.LogicalMax}] reportId={v.ReportID} " +
                              $"link={v.LinkCollection} linkUsage=0x{v.LinkUsagePage:X2}:0x{v.LinkUsage:X2} " +
                              $"abs={v.IsAbsolute} hasNull={v.HasNull}");
            }

            return sb.ToString();
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
