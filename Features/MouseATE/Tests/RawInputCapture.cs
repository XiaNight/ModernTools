using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MouseATE.Tests;

/// <summary>
/// Captures raw mouse WM_INPUT messages from the Windows message pump.
/// Register via <see cref="Start"/> before starting a movement, then call
/// <see cref="Stop"/> and read <see cref="Points"/> for collected (dx, dy) pairs.
/// </summary>
public class RawInputCapture
{
    private const int WM_INPUT = 0x00FF;
    private const uint RID_INPUT = 0x10000003;
    private const int RIM_TYPEMOUSE = 0;
    private const uint RIDEV_INPUTSINK = 0x00000100;
    private const uint RIDEV_REMOVE = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTHEADER
    {
        public uint dwType, dwSize;
        public IntPtr hDevice, wParam;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWMOUSE
    {
        public ushort usFlags, pad;
        public uint ulButtons;
        public ushort usButtonFlags, usButtonData;
        public uint ulRawButtons;
        public int lLastX, lLastY;
        public uint ulExtraInformation;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct RAWINPUT
    {
        [FieldOffset(0)] public RAWINPUTHEADER header;
        [FieldOffset(16)] public RAWMOUSE mouse;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAWINPUTDEVICE
    {
        public ushort usUsagePage, usUsage;
        public uint dwFlags;
        public IntPtr hwndTarget;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterRawInputDevices([In] RAWINPUTDEVICE[] devices, uint count, uint cbSize);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetRawInputData(IntPtr hRawInput, uint uiCommand, IntPtr pData, ref uint pcbSize, uint cbSizeHeader);

    private readonly List<(int dx, int dy)> _points = new();
    private bool _capturing;
    private readonly IntPtr _hwnd;

    public IReadOnlyList<(int dx, int dy)> Points => _points;

    public RawInputCapture(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    public void Start()
    {
        _points.Clear();
        _capturing = true;
        RegisterDevice(RIDEV_INPUTSINK);
    }

    public void Stop()
    {
        _capturing = false;
        RegisterDevice(RIDEV_REMOVE);
    }

    public void OnWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam)
    {
        if (!_capturing || msg != WM_INPUT) return;

        uint size = 0;
        GetRawInputData(lParam, RID_INPUT, IntPtr.Zero, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>());
        if (size == 0) return;

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            if (GetRawInputData(lParam, RID_INPUT, buf, ref size, (uint)Marshal.SizeOf<RAWINPUTHEADER>()) == size)
            {
                var raw = Marshal.PtrToStructure<RAWINPUT>(buf);
                if (raw.header.dwType == RIM_TYPEMOUSE)
                    _points.Add((raw.mouse.lLastX, raw.mouse.lLastY));
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private void RegisterDevice(uint flags)
    {
        var dev = new RAWINPUTDEVICE[]
        {
            new() { usUsagePage = 0x01, usUsage = 0x02, dwFlags = flags, hwndTarget = _hwnd }
        };
        RegisterRawInputDevices(dev, 1, (uint)Marshal.SizeOf<RAWINPUTDEVICE>());
    }
}
