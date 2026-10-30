using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using DynamicIsland.Helpers;

namespace DynamicIsland.Services
{
    public class TrayIconService : IDisposable
    {
        #region Win32 P/Invoke & Structures

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NOTIFYICONDATA
        {
            public int cbSize;
            public IntPtr hWnd;
            public int uID;
            public int uFlags;
            public int uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public int dwState;
            public int dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public int uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public int dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        private const int NIM_ADD = 0x00000000;
        private const int NIM_MODIFY = 0x00000001;
        private const int NIM_DELETE = 0x00000002;

        private const int NIF_MESSAGE = 0x00000001;
        private const int NIF_ICON = 0x00000002;
        private const int NIF_TIP = 0x00000004;

        public const int WM_USER = 0x0400;
        public const int WM_TRAYICON = WM_USER + 101;

        public const int WM_LBUTTONUP = 0x0202;
        public const int WM_RBUTTONUP = 0x0205;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern bool Shell_NotifyIcon(int dwMessage, ref NOTIFYICONDATA lpData);

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint ExtractIconEx(string szFileName, int nIconIndex, IntPtr[]? phiconLarge, IntPtr[]? phiconSmall, uint nIcons);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint uType, int cxDesired, int cyDesired, uint fuLoad);

        private const uint IMAGE_ICON = 1;
        private const uint LR_LOADFROMFILE = 0x00000010;

        #endregion

        private IntPtr _hwnd = IntPtr.Zero;
        private IntPtr _hIcon = IntPtr.Zero;
        private bool _isAdded = false;
        private Action? _onLeftClick;
        private Action? _onRightClick;

        public void Initialize(IntPtr hwnd, Action onLeftClick, Action onRightClick)
        {
            _hwnd = hwnd;
            _onLeftClick = onLeftClick;
            _onRightClick = onRightClick;

            LoadTrayIcon();

            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
                uCallbackMessage = WM_TRAYICON,
                hIcon = _hIcon,
                szTip = "Aia Dinamic • Sistem Siap"
            };

            _isAdded = Shell_NotifyIcon(NIM_ADD, ref data);
        }

        private void LoadTrayIcon()
        {
            try
            {
                // Try 1: Extract icon from running executable
                string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    IntPtr[] smallIcons = new IntPtr[1];
                    uint extracted = ExtractIconEx(exePath, 0, null, smallIcons, 1);
                    if (extracted > 0 && smallIcons[0] != IntPtr.Zero)
                    {
                        _hIcon = smallIcons[0];
                        return;
                    }
                }
            }
            catch { }

            try
            {
                // Try 2: Load directly from app.ico if in directory
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(icoPath))
                {
                    _hIcon = LoadImage(IntPtr.Zero, icoPath, IMAGE_ICON, 16, 16, LR_LOADFROMFILE);
                    if (_hIcon != IntPtr.Zero) return;
                }
            }
            catch { }
        }

        public void UpdateTooltip(string tooltip)
        {
            if (!_isAdded || _hwnd == IntPtr.Zero) return;

            string safeTip = tooltip;
            if (safeTip.Length > 120) safeTip = safeTip.Substring(0, 117) + "...";

            var data = new NOTIFYICONDATA
            {
                cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                hWnd = _hwnd,
                uID = 1001,
                uFlags = NIF_TIP,
                szTip = safeTip
            };

            Shell_NotifyIcon(NIM_MODIFY, ref data);
        }

        public IntPtr HandleMessage(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_TRAYICON)
            {
                int mouseEvent = lParam.ToInt32();
                if (mouseEvent == WM_LBUTTONUP)
                {
                    _onLeftClick?.Invoke();
                    handled = true;
                }
                else if (mouseEvent == WM_RBUTTONUP)
                {
                    _onRightClick?.Invoke();
                    handled = true;
                }
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            if (_isAdded && _hwnd != IntPtr.Zero)
            {
                var data = new NOTIFYICONDATA
                {
                    cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
                    hWnd = _hwnd,
                    uID = 1001
                };
                Shell_NotifyIcon(NIM_DELETE, ref data);
                _isAdded = false;
            }

            if (_hIcon != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(_hIcon);
                _hIcon = IntPtr.Zero;
            }
        }
    }
}
