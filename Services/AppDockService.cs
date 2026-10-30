using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Helpers;

namespace DynamicIsland.Services
{
    public enum DockDisplayMode
    {
        Inline,      // Model 1: Sejajar di dalam kapsul notch
        GridButton,  // Model 2: Tombol Grid Minimalis (⊞)
        Hidden       // Nonaktif
    }

    public class AppShortcut : INotifyPropertyChanged
    {
        private bool _isRunning;
        private double _itemWidth = 62;
        private double _itemHeight = 66;
        private double _iconSize = 28;
        private double _fontSize = 10;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = string.Empty;
        public string TargetPath { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public string? IconBase64 { get; set; }

        [JsonIgnore]
        public ImageSource? IconSource { get; set; }

        [JsonIgnore]
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (_isRunning != value)
                {
                    _isRunning = value;
                    OnPropertyChanged(nameof(IsRunning));
                }
            }
        }

        [JsonIgnore]
        public double ItemWidth
        {
            get => _itemWidth;
            set
            {
                if (Math.Abs(_itemWidth - value) > 0.1)
                {
                    _itemWidth = value;
                    OnPropertyChanged(nameof(ItemWidth));
                }
            }
        }

        [JsonIgnore]
        public double ItemHeight
        {
            get => _itemHeight;
            set
            {
                if (Math.Abs(_itemHeight - value) > 0.1)
                {
                    _itemHeight = value;
                    OnPropertyChanged(nameof(ItemHeight));
                }
            }
        }

        [JsonIgnore]
        public double IconSize
        {
            get => _iconSize;
            set
            {
                if (Math.Abs(_iconSize - value) > 0.1)
                {
                    _iconSize = value;
                    OnPropertyChanged(nameof(IconSize));
                }
            }
        }

        [JsonIgnore]
        public double FontSize
        {
            get => _fontSize;
            set
            {
                if (Math.Abs(_fontSize - value) > 0.1)
                {
                    _fontSize = value;
                    OnPropertyChanged(nameof(FontSize));
                }
            }
        }

        protected void OnPropertyChanged(string propName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }

    public class AppDockService
    {
        private readonly ObservableCollection<AppShortcut> _shortcuts = new();
        public ObservableCollection<AppShortcut> Shortcuts => _shortcuts;

        public DockDisplayMode DisplayMode { get; set; } = DockDisplayMode.Inline;

        private readonly string _storageFolder;
        private readonly string _shortcutsFilePath;
        private readonly string _modeFilePath;

        public event Action? ShortcutsChanged;

        #region Win32 Shell Icon Interop

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x000000100;
        private const uint SHGFI_LARGEICON = 0x000000000;

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbFileInfo, uint uFlags);

        #endregion

        public AppDockService()
        {
            _storageFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AiaDinamic");
            string legacyFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");

            if (!Directory.Exists(_storageFolder))
            {
                Directory.CreateDirectory(_storageFolder);
                if (Directory.Exists(legacyFolder))
                {
                    try
                    {
                        foreach (var file in Directory.GetFiles(legacyFolder))
                        {
                            string dest = Path.Combine(_storageFolder, Path.GetFileName(file));
                            if (!File.Exists(dest)) File.Copy(file, dest);
                        }
                    }
                    catch { }
                }
            }

            _shortcutsFilePath = Path.Combine(_storageFolder, "dock_shortcuts.json");
            _modeFilePath = Path.Combine(_storageFolder, "dock_mode.txt");

            LoadMode();
            LoadShortcuts();
        }

        public void LoadMode()
        {
            try
            {
                if (File.Exists(_modeFilePath))
                {
                    string text = File.ReadAllText(_modeFilePath).Trim();
                    if (Enum.TryParse<DockDisplayMode>(text, true, out var mode))
                    {
                        DisplayMode = mode;
                        return;
                    }
                }
            }
            catch { }
            DisplayMode = DockDisplayMode.Inline;
        }

        public void SaveMode(DockDisplayMode mode)
        {
            DisplayMode = mode;
            try
            {
                File.WriteAllText(_modeFilePath, mode.ToString());
            }
            catch { }
            ShortcutsChanged?.Invoke();
        }

