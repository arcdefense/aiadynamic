using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DynamicIsland.Services
{
    public class MonitorInfo
    {
        public int Index { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public bool IsPrimary { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }

        public string DisplayName => $"Monitor {Index + 1} ({(IsPrimary ? "Utama - " : "")}{Width}x{Height})";
    }

    public static class MonitorService
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
            public int Width => Right - Left;
            public int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
        private const uint MONITORINFOF_PRIMARY = 0x00000001;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;

        public static List<MonitorInfo> GetAllMonitors()
        {
            var list = new List<MonitorInfo>();
            int index = 0;

            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr hdc, ref RECT rc, IntPtr data) =>
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMon, ref mi))
                {
                    bool isPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0;
                    list.Add(new MonitorInfo
                    {
                        Index = index,
                        DeviceName = mi.szDevice ?? $"DISPLAY{index + 1}",
                        IsPrimary = isPrimary,
                        Left = mi.rcMonitor.Left,
                        Top = mi.rcMonitor.Top,
                        Width = mi.rcMonitor.Width,
                        Height = mi.rcMonitor.Height
                    });
                    index++;
                }
                return true;
            }, IntPtr.Zero);

            if (list.Count == 0)
            {
                // Fallback to primary screen parameters if EnumDisplayMonitors returned empty
                list.Add(new MonitorInfo
                {
                    Index = 0,
                    DeviceName = "DISPLAY1",
                    IsPrimary = true,
                    Left = 0,
                    Top = 0,
                    Width = (int)SystemParameters.PrimaryScreenWidth,
                    Height = (int)SystemParameters.PrimaryScreenHeight
                });
            }
            else
            {
                // Ensure Primary Monitor is ALWAYS at Index 0 so notch appears on user's main screen
                list.Sort((a, b) =>
                {
                    if (a.IsPrimary && !b.IsPrimary) return -1;
                    if (!a.IsPrimary && b.IsPrimary) return 1;
                    return string.Compare(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase);
                });
                for (int i = 0; i < list.Count; i++)
                {
                    list[i].Index = i;
                }
            }

            return list;
        }

        public static MonitorInfo GetPrimaryMonitor()
        {
            var monitors = GetAllMonitors();
            var primary = monitors.Find(m => m.IsPrimary);
            return primary ?? monitors[0];
        }

        public static MonitorInfo GetMonitorByIndex(int index)
        {
            var monitors = GetAllMonitors();
            if (index >= 0 && index < monitors.Count)
            {
                return monitors[index];
            }
            return GetPrimaryMonitor();
        }

        public static MonitorInfo? GetMonitorFromPoint(int x, int y)
        {
            var pt = new POINT { X = x, Y = y };
            IntPtr hMon = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
            if (hMon != IntPtr.Zero)
            {
                var mi = new MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(MONITORINFOEX));
                if (GetMonitorInfo(hMon, ref mi))
                {
                    var all = GetAllMonitors();
                    var match = all.Find(m => m.DeviceName == mi.szDevice);
                    if (match != null) return match;

                    return new MonitorInfo
                    {
                        Index = 0,
                        DeviceName = mi.szDevice ?? "DISPLAY",
                        IsPrimary = (mi.dwFlags & MONITORINFOF_PRIMARY) != 0,
                        Left = mi.rcMonitor.Left,
                        Top = mi.rcMonitor.Top,
                        Width = mi.rcMonitor.Width,
                        Height = mi.rcMonitor.Height
                    };
                }
            }
            return null;
        }

        public static void DockWindowToMonitor(Window window, MonitorInfo monitor)
        {
            if (window == null) return;

            try
            {
                var dpi = VisualTreeHelper.GetDpi(window);
                double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
                double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

                double monLeftDIP = monitor.Left / scaleX;
                double monTopDIP = monitor.Top / scaleY;
                double monWidthDIP = monitor.Width / scaleX;

                double targetLeft = monLeftDIP + (monWidthDIP - window.Width) / 2.0;
                double targetTop = monTopDIP;

                window.Left = targetLeft;
                window.Top = targetTop;

                // Also call SetWindowPos for immediate physical precision
                var helper = new WindowInteropHelper(window);
                if (helper.Handle != IntPtr.Zero)
                {
                    int pixelX = (int)Math.Round(targetLeft * scaleX);
                    int pixelY = (int)Math.Round(targetTop * scaleY);
                    SetWindowPos(helper.Handle, IntPtr.Zero, pixelX, pixelY, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
                }
            }
            catch { }
        }
    }
}
