using System;
using System.Runtime.InteropServices;

namespace AntiDuneKeyboardDiddler
{
    internal static class NativeMethods
    {
        public const uint SPI_GETDEFAULTINPUTLANG = 0x0059;
        public const uint SPI_SETDEFAULTINPUTLANG = 0x005A;
        public const uint SPIF_SENDCHANGE = 0x0002;

        public const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;
        public const uint INPUTLANGCHANGE_SYSCHARSET = 0x0001;

        public const uint KLF_SETFORPROCESS = 0x00000100;

        // Passed to AttachConsole to borrow the console of whatever launched us, so that
        // --status and --cleanup still print something when run from a terminal. A windows
        // subsystem application has no console of its own.
        public const int ATTACH_PARENT_PROCESS = -1;

        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetKeyboardLayoutList(int nBuff, [Out] IntPtr[] lpList);

        [DllImport("user32.dll")]
        public static extern IntPtr GetKeyboardLayout(uint idThread);

        [DllImport("user32.dll")]
        public static extern IntPtr ActivateKeyboardLayout(IntPtr hkl, uint Flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UnloadKeyboardLayout(IntPtr hkl);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SystemParametersInfoW(uint uiAction, uint uiParam, ref IntPtr pvParam, uint fWinIni);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AttachConsole(int dwProcessId);
    }
}
