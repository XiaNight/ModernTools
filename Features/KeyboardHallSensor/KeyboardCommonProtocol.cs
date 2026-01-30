using Base.Core;
using Base.Services;
using Base.Services.Peripheral;

namespace KeyboardHallSensor
{
    public class KeyboardCommonProtocol : WpfBehaviourSingleton<KeyboardCommonProtocol>
    {
        public PeripheralInterface ActiveInterface { get; private set; }
        public event Action OnInterfaceConnected;
        public event Action OnInterfaceDisconnected;

        private RawPage rawPage;
        private AnalogPage analogPage;
        private SegmentPage segmentPage;
        private CustomizedSegmentPage customizedSegmentPage;
        private GainPage gainPage;
        private BottomAveragePage bottomAveragePage;
        private BaseLineTopPage baseLineTopPage;
        private BaseLineBottomPage baseLineBottomPage;

        public override void Awake()
        {
            base.Awake();

            DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToInterface;

            rawPage = new RawPage();
            analogPage = new AnalogPage();
            segmentPage = new SegmentPage();
            customizedSegmentPage = new CustomizedSegmentPage();
            gainPage = new GainPage();
            bottomAveragePage = new BottomAveragePage();
            baseLineTopPage = new BaseLineTopPage();
            baseLineBottomPage = new BaseLineBottomPage();

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

                ActiveInterface = deviceInterface.Connect(true);
                OnSpecificDeviceConnectedAction(device.PID);

                //- Connection closed / unplugged or died
                ActiveInterface.OnDisconnected += DisconnectInterface;
            }
            catch (Exception ex)
            {
                Debug.Log("[Keyboard] Failed to open HID device: " + ex.Message);
                return;
            }

            Debug.Log($"[HID] Connected to {ActiveInterface.ProductInfo.Product}");

            ProtocalService.EnterFactory(ActiveInterface);
            ProtocalService.EnterHallSensor(ActiveInterface);

            OnInterfaceConnected?.Invoke();
        }


        private void OnSpecificDeviceConnectedAction(ushort pid)
        {
            switch (pid)
            {
                case 0x1B7E: // M605
                    segmentPage.SetMgfCmdPackageSize(2);
                    break;
            }
        }

        private async void DisconnectInterface()
        {
            if (ActiveInterface == null) return;
            if (!ActiveInterface.IsDeviceConnected) return;
            OnInterfaceDisconnected?.Invoke();

            await Task.Run(() =>
            {
                //- Pre closing sequence
                ProtocalService.ExitHallSensor(ActiveInterface);
                ProtocalService.ExitFactory(ActiveInterface);
                ProtocalService.WaitForFinish();

                //- Close the device
                if (ActiveInterface == null) return;
                ActiveInterface.Close();
                ActiveInterface = null;

                Debug.Log("[HID] Disconnected");
            });
            ProtocalService.ClearCmd();
        }
    }
}
