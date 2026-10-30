using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;

namespace DynamicIsland.Services
{
    public struct SystemPowerStatus
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public int BatteryLifeTime;
        public int BatteryFullLifeTime;
    }

    public class BatteryInfo
    {
        public bool HasBattery { get; set; }
        public int Percentage { get; set; }
        public bool IsCharging { get; set; }
        public string StatusText { get; set; } = string.Empty;
    }

    public class SystemInfoManager
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemPowerStatus(out SystemPowerStatus lpSystemPowerStatus);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime, out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);

        private ulong _prevIdleTime;
        private ulong _prevKernelTime;
        private ulong _prevUserTime;
        private bool _isFirstCpuSample = true;
        private int _cachedCpuPercent = 0;

        public int GetCpuUsage()
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user))
            {
                ulong currentIdle = ((ulong)idle.dwHighDateTime << 32) | (uint)idle.dwLowDateTime;
                ulong currentKernel = ((ulong)kernel.dwHighDateTime << 32) | (uint)kernel.dwLowDateTime;
                ulong currentUser = ((ulong)user.dwHighDateTime << 32) | (uint)user.dwLowDateTime;

                if (_isFirstCpuSample)
                {
                    _prevIdleTime = currentIdle;
                    _prevKernelTime = currentKernel;
                    _prevUserTime = currentUser;
                    _isFirstCpuSample = false;
                    return _cachedCpuPercent;
                }

                ulong deltaIdle = currentIdle - _prevIdleTime;
                ulong deltaKernel = currentKernel - _prevKernelTime;
                ulong deltaUser = currentUser - _prevUserTime;
                ulong deltaTotal = deltaKernel + deltaUser;

                _prevIdleTime = currentIdle;
                _prevKernelTime = currentKernel;
                _prevUserTime = currentUser;

                if (deltaTotal > 0)
                {
                    double cpu = (1.0 - ((double)deltaIdle / deltaTotal)) * 100.0;
                    _cachedCpuPercent = Math.Clamp((int)Math.Round(cpu), 0, 100);
                }
            }
            return _cachedCpuPercent;
        }

        public int GetRamUsage()
        {
            var mem = new MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
            if (GlobalMemoryStatusEx(ref mem))
            {
                return (int)mem.dwMemoryLoad;
            }
            return 0;
        }

        private readonly BatteryInfo _cachedBatteryInfo = new BatteryInfo();

        public BatteryInfo GetBatteryInfo()
        {
            if (GetSystemPowerStatus(out SystemPowerStatus status))
            {
                // BatteryFlag: 128 = No system battery, 255 = Unknown status
                if (status.BatteryFlag == 128 || status.BatteryLifePercent > 100)
                {
                    _cachedBatteryInfo.HasBattery = false;
                    _cachedBatteryInfo.Percentage = 100;
                    _cachedBatteryInfo.IsCharging = true;
                    _cachedBatteryInfo.StatusText = "AC Power";
                    return _cachedBatteryInfo;
                }

                bool isCharging = (status.ACLineStatus == 1);
                int percent = status.BatteryLifePercent;

                _cachedBatteryInfo.HasBattery = true;
                _cachedBatteryInfo.Percentage = percent;
                _cachedBatteryInfo.IsCharging = isCharging;
                _cachedBatteryInfo.StatusText = isCharging ? $"{percent}% (Charging)" : $"{percent}%";
                return _cachedBatteryInfo;
            }

            _cachedBatteryInfo.HasBattery = false;
            _cachedBatteryInfo.Percentage = 100;
            _cachedBatteryInfo.IsCharging = false;
            _cachedBatteryInfo.StatusText = "Desktop";
            return _cachedBatteryInfo;
        }

        #region Real-Time Network Speed Meter (TX / RX)

        private long _prevRxBytes = 0;
        private long _prevTxBytes = 0;
        private DateTime _prevNetworkTime = DateTime.MinValue;
        private bool _isFirstNetworkSample = true;
        private double _cachedRxBytesPerSec = 0;
        private double _cachedTxBytesPerSec = 0;

        public void GetNetworkSpeed(out double rxBytesPerSec, out double txBytesPerSec)
        {
            long currentRxBytes = 0;
            long currentTxBytes = 0;
            DateTime now = DateTime.UtcNow;

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces();
                foreach (var ni in interfaces)
                {
                    if (ni.OperationalStatus == OperationalStatus.Up &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        ni.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                    {
                        var stats = ni.GetIPStatistics();
                        currentRxBytes += stats.BytesReceived;
                        currentTxBytes += stats.BytesSent;
                    }
                }
            }
            catch
            {
                rxBytesPerSec = _cachedRxBytesPerSec;
                txBytesPerSec = _cachedTxBytesPerSec;
                return;
            }

            if (_isFirstNetworkSample || _prevNetworkTime == DateTime.MinValue)
            {
                _prevRxBytes = currentRxBytes;
                _prevTxBytes = currentTxBytes;
                _prevNetworkTime = now;
                _isFirstNetworkSample = false;
                rxBytesPerSec = 0;
                txBytesPerSec = 0;
                return;
            }

            double elapsedSeconds = (now - _prevNetworkTime).TotalSeconds;
            if (elapsedSeconds >= 0.5)
            {
                long deltaRx = currentRxBytes - _prevRxBytes;
                long deltaTx = currentTxBytes - _prevTxBytes;

                _prevRxBytes = currentRxBytes;
                _prevTxBytes = currentTxBytes;
                _prevNetworkTime = now;

                if (deltaRx >= 0 && deltaTx >= 0)
                {
                    _cachedRxBytesPerSec = deltaRx / elapsedSeconds;
                    _cachedTxBytesPerSec = deltaTx / elapsedSeconds;
                }
            }

            rxBytesPerSec = _cachedRxBytesPerSec;
            txBytesPerSec = _cachedTxBytesPerSec;
        }

        public static string FormatNetworkSpeed(double bytesPerSec, bool useBits = false)
        {
            if (useBits)
            {
                double bitsPerSec = bytesPerSec * 8.0;
                if (bitsPerSec <= 0) return "0 bps";
                if (bitsPerSec < 1_000) return $"{Math.Round(bitsPerSec)} bps";
                if (bitsPerSec < 1_000_000) return $"{(bitsPerSec / 1_000.0):0.#} kbps";
                if (bitsPerSec < 1_000_000_000) return $"{(bitsPerSec / 1_000_000.0):0.#} Mbps";
                return $"{(bitsPerSec / 1_000_000_000.0):0.##} Gbps";
            }

            if (bytesPerSec <= 0)
            {
                return "0 B/s";
            }
            else if (bytesPerSec < 1024)
            {
                return $"{Math.Round(bytesPerSec)} B/s";
            }
            else if (bytesPerSec < 1024 * 1024)
            {
                double kb = bytesPerSec / 1024.0;
                return kb < 10.0 ? $"{kb:0.0} KB/s" : $"{Math.Round(kb)} KB/s";
            }
            else if (bytesPerSec < 1024 * 1024 * 1024)
            {
                double mb = bytesPerSec / (1024.0 * 1024.0);
                return mb < 100.0 ? $"{mb:0.1} MB/s" : $"{Math.Round(mb)} MB/s";
            }
            else
            {
                double gb = bytesPerSec / (1024.0 * 1024.0 * 1024.0);
                return $"{gb:0.00} GB/s";
            }
        }

        #endregion
    }
}
