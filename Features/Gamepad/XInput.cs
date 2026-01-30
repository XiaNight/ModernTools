using System.Runtime.InteropServices;

namespace Gamepad
{
    public static class XInput
    {
        public const int XUSER_INDEX_ANY = 0xFF;
        public const int ERROR_SUCCESS = 0;
        public const int ERROR_EMPTY = 4306;
        public const int ERROR_DEVICE_NOT_CONNECTED = 1167;

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_STATE
        {
            public uint dwPacketNumber;
            public XINPUT_GAMEPAD Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_GAMEPAD
        {
            public ushort wButtons; // Bitmask of the buttons
            public byte bLeftTrigger; // Analog value of the left trigger
            public byte bRightTrigger; // Analog value of the right trigger
            public short sThumbLX; // X axis of the left thumbstick
            public short sThumbLY; // Y axis of the left thumbstick
            public short sThumbRX; // X axis of the right thumbstick
            public short sThumbRY; // Y axis of the right thumbstick
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;   // 0-65535
            public ushort wRightMotorSpeed;  // 0-65535
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_KEYSTROKE
        {
            public ushort VirtualKey;
            public char Unicode;
            public ushort Flags;
            public byte UserIndex;
            public byte HidCode;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        public static extern int XInputGetState(int dwUserIndex, out XINPUT_STATE pState);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        public static extern int XInputSetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetKeystroke")]
        public static extern int XInputGetKeystroke(int dwUserIndex, int dwReserved, out XINPUT_KEYSTROKE pKeystroke);
    }
}
