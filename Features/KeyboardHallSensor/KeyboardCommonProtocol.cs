using Base.Core;
using Base.Pages;
using Base.Services;
using Base.Services.Peripheral;

namespace KeyboardHallSensor
{
    public class KeyboardCommonProtocol : WpfBehaviourSingleton<KeyboardCommonProtocol>
    {
        public PeripheralInterface ActiveInterface { get; private set; }
        public event Action OnInterfaceConnected;
        public event Action OnInterfaceDisconnected;

        public override void Awake()
        {
            base.Awake();

            Main.OnPageChanged += OnPageChanged;
        }

        private void OnPageChanged(PageBase previousPage, PageBase currentPage)
        {
            bool isPreviousKeyboardPage = previousPage != null && typeof(IKeyboardPage).IsAssignableFrom(previousPage.GetType());
            bool isCurrentKeyboardPage = currentPage != null && typeof(IKeyboardPage).IsAssignableFrom(currentPage.GetType());

            if (isPreviousKeyboardPage)
            {
                if (isCurrentKeyboardPage)
                {
                    //- Still on keyboard pages: notify the previous page to exit its mode
                    OnInterfaceDisconnected?.Invoke();
                }
                else
                {
                    //- Left keyboard page, disconnect interface
                    DeviceSelection.Instance.OnActiveDeviceConnected -= ConnectToInterface;
                    DeviceSelection.Instance.OnActiveDeviceDisconnected -= DisconnectInterface;
                    DisconnectInterface();
                }
            }
            else if (isCurrentKeyboardPage)
            {
                //- Entered keyboard page, connect to interface
                if (DeviceSelection.Instance.ActiveDevice != null)
                {
                    ConnectToInterface();
                }
                DeviceSelection.Instance.OnActiveDeviceConnected += ConnectToInterface;
                DeviceSelection.Instance.OnActiveDeviceDisconnected += DisconnectInterface;
            }
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
            }
            catch (Exception ex)
            {
                Debug.Log("[Keyboard] Failed to open HID device: " + ex.Message);
                return;
            }

            Debug.Log($"[HID] Connected to {ActiveInterface.ProductInfo.Product}");

            ProtocolService.EnterFactory(ActiveInterface);
            ProtocolService.EnterHallSensor(ActiveInterface);

            OnInterfaceConnected?.Invoke();
        }


        private void OnSpecificDeviceConnectedAction(ushort pid)
        {
            //switch (pid)
            //{
            //    case 0x1B7E: // M605
            //        segmentPage.SetMgfCmdPackageSize(2);
            //        break;
            //}
        }

        private async void DisconnectInterface()
        {
            if (ActiveInterface == null) return;

            var interfaceSnapshot = ActiveInterface;
            bool canSendCmds = interfaceSnapshot.IsDeviceConnected;

            //- Clear any pending commands before queuing the exit sequence
            ProtocolService.ClearCmd();

            if (!canSendCmds)
            {
                //- Device is already gone; null out so Exit() skips sending protocol commands
                ActiveInterface = null;
            }

            //- Notify keyboard pages to run their exit protocol (e.g., ExitHallProdTest).
            //- When connected, ActiveInterface is still set so Exit() can queue commands.
            //- When disconnected, ActiveInterface is already null so Exit() only unsubscribes.
            OnInterfaceDisconnected?.Invoke();

            //- Null out immediately to prevent duplicate calls (e.g. from OnDisable)
            ActiveInterface = null;

            if (canSendCmds)
            {
                await Task.Run(() =>
                {
                    //- Pre closing sequence (reverse order of entry)
                    ProtocolService.ExitHallSensor(interfaceSnapshot);
                    ProtocolService.ExitFactory(interfaceSnapshot);
                });
            }

            Debug.Log("[HID] Disconnected");
        }
    }
}
