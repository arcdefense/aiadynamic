using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DynamicIsland.Services
{
    public class UserSettingsModel
    {
        public bool IsAutoHide { get; set; } = true;
        public bool IsPinned { get; set; } = false;
        public string DockMode { get; set; } = "Inline";
        public string SpectrumTheme { get; set; } = "NeoGreen";
        public float AudioSensitivity { get; set; } = 1.0f;
        public bool RunAtStartup { get; set; } = false;
        public int TargetMonitorIndex { get; set; } = 0;
        public bool FollowCursorMonitor { get; set; } = false;
        public bool LyricStyleNotch { get; set; } = true;
        public bool LyricShowNextPreview { get; set; } = true;
        public double LyricNextOpacity { get; set; } = 0.50;
        public string LyricFontSize { get; set; } = "Medium";
        public bool ShowNetworkSpeedMeter { get; set; } = false;
        public string SpeedMeterUnit { get; set; } = "Bytes"; // "Bytes" (KB/s, MB/s) or "Bits" (kbps, Mbps)
        public string SpectrumModel { get; set; } = "BottomUp"; // "BottomUp", "CenterMirrored", "TopDown", "Needles"
    }

    public class SettingsService
    {
        private const string StartupRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "AiaDinamic";

        private readonly string _settingsFilePath;
        private readonly string _appDataFolder;

        public UserSettingsModel Current { get; private set; } = new();

        public event Action? SettingsChanged;

        public SettingsService()
        {
            _appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiaDinamic");
            string legacyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

            // Migration from legacy folder if exists
            try
            {
                if (!Directory.Exists(_appDataFolder))
                {
                    Directory.CreateDirectory(_appDataFolder);

                    if (Directory.Exists(legacyFolder))
                    {
                        foreach (var file in Directory.GetFiles(legacyFolder))
                        {
                            string dest = Path.Combine(_appDataFolder, Path.GetFileName(file));
                            if (!File.Exists(dest))
                            {
                                File.Copy(file, dest);
                            }
                        }
                    }
                }
            }
            catch { }

            _settingsFilePath = Path.Combine(_appDataFolder, "settings.json");
            Load();
        }

        public void Load()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var loaded = JsonSerializer.Deserialize<UserSettingsModel>(json);
                    if (loaded != null)
                    {
                        Current = loaded;
                    }
                }
                else
                {
                    // Create initial default settings file
                    Save();
                }
            }
            catch { }

            // Sync with registry actual state for RunAtStartup
            Current.RunAtStartup = CheckRegistryStartup();
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(_appDataFolder))
                {
                    Directory.CreateDirectory(_appDataFolder);
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(Current, options);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }

            SettingsChanged?.Invoke();
        }

        public void SetRunAtStartup(bool enable)
        {
            Current.RunAtStartup = enable;
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
                if (key != null)
                {
                    if (enable)
                    {
                        string? exePath = Environment.ProcessPath;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue(AppName, $"\"{exePath}\"");
                        }
                    }
                    else
                    {
                        key.DeleteValue(AppName, false);
                    }
                }
            }
            catch { }

            Save();
        }

        private bool CheckRegistryStartup()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
                if (key != null)
                {
                    var val = key.GetValue(AppName);
                    return val != null;
                }
            }
            catch { }
            return false;
        }
    }
}
