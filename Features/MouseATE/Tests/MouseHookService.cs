using System.Runtime.InteropServices;

namespace MouseATE.Tests;

public enum MouseButton { Left, Right }

public class MouseHookService : IDisposable
{
    private const int WH_MOUSE_LL    = 14;
    private const int WM_LBUTTONDOWN = 0x0201;
    private const int WM_RBUTTONDOWN = 0x0204;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc callback, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    private IntPtr _hookHandle = IntPtr.Zero;
    private LowLevelMouseProc _proc;   // must be held to prevent GC

    private int _leftCount;
    private int _rightCount;

    public bool IsInstalled => _hookHandle != IntPtr.Zero;

    // Must be called from the UI thread (WPF message pump)
    public void Install()
    {
        if (IsInstalled) return;
        _proc = HookCallback;
        IntPtr hMod = Marshal.GetHINSTANCE(typeof(MouseHookService).Module);
        _hookHandle = SetWindowsHookEx(WH_MOUSE_LL, _proc, hMod, 0);
    }

    public void Uninstall()
    {
        if (!IsInstalled) return;
        UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
    }

    public void ResetCount(MouseButton btn)
    {
        if (btn == MouseButton.Left)  Interlocked.Exchange(ref _leftCount, 0);
        else                           Interlocked.Exchange(ref _rightCount, 0);
    }

    public int GetCount(MouseButton btn) =>
        btn == MouseButton.Left ? Volatile.Read(ref _leftCount) : Volatile.Read(ref _rightCount);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            if (wParam == (IntPtr)WM_LBUTTONDOWN) Interlocked.Increment(ref _leftCount);
            else if (wParam == (IntPtr)WM_RBUTTONDOWN) Interlocked.Increment(ref _rightCount);
        }
        return CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    public void Dispose() => Uninstall();
}
