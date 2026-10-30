using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace DynamicIsland.Helpers
{
    public static class NativeMethods
    {
        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_APPWINDOW = 0x00040000;

        public const int WM_APPCOMMAND = 0x0319;
        public const int APPCOMMAND_VOLUME_MUTE = 0x80000;
        public const int APPCOMMAND_VOLUME_DOWN = 0x90000;
        public const int APPCOMMAND_VOLUME_UP = 0xA0000;
        public const int APPCOMMAND_MEDIA_NEXTTRACK = 0xB0000;
        public const int APPCOMMAND_MEDIA_PREVIOUSTRACK = 0xC0000;
        public const int APPCOMMAND_MEDIA_PLAY_PAUSE = 0xE0000;

        public const int WM_CLIPBOARDUPDATE = 0x031D;
        public const int VK_CAPITAL = 0x14; // Caps Lock
        public const int VK_NUMLOCK = 0x90; // Num Lock

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "GetWindowLong", SetLastError = true)]
        private static extern IntPtr GetWindowLongPtr32(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
        private static extern IntPtr SetWindowLongPtr32(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessageW(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
        {
            if (IntPtr.Size == 8)
                return GetWindowLongPtr64(hWnd, nIndex);
            return GetWindowLongPtr32(hWnd, nIndex);
        }

        public static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
        {
            if (IntPtr.Size == 8)
                return SetWindowLongPtr64(hWnd, nIndex, dwNewLong);
            return SetWindowLongPtr32(hWnd, nIndex, dwNewLong);
        }

        /// <summary>
        /// Configures the window as a tool window so it will not appear in the Alt+Tab dialog.
        /// </summary>
        public static void HideFromAltTab(IntPtr hWnd)
        {
            long exStyle = GetWindowLongPtr(hWnd, GWL_EXSTYLE).ToInt64();
            exStyle |= WS_EX_TOOLWINDOW;
            exStyle &= ~WS_EX_APPWINDOW;
            SetWindowLongPtr(hWnd, GWL_EXSTYLE, new IntPtr(exStyle));
        }

        public const int SW_RESTORE = 9;
        public const int SW_MINIMIZE = 6;
        public const int SW_SHOW = 5;

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsZoomed(IntPtr hWnd);

        public static bool IsForegroundTrueFullScreen(IntPtr myHwnd, int monLeft, int monTop, int monWidth, int monHeight)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == myHwnd) return false;

            IntPtr shell = GetShellWindow();
            IntPtr desktop = GetDesktopWindow();
            if (fg == shell || fg == desktop) return false;

            var sb = new System.Text.StringBuilder(256);
            GetClassName(fg, sb, 256);
            string className = sb.ToString();
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd") return false;

            if (!GetWindowRect(fg, out var rect)) return false;

            int cx = (rect.Left + rect.Right) / 2;
            int cy = (rect.Top + rect.Bottom) / 2;
            bool onThisMonitor = (cx >= monLeft && cx < (monLeft + monWidth) &&
                                  cy >= monTop && cy < (monTop + monHeight));

            if (!onThisMonitor) return false;

            // True Fullscreen: covers entire monitor rect edge-to-edge
            // A true fullscreen app (YouTube full screen, games, VLC, Netflix) starts directly at monLeft/monTop and covers width/height
            bool coversFullMonitor = rect.Left <= monLeft &&
                                     rect.Top <= monTop &&
                                     rect.Right >= (monLeft + monWidth) &&
                                     rect.Bottom >= (monTop + monHeight);

            // True fullscreen apps do not have standard window sizing drop-shadow offsets (like -8px in maximized windows)
            // or cover the taskbar entirely
            return coversFullMonitor;
        }

        public static bool IsForegroundMaximized(IntPtr myHwnd, int monLeft, int monTop, int monWidth, int monHeight)
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero || fg == myHwnd) return false;

            IntPtr shell = GetShellWindow();
            IntPtr desktop = GetDesktopWindow();
            if (fg == shell || fg == desktop) return false;

            var sb = new System.Text.StringBuilder(256);
            GetClassName(fg, sb, 256);
            string className = sb.ToString();
            if (className == "Progman" || className == "WorkerW" || className == "Shell_TrayWnd") return false;

            if (!GetWindowRect(fg, out var rect)) return false;

            int cx = (rect.Left + rect.Right) / 2;
            int cy = (rect.Top + rect.Bottom) / 2;
            bool onThisMonitor = (cx >= monLeft && cx < (monLeft + monWidth) &&
                                  cy >= monTop && cy < (monTop + monHeight));

            if (!onThisMonitor) return false;

            return IsZoomed(fg);
        }

        public static bool IsForegroundObstructing(IntPtr myHwnd, int monLeft, int monTop, int monWidth, int monHeight)
        {
            return IsForegroundTrueFullScreen(myHwnd, monLeft, monTop, monWidth, monHeight) ||
                   IsForegroundMaximized(myHwnd, monLeft, monTop, monWidth, monHeight);
        }

        public static bool IsForegroundFullScreen(IntPtr myHwnd, int monLeft, int monTop, int monWidth, int monHeight)
            => IsForegroundTrueFullScreen(myHwnd, monLeft, monTop, monWidth, monHeight);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("psapi.dll")]
        public static extern int EmptyWorkingSet(IntPtr hwProc);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);

        public static void TrimProcessMemory()
        {
            try
            {
                // Gentle, non-blocking GC collection without forcing working set pages into disk pagefile.
                // We deliberately avoid EmptyWorkingSet or SetProcessWorkingSetSize(-1, -1),
                // because paging memory to swap file causes severe UI thread freezes and hard page faults
                // whenever the user interacts with the app!
                GC.Collect(1, GCCollectionMode.Optimized, false);
            }
            catch { }
        }

        public static void SendMediaAppCommand(IntPtr hWnd, int command)
        {
            SendMessageW(hWnd, WM_APPCOMMAND, hWnd, (IntPtr)command);
        }
    }
}
