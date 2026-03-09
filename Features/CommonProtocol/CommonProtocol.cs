using Base.Core;
using Base.Services;
using Base.Services.Peripheral;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Threading;
using static Base.Services.DeviceSelection;

namespace CommonProtocol
{
    public class CommonProtocol : WpfBehaviourSingleton<CommonProtocol>
    {
        private PeripheralInterface activeInterface;


        public override void Awake()
        {
            base.Awake();

            DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToInterface;
            DeviceSelection.Instance.OnActiveDeviceDisconnected += DisconnectInterface;
        }

        private void ConnectToInterface()
        {
            var device = DeviceSelection.Instance.ActiveDevice;
            try
            {
                var usagePage = device.PID == 0x1ACE ? 0xFF02 : 0xFF00;
                if (device.interfaces.Count == 0) return;

                var deviceInterface = device.interfaces.FirstOrDefault(@interface =>
                    (@interface.UsagePage == usagePage) && (@interface.Usage == 1),
                    device.interfaces[0]
                );
                if (deviceInterface == null) return;

                activeInterface = deviceInterface.Connect(true);
                activeInterface.OnDataReceived += Parse;
            }
            catch (Exception ex)
            {
                Debug.Log("[CommonProtocol] Failed to open HID device: " + ex.Message);
                return;
            }
        }

        private void DisconnectInterface()
        {
            if (activeInterface == null) return;
            if (!activeInterface.IsDeviceConnected) return;
            activeInterface.OnDataReceived -= Parse;

            activeInterface = null;
        }

        private void Parse(ReadOnlyMemory<byte> arg1, DateTime arg2)
        {
            ReadOnlySpan<byte> span = arg1.Span;

            if (!ProtocolService.IsCmdMatch([0xFD, 0xA0], span)) return;

            ReadOnlySpan<byte> data = span.Slice(5);

            int length = data.IndexOf((byte)0);
            if (length < 0)
            {
                length = data.Length;
            }
            if(length == 0) return;

            string message = System.Text.Encoding.ASCII.GetString(data.Slice(0, length));
            Debug.Log(message);
        }
    }
}