        public void LoadShortcuts()
        {
            _shortcuts.Clear();
            try
            {
                if (File.Exists(_shortcutsFilePath))
                {
                    string json = File.ReadAllText(_shortcutsFilePath);
                    var list = JsonSerializer.Deserialize<List<AppShortcut>>(json);
                    if (list != null && list.Count > 0)
                    {
                        foreach (var item in list)
                        {
                            RestoreIcon(item);
                            _shortcuts.Add(item);
                        }
                        UpdateRunningStates();
                        UpdateShortcutSizes();
                        return;
                    }
                }
            }
            catch { }

            // Default initial shortcuts if empty
            SeedDefaultShortcuts();
            UpdateShortcutSizes();
            SaveShortcuts();
        }

        private void SeedDefaultShortcuts()
        {
            // 1. File Explorer
            AddShortcutFromPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), "File Explorer");

            // 2. Browser (Chrome / Edge)
            string chromePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Google\Chrome\Application\chrome.exe");
            string edgePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe");
            if (File.Exists(chromePath))
            {
                AddShortcutFromPath(chromePath, "Google Chrome");
            }
            else if (File.Exists(edgePath))
            {
                AddShortcutFromPath(edgePath, "Microsoft Edge");
            }

            // 3. Notepad
            string notepadPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "notepad.exe");
            if (File.Exists(notepadPath))
            {
                AddShortcutFromPath(notepadPath, "Notepad");
            }

            // 4. VS Code (optional if found)
            string vscodePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Microsoft VS Code\Code.exe");
            if (File.Exists(vscodePath))
            {
                AddShortcutFromPath(vscodePath, "VS Code");
            }
        }

        public void SaveShortcuts()
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(_shortcuts, options);
                File.WriteAllText(_shortcutsFilePath, json);
            }
            catch { }
            ShortcutsChanged?.Invoke();
        }

        public AppShortcut? AddShortcutFromPath(string rawPath, string? customName = null)
        {
            if (string.IsNullOrWhiteSpace(rawPath)) return null;

            string targetPath = ResolveShortcutTarget(rawPath);
            if (!File.Exists(targetPath) && !Directory.Exists(targetPath)) return null;

            string name = customName ?? Path.GetFileNameWithoutExtension(rawPath);
            if (string.IsNullOrWhiteSpace(name)) name = Path.GetFileNameWithoutExtension(targetPath);

            string processName = Path.GetFileNameWithoutExtension(targetPath).ToLowerInvariant();

            // Avoid duplicate path
            var existing = _shortcuts.FirstOrDefault(s => string.Equals(s.TargetPath, targetPath, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                return existing;
            }

            var item = new AppShortcut
            {
                Name = name,
                TargetPath = targetPath,
                ProcessName = processName
            };

            ExtractAndCacheIcon(item, rawPath, targetPath);
            _shortcuts.Add(item);
            UpdateRunningStates();
            UpdateShortcutSizes();
            SaveShortcuts();
            return item;
        }

        public bool RemoveShortcut(string id)
        {
            var item = _shortcuts.FirstOrDefault(s => s.Id == id);
            if (item != null)
            {
                _shortcuts.Remove(item);
                UpdateShortcutSizes();
                SaveShortcuts();
                return true;
            }
            return false;
        }

        public void UpdateShortcutSizes()
        {
            int count = _shortcuts.Count;
            double itemWidth, itemHeight, iconSize, fontSize;

            if (count <= 6)
            {
                itemWidth = 62;
                itemHeight = 66;
                iconSize = 28;
                fontSize = 10;
            }
            else if (count <= 10)
            {
                itemWidth = 56;
                itemHeight = 58;
                iconSize = 24;
                fontSize = 9.5;
            }
            else if (count <= 14)
            {
                itemWidth = 52;
                itemHeight = 54;
                iconSize = 22;
                fontSize = 9;
            }
            else
            {
                itemWidth = 48;
                itemHeight = 52;
                iconSize = 20;
                fontSize = 8.5;
            }

            foreach (var item in _shortcuts)
            {
                item.ItemWidth = itemWidth;
                item.ItemHeight = itemHeight;
                item.IconSize = iconSize;
                item.FontSize = fontSize;
            }
        }

        public static string ResolveShortcutTarget(string path)
        {
            if (string.Equals(Path.GetExtension(path), ".lnk", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                    if (shellType != null)
                    {
                        dynamic? shell = Activator.CreateInstance(shellType);
                        dynamic? shortcut = shell?.CreateShortcut(path);
                        string? target = shortcut?.TargetPath;
                        if (!string.IsNullOrWhiteSpace(target) && (File.Exists(target) || Directory.Exists(target)))
                        {
                            return target;
                        }
                    }
                }
                catch { }
            }
            return path;
        }

        private void ExtractAndCacheIcon(AppShortcut item, string rawPath, string targetPath)
        {
            try
            {
                // Try raw path first (gives custom icon from .lnk if specified), then fallback to targetPath
                string pathToQuery = File.Exists(rawPath) ? rawPath : targetPath;

                var shinfo = new SHFILEINFO();
                IntPtr res = SHGetFileInfo(pathToQuery, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), SHGFI_ICON | SHGFI_LARGEICON);

                if (res != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                {
                    try
                    {
                        var bitmapSource = Imaging.CreateBitmapSourceFromHIcon(
                            shinfo.hIcon,
                            Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());

                        bitmapSource.Freeze();
                        item.IconSource = bitmapSource;

                        // Encode to PNG byte array and Base64
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                        using var ms = new MemoryStream();
                        encoder.Save(ms);
                        item.IconBase64 = Convert.ToBase64String(ms.ToArray());
                        return;
                    }
                    finally
                    {
                        NativeMethods.DestroyIcon(shinfo.hIcon);
                    }
                }
            }
            catch { }

            item.IconSource = null;
        }

        private void RestoreIcon(AppShortcut item)
        {
            if (!string.IsNullOrWhiteSpace(item.IconBase64))
            {
                try
                {
                    byte[] bytes = Convert.FromBase64String(item.IconBase64);
                    item.IconSource = ConvertBytesToBitmapSource(bytes);
                    return;
                }
                catch { }
            }

            // Fallback to extract from path if target exists
            if (File.Exists(item.TargetPath))
            {
                ExtractAndCacheIcon(item, item.TargetPath, item.TargetPath);
            }
        }

        private static BitmapSource ConvertBytesToBitmapSource(byte[] bytes)
        {
            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        public void UpdateRunningStates()
        {
            if (_shortcuts.Count == 0) return;

            Task.Run(() =>
            {
                try
                {
                    // Single-pass process snapshot in background to avoid freezing the UI thread
                    var runningNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    var processes = Process.GetProcesses();
                    for (int i = 0; i < processes.Length; i++)
                    {
                        try
                        {
                            runningNames.Add(processes[i].ProcessName);
                        }
                        catch { }
                        finally
                        {
                            try { processes[i].Dispose(); } catch { }
                        }
                    }

                    foreach (var item in _shortcuts)
                    {
                        if (string.IsNullOrEmpty(item.ProcessName))
                        {
                            item.IsRunning = false;
                            continue;
                        }

                        item.IsRunning = runningNames.Contains(item.ProcessName);
                    }
                }
                catch { }
            });
        }

        public void LaunchOrToggle(AppShortcut shortcut)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(shortcut.ProcessName))
                {
                    Process.Start(new ProcessStartInfo { FileName = shortcut.TargetPath, UseShellExecute = true });
                    return;
                }

                // Special handling for explorer.exe
                if (shortcut.ProcessName == "explorer")
                {
                    Process.Start(new ProcessStartInfo { FileName = shortcut.TargetPath, UseShellExecute = true });
                    return;
                }

                // Find process by name
                var processes = Process.GetProcessesByName(shortcut.ProcessName);
                Process? targetProc = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero) ?? processes.FirstOrDefault();

                if (targetProc != null && targetProc.MainWindowHandle != IntPtr.Zero)
                {
                    IntPtr hwnd = targetProc.MainWindowHandle;
                    IntPtr fgHwnd = NativeMethods.GetForegroundWindow();

                    if (fgHwnd == hwnd)
                    {
                        // Active foreground -> Minimize
                        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
                    }
                    else
                    {
                        // Minimized or background -> Restore & bring to front
                        if (NativeMethods.IsIconic(hwnd))
                        {
                            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                        }
                        else
                        {
                            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                        }
                        NativeMethods.SetForegroundWindow(hwnd);
                    }
                }
                else
                {
                    // Not running -> Launch fresh instance
                    Process.Start(new ProcessStartInfo { FileName = shortcut.TargetPath, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"LaunchOrToggle error: {ex.Message}");
            }
            finally
            {
                UpdateRunningStates();
            }
        }
    }
}
