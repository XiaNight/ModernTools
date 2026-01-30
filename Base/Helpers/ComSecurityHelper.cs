using System;
using System.Runtime.InteropServices;

namespace Base.Helpers
{
    /// <summary>
    /// Helper class to initialize COM security for Bluetooth/WinRT APIs.
    /// </summary>
    public static class ComSecurityHelper
    {
        private static bool _initialized;

        [DllImport("ole32.dll", ExactSpelling = true, PreserveSig = false)]
        private static extern void CoInitializeSecurity(
            IntPtr pSecDesc,
            int cAuthSvc,
            IntPtr asAuthSvc,
            IntPtr pReserved1,
            uint dwAuthnLevel,
            uint dwImpLevel,
            IntPtr pAuthList,
            uint dwCapabilities,
            IntPtr pReserved3);

        /// <summary>
        /// Call once at app startup, before any Bluetooth/WinRT API.
        /// Uses default COM security.
        /// </summary>
        public static void Initialize()
        {
            if (_initialized) return;

            try
            {
                // Use default process-wide COM security.
                // RPC_C_AUTHN_LEVEL_DEFAULT = 0
                // RPC_C_IMP_LEVEL_IDENTIFY = 2
                // EOAC_NONE = 0
                CoInitializeSecurity(
                    IntPtr.Zero,
                    -1,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    0,
                    2,
                    IntPtr.Zero,
                    0,
                    IntPtr.Zero);
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80010119)) // RPC_E_TOO_LATE
            {
                // COM security already initialized by the runtime/framework; safe to continue.
            }

            _initialized = true;
        }
    }
}
