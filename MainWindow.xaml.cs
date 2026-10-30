using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Helpers;
using DynamicIsland.Services;

namespace DynamicIsland
{
    public partial class MainWindow : Window
    {
        private readonly MediaManager _mediaManager = new();
        private readonly SystemInfoManager _sysInfoManager = new();
        private readonly AudioSpectrumService _audioSpectrum = new();
        private readonly LyricsService _lyricsService = new();
        private readonly EqualizerService _equalizer = new();

        private readonly DispatcherTimer _clockTimer = new();
        private readonly DispatcherTimer _spectrumTimer = new();
        private readonly DispatcherTimer _autoHideTimer = new();
        private readonly DispatcherTimer _volumeHudTimer = new();
        private readonly DispatcherTimer _rightVolumeTimer = new();
        private readonly DispatcherTimer _bottomVolumeTimer = new();
        private readonly DispatcherTimer _notificationTimer = new();

        private readonly VolumeService _volumeService = new();
        private readonly NotificationService _notificationService = new();
        private readonly AppDockService _dockService = new();
        private readonly TrayIconService _trayService = new();
        private readonly SettingsService _settingsService = new();

        private bool _isAutoHide = true;
        private bool _isPeeking = false;
        private bool _isExpanded = false;
        private bool _isPinned = false;
        private bool _isHovered = false;
        private bool _isSettingsOpen = false;
        private bool _isLyricsMode = false;
        private bool _isEqMode = false;
        private bool _isUpdatingEqSliders = false;
        private bool _isFloatingLyrics = false;
        private bool _isLyricsPinned = false;
        private bool _lyricStyleNotch = true;
        private bool _lyricShowNextPreview = true;
        private double _lyricNextOpacity = 0.50;
        private string _lyricFontSize = "Medium";
        private bool _showNetworkSpeedMeter = false;
        private string _speedMeterUnit = "Bytes";
        private string _spectrumModel = "BottomUp";
        private int _lastFloatingLyricIndex = -99;
        private bool _isShowingVolumeHUD = false;
        private bool _isShowingNotification = false;
        private bool _hasMedia = false;
        private bool _isPlaying = false;
        private bool _isSpectrumFlattened = false;
        private IntPtr _hwnd = IntPtr.Zero;
        private string _currentLyricsKey = string.Empty;
        private LyricsResult? _currentLyrics = null;
        private int _lastActiveLyricIndex = -2;
        private int _currentMonitorIndex = 0;
        private MonitorInfo? _cachedMonitor = null;
        private readonly DispatcherTimer _monitorFollowTimer = new();

        public enum ExpandedTab
        {
            Player,
            Apps,
            System
        }

        private ExpandedTab _currentTab = ExpandedTab.Player;
        private bool _isCompactDockActive = false;

        // Dimensions
        private const double PeekWidth = 72;
        private const double PeekHeight = 3;
        private const double PeekRadius = 3;

        private const double CompactBaseWidth = 188;
        private const double CompactHeight = 40;
        private const double CompactRadius = 20;
        private SolidColorBrush[]? _currentThemeBrushes = null;
        private readonly SolidColorBrush _restingSpectrumBrush = new(Color.FromRgb(58, 58, 62));

        private const double FloatingLyricsHeight = 36;
        private const double ExpandedWidth = 460;
        private const double ExpandedHeightNormal = 188;
        private const double ExpandedHeightKaraoke = 214;
        private const double ExpandedHeightEq = 248;
        private double ExpandedHeight => _isEqMode ? ExpandedHeightEq : (_isLyricsMode ? ExpandedHeightKaraoke : ExpandedHeightNormal);
        private const double ExpandedRadius = 26;

        // Path geometry strings for Play / Pause
        private const string PlayPathData = "M8 5v14l11-7z";
        private const string PausePathData = "M6 19h4V5H6v14zm8-14v14h4V5h-4z";

        private SolidColorBrush GetBrush(string key, Color fallbackColor)
        {
            if (TryFindResource(key) is SolidColorBrush brush)
                return brush;
            return new SolidColorBrush(fallbackColor);
        }

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. Position Window on configured monitor
                _currentMonitorIndex = _settingsService.Current.TargetMonitorIndex;
                ApplyMonitorPosition(_currentMonitorIndex);
                UpdateContextMenuCheckmarks();

                _monitorFollowTimer.Interval = TimeSpan.FromMilliseconds(250);
                _monitorFollowTimer.Tick += MonitorFollowTimer_Tick;
                _monitorFollowTimer.Start();

                DpiChanged += (s, e) =>
                {
                    ApplyMonitorPosition(_currentMonitorIndex);
                };

                // 2. Hide from Alt+Tab
                var helper = new WindowInteropHelper(this);
                _hwnd = helper.Handle;
                NativeMethods.HideFromAltTab(_hwnd);

                // 2b. Setup System Tray Icon & Win32 Hook
                var source = HwndSource.FromHwnd(_hwnd);
                source?.AddHook(WndProc);

                _trayService.Initialize(_hwnd,
                    onLeftClick: () =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            NativeMethods.SetForegroundWindow(_hwnd);
                            if (_isPeeking)
                            {
                                ShowCompact();
                            }
                            else if (!_isExpanded)
                            {
                                ExpandIsland();
                            }
                            else
                            {
                                ShowCompact();
                            }
                        });
                    },
                    onRightClick: () =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            NativeMethods.SetForegroundWindow(_hwnd);
                            if (ContextMenu != null)
                            {
                                ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                                ContextMenu.IsOpen = true;
                            }
                        });
                    });

                // 2c. Load and apply unified settings
                _isAutoHide = _settingsService.Current.IsAutoHide;
                MenuItemAutoHide.IsChecked = _isAutoHide;
                _isPinned = _settingsService.Current.IsPinned;
                MenuItemPin.IsChecked = _isPinned;
                MenuItemStartup.IsChecked = _settingsService.Current.RunAtStartup;
                _lyricStyleNotch = _settingsService.Current.LyricStyleNotch;
                _lyricShowNextPreview = _settingsService.Current.LyricShowNextPreview;
                _lyricNextOpacity = _settingsService.Current.LyricNextOpacity;
                _lyricFontSize = string.IsNullOrEmpty(_settingsService.Current.LyricFontSize) ? "Medium" : _settingsService.Current.LyricFontSize;
                ApplyLyricFontSize(_lyricFontSize, false);
                ApplyLyricNextOpacity(_lyricNextOpacity > 0 ? _lyricNextOpacity : 0.50, false);
                _spectrumModel = string.IsNullOrEmpty(_settingsService.Current.SpectrumModel) ? "BottomUp" : _settingsService.Current.SpectrumModel;
                ApplySpectrumTheme(_settingsService.Current.SpectrumTheme, false);
                ApplySpectrumModel(_spectrumModel, false);
                ApplyAudioSensitivity(_settingsService.Current.AudioSensitivity, false);
                _showNetworkSpeedMeter = _settingsService.Current.ShowNetworkSpeedMeter;
                _speedMeterUnit = string.IsNullOrEmpty(_settingsService.Current.SpeedMeterUnit) ? "Bytes" : _settingsService.Current.SpeedMeterUnit;
                if (MenuItemSpeedMeter != null) MenuItemSpeedMeter.IsChecked = _showNetworkSpeedMeter;
                if (NetworkSpeedPanel != null) NetworkSpeedPanel.Visibility = _showNetworkSpeedMeter ? Visibility.Visible : Visibility.Collapsed;

                // 3. Setup Clock & Status Timer (1 second)
                int clockTickCount = 0;
                _clockTimer.Interval = TimeSpan.FromSeconds(1);
                _clockTimer.Tick += (s, args) =>
                {
                    UpdateClockAndBattery();
                    clockTickCount++;
                    // Only poll running process shortcuts periodically (every 5s) or when dock is open
                    if ((clockTickCount % 5 == 0) || (_isExpanded && _currentTab == ExpandedTab.Apps) || _isCompactDockActive)
                    {
                        _dockService.UpdateRunningStates();
                    }

                    // Periodic idle memory trim every 60 seconds when completely collapsed and idle
                    if (clockTickCount % 60 == 0 && !_isExpanded && !_isHovered && !_isLyricsPinned)
                    {
                        NativeMethods.TrimProcessMemory();
                    }
                };
                _clockTimer.Start();
                UpdateClockAndBattery();

                // 4. Setup REAL Audio Spectrum Visualizer (33ms interval ~ 30 FPS)
                _spectrumTimer.Interval = TimeSpan.FromMilliseconds(33);
                _spectrumTimer.Tick += SpectrumTimer_Tick;
                _spectrumTimer.Start();
                _audioSpectrum.Start();
                _equalizer.SettingsChanged += () => Dispatcher.Invoke(SyncEqualizerUi);
                SyncEqualizerUi();

                // 5. Setup Volume HUD & Notification Timers
                _volumeHudTimer.Interval = TimeSpan.FromMilliseconds(1300);
                _volumeHudTimer.Tick += (s, args) => DismissPeekingVolumeHUD();

                _rightVolumeTimer.Interval = TimeSpan.FromMilliseconds(1200);
                _rightVolumeTimer.Tick += (s, args) =>
                {
                    _rightVolumeTimer.Stop();
                    HideRightVolumeHUD();
                };

                _bottomVolumeTimer.Interval = TimeSpan.FromMilliseconds(1200);
                _bottomVolumeTimer.Tick += (s, args) =>
                {
                    _bottomVolumeTimer.Stop();
                    HideBottomVolumeHUD();
                };

                // 6. Setup App Dock Shortcuts & Mode
                DockInlineList.ItemsSource = _dockService.Shortcuts;
                DockExpandedList.ItemsSource = _dockService.Shortcuts;
                _dockService.ShortcutsChanged += () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        RefreshDockUI();
                        if (!_isExpanded && !_isPeeking && !_isFloatingLyrics && !_isShowingVolumeHUD && !_isShowingNotification)
                        {
                            AnimateBorderWidth(GetCompactTargetWidth(), 200);
                        }
                    });
                };
                RefreshDockUI();

                _notificationTimer.Interval = TimeSpan.FromMilliseconds(3800);
                _notificationTimer.Tick += (s, args) => DismissNotification();

                // 6. Setup Notification Service
                _notificationService.NotificationReceived += OnNotificationReceived;
                await _notificationService.InitializeAsync();
                UpdateNotchShape(IslandBorder.Width > 0 ? IslandBorder.Width : CompactBaseWidth, IslandBorder.Height > 0 ? IslandBorder.Height : CompactHeight);

                // 8. Setup Auto-Hide Timer (650ms delay)
                _autoHideTimer.Interval = TimeSpan.FromMilliseconds(650);
                _autoHideTimer.Tick += (s, args) =>
                {
                    _autoHideTimer.Stop();
                    if (!_isHovered && !_isPinned && !_isLyricsPinned && !_isSettingsOpen && !_isEqMode)
                    {
                        if (_isExpanded)
                        {
                            ShowCompact();
                        }
                        else if (_isTrueFullScreen)
                        {
                            HideToPeek();
                        }
                        else if (_isLyricsMode && _hasMedia)
                        {
                            ShowFloatingLyrics();
                        }
                        else if (_isAutoHide)
                        {
                            HideToPeek();
                        }
                    }
                };

                // 6. Setup Media Manager
                _mediaManager.MediaChanged += OnMediaChanged;
                await _mediaManager.InitializeAsync();

                // Always start in Compact Mode (Notch Kecil) so the user immediately sees the notch on launch!
                ShowCompact();

                // Check initial obstruction state
                CheckWindowObstructionState();

                // One-time startup memory trim after JIT warm-up and initial UI render (3.5s)
                var startupTrimTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
                startupTrimTimer.Tick += (s, e) =>
                {
                    startupTrimTimer.Stop();
                    NativeMethods.TrimProcessMemory();
                };
                startupTrimTimer.Start();
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText("D:\\Projek-dev\\aia-ide\\crash.log", ex.ToString()); } catch { }
            }
        }

        private void SpectrumTimer_Tick(object? sender, EventArgs e)
        {
            // 1. Live Karaoke Synchronization (Must run unconditionally whenever Lyrics or Floating Lyrics are active!)
            if (_isFloatingLyrics || _isLyricsMode)
            {
                UpdateKaraokePosition();
            }

            // 2. Vinyl album art continuous spin animation
            if (_isPlaying && MiniVinylRotate != null && ExpandedVinylRotate != null && KaraokeVinylRotate != null)
            {
                double nextAngle = (MiniVinylRotate.Angle + 1.2) % 360.0;
                MiniVinylRotate.Angle = nextAngle;
                ExpandedVinylRotate.Angle = nextAngle;
                KaraokeVinylRotate.Angle = nextAngle;
            }

            // 3. Apple Music Sing pulsing neon glow
            if (_isExpanded && _isLyricsMode && CurrentLyricGlow != null)
            {
                double pulse = Math.Sin(DateTime.Now.TimeOfDay.TotalSeconds * 5.0);
                CurrentLyricGlow.BlurRadius = 10 + (pulse * 4.0);
                CurrentLyricGlow.Opacity = 0.45 + (pulse * 0.15);
            }

            if (_isPeeking) return;

            // 4. Check if live hardware audio or media playback is active
            bool isAudioActive = _audioSpectrum.HasAudioSignal || (_hasMedia && _isPlaying);
            if (!isAudioActive)
            {
                if (!_isSpectrumFlattened)
                {
                    EqBar1.Height = 2; EqBar1.Fill = _restingSpectrumBrush;
                    EqBar2.Height = 2; EqBar2.Fill = _restingSpectrumBrush;
                    EqBar3.Height = 2; EqBar3.Fill = _restingSpectrumBrush;
                    EqBar4.Height = 2; EqBar4.Fill = _restingSpectrumBrush;
                    FlattenSpectrumBars();
                    _audioSpectrum.ClearLevels();
                    _isSpectrumFlattened = true;
                    if (!_isPlaying) EqualizerPanel.Visibility = Visibility.Collapsed;
                }
                return;
            }
            _isSpectrumFlattened = false;

            if (EqualizerPanel.Visibility != Visibility.Visible && !_isExpanded && !_isPeeking && !_isFloatingLyrics)
            {
                EqualizerPanel.Visibility = Visibility.Visible;
            }

            if (_isExpanded && _currentTab == ExpandedTab.Player && !_hasMedia)
            {
                if (TxtAppSource.Text != "AUDIO VISUALIZER")
                {
                    TxtAppSource.Text = "AUDIO VISUALIZER";
                    TxtExpandedTitle.Text = "Windows Audio Output";
                    TxtExpandedArtist.Text = "Live Hardware Spectrum Visualizer";
                }
            }

            // Read actual hardware frequency spectrum with fluid falloff decay (Pro RTA Engine)
            float[] levels = _audioSpectrum.GetCurrentLevelsWithDecay();

            // Compact Equalizer Bars (4 ISO frequency groups: Sub-Bass, Low-Mid, Mid, Treble)
            if (EqualizerPanel.Visibility == Visibility.Visible)
            {
                var b0 = _currentThemeBrushes != null && _currentThemeBrushes.Length > 0 ? _currentThemeBrushes[0] : GetBrush("IslandGreen", Color.FromRgb(48, 209, 88));
                var b1 = _currentThemeBrushes != null && _currentThemeBrushes.Length > 1 ? _currentThemeBrushes[1] : b0;
                var b2 = _currentThemeBrushes != null && _currentThemeBrushes.Length > 2 ? _currentThemeBrushes[2] : b0;
                var b3 = _currentThemeBrushes != null && _currentThemeBrushes.Length > 3 ? _currentThemeBrushes[3] : b0;

                float[] compact = _audioSpectrum.GetCompactLevels(levels);
                if (_spectrumModel == "BouncingBeads")
                {
                    const double CompactHop = 13.0;
                    EqBar1.Fill = b0; EqBar1.Height = 3.5; SetBarY(EqBar1, -compact[0] * CompactHop);
                    EqBar2.Fill = b1; EqBar2.Height = 3.5; SetBarY(EqBar2, -compact[1] * CompactHop);
                    EqBar3.Fill = b2; EqBar3.Height = 3.5; SetBarY(EqBar3, -compact[2] * CompactHop);
                    EqBar4.Fill = b3; EqBar4.Height = 3.5; SetBarY(EqBar4, -compact[3] * CompactHop);
                }
                else
                {
                    const double CompactMax = 18.0;
                    const double CompactMin = 2.0;

                    EqBar1.Fill = b0; EqBar1.Height = Math.Max(CompactMin, compact[0] * CompactMax);
                    EqBar2.Fill = b1; EqBar2.Height = Math.Max(CompactMin, compact[1] * CompactMax);
                    EqBar3.Fill = b2; EqBar3.Height = Math.Max(CompactMin, compact[2] * CompactMax);
                    EqBar4.Fill = b3; EqBar4.Height = Math.Max(CompactMin, compact[3] * CompactMax);
                }
            }

            // Expanded Card Equalizer Bars (20 ISO 1/3-Octave Bands with Decibel Scaling & Spectral Tilt)
            if (_isExpanded && !_isLyricsMode && !_isEqMode)
            {
                if (_spectrumModel == "BouncingBeads")
                {
                    const double MaxHop = 44.0;
                    ExpBar1.Height  = 6.0; SetBarY(ExpBar1,  -levels[0]  * MaxHop);
                    ExpBar2.Height  = 6.0; SetBarY(ExpBar2,  -levels[1]  * MaxHop);
                    ExpBar3.Height  = 6.0; SetBarY(ExpBar3,  -levels[2]  * MaxHop);
                    ExpBar4.Height  = 6.0; SetBarY(ExpBar4,  -levels[3]  * MaxHop);
                    ExpBar5.Height  = 6.0; SetBarY(ExpBar5,  -levels[4]  * MaxHop);
                    ExpBar6.Height  = 6.0; SetBarY(ExpBar6,  -levels[5]  * MaxHop);
                    ExpBar7.Height  = 6.0; SetBarY(ExpBar7,  -levels[6]  * MaxHop);
                    ExpBar8.Height  = 6.0; SetBarY(ExpBar8,  -levels[7]  * MaxHop);
                    ExpBar9.Height  = 6.0; SetBarY(ExpBar9,  -levels[8]  * MaxHop);
                    ExpBar10.Height = 6.0; SetBarY(ExpBar10, -levels[9]  * MaxHop);
                    ExpBar11.Height = 6.0; SetBarY(ExpBar11, -levels[10] * MaxHop);
                    ExpBar12.Height = 6.0; SetBarY(ExpBar12, -levels[11] * MaxHop);
                    ExpBar13.Height = 6.0; SetBarY(ExpBar13, -levels[12] * MaxHop);
                    ExpBar14.Height = 6.0; SetBarY(ExpBar14, -levels[13] * MaxHop);
                    ExpBar15.Height = 6.0; SetBarY(ExpBar15, -levels[14] * MaxHop);
                    ExpBar16.Height = 6.0; SetBarY(ExpBar16, -levels[15] * MaxHop);
                    ExpBar17.Height = 6.0; SetBarY(ExpBar17, -levels[16] * MaxHop);
                    ExpBar18.Height = 6.0; SetBarY(ExpBar18, -levels[17] * MaxHop);
                    ExpBar19.Height = 6.0; SetBarY(ExpBar19, -levels[18] * MaxHop);
                    ExpBar20.Height = 6.0; SetBarY(ExpBar20, -levels[19] * MaxHop);
                }
                else
                {
                    const double MaxBarHeight = 52.0;
                    const double MinBarHeight = 3.0;

                    ExpBar1.Height  = Math.Max(MinBarHeight, levels[0]  * MaxBarHeight);
                    ExpBar2.Height  = Math.Max(MinBarHeight, levels[1]  * MaxBarHeight);
                    ExpBar3.Height  = Math.Max(MinBarHeight, levels[2]  * MaxBarHeight);
                    ExpBar4.Height  = Math.Max(MinBarHeight, levels[3]  * MaxBarHeight);
                    ExpBar5.Height  = Math.Max(MinBarHeight, levels[4]  * MaxBarHeight);
                    ExpBar6.Height  = Math.Max(MinBarHeight, levels[5]  * MaxBarHeight);
                    ExpBar7.Height  = Math.Max(MinBarHeight, levels[6]  * MaxBarHeight);
                    ExpBar8.Height  = Math.Max(MinBarHeight, levels[7]  * MaxBarHeight);
                    ExpBar9.Height  = Math.Max(MinBarHeight, levels[8]  * MaxBarHeight);
                    ExpBar10.Height = Math.Max(MinBarHeight, levels[9]  * MaxBarHeight);
                    ExpBar11.Height = Math.Max(MinBarHeight, levels[10] * MaxBarHeight);
                    ExpBar12.Height = Math.Max(MinBarHeight, levels[11] * MaxBarHeight);
                    ExpBar13.Height = Math.Max(MinBarHeight, levels[12] * MaxBarHeight);
                    ExpBar14.Height = Math.Max(MinBarHeight, levels[13] * MaxBarHeight);
                    ExpBar15.Height = Math.Max(MinBarHeight, levels[14] * MaxBarHeight);
                    ExpBar16.Height = Math.Max(MinBarHeight, levels[15] * MaxBarHeight);
                    ExpBar17.Height = Math.Max(MinBarHeight, levels[16] * MaxBarHeight);
                    ExpBar18.Height = Math.Max(MinBarHeight, levels[17] * MaxBarHeight);
                    ExpBar19.Height = Math.Max(MinBarHeight, levels[18] * MaxBarHeight);
                    ExpBar20.Height = Math.Max(MinBarHeight, levels[19] * MaxBarHeight);
                }
            }
        }

        private void FlattenSpectrumBars()
        {
            double resetHeight = (_spectrumModel == "BouncingBeads") ? 6.0 : 3.0;

            ExpBar1.Height = resetHeight;  ExpBar2.Height = resetHeight;  ExpBar3.Height = resetHeight;  ExpBar4.Height = resetHeight;
            ExpBar5.Height = resetHeight;  ExpBar6.Height = resetHeight;  ExpBar7.Height = resetHeight;  ExpBar8.Height = resetHeight;
            ExpBar9.Height = resetHeight;  ExpBar10.Height = resetHeight; ExpBar11.Height = resetHeight; ExpBar12.Height = resetHeight;
            ExpBar13.Height = resetHeight; ExpBar14.Height = resetHeight; ExpBar15.Height = resetHeight; ExpBar16.Height = resetHeight;
            ExpBar17.Height = resetHeight; ExpBar18.Height = resetHeight; ExpBar19.Height = resetHeight; ExpBar20.Height = resetHeight;

            SetBarY(ExpBar1, 0);  SetBarY(ExpBar2, 0);  SetBarY(ExpBar3, 0);  SetBarY(ExpBar4, 0);
            SetBarY(ExpBar5, 0);  SetBarY(ExpBar6, 0);  SetBarY(ExpBar7, 0);  SetBarY(ExpBar8, 0);
            SetBarY(ExpBar9, 0);  SetBarY(ExpBar10, 0); SetBarY(ExpBar11, 0); SetBarY(ExpBar12, 0);
            SetBarY(ExpBar13, 0); SetBarY(ExpBar14, 0); SetBarY(ExpBar15, 0); SetBarY(ExpBar16, 0);
            SetBarY(ExpBar17, 0); SetBarY(ExpBar18, 0); SetBarY(ExpBar19, 0); SetBarY(ExpBar20, 0);

            SetBarY(EqBar1, 0);   SetBarY(EqBar2, 0);   SetBarY(EqBar3, 0);   SetBarY(EqBar4, 0);
        }

        private int _refreshMediaCounter = 0;

        private void UpdateClockAndBattery()
        {
            TxtCompactTime.Text = DateTime.Now.ToString("HH:mm");
            try
            {
                TxtCompactDate.Text = DateTime.Now.ToString("dddd, dd MMM", new System.Globalization.CultureInfo("id-ID"));
            }
            catch
            {
                TxtCompactDate.Text = DateTime.Now.ToString("dddd, dd MMM");
            }
            TxtDashboardTime.Text = DateTime.Now.ToString("HH:mm");
            try
            {
                TxtDashboardDate.Text = DateTime.Now.ToString("dddd, dd MMMM", new System.Globalization.CultureInfo("id-ID"));
            }
            catch
            {
                TxtDashboardDate.Text = DateTime.Now.ToString("dddd, dd MMMM");
            }

            // Real-Time TX / RX Network Speed Meter
            if (_showNetworkSpeedMeter && NetworkSpeedPanel != null && NetworkSpeedPanel.Visibility == Visibility.Visible && !_isPeeking && !_isFloatingLyrics)
            {
                bool useBits = string.Equals(_speedMeterUnit, "Bits", StringComparison.OrdinalIgnoreCase);
                _sysInfoManager.GetNetworkSpeed(out double rxBytes, out double txBytes);
                TxtSpeedDownload.Text = SystemInfoManager.FormatNetworkSpeed(rxBytes, useBits);
                TxtSpeedUpload.Text = SystemInfoManager.FormatNetworkSpeed(txBytes, useBits);
            }

            // Update System Dashboard meters only when visible
            if (_isExpanded && _currentTab == ExpandedTab.System)
            {
                int cpu = _sysInfoManager.GetCpuUsage();
                int ram = _sysInfoManager.GetRamUsage();
                float vol = _volumeService.Volume;
                int volPercent = (int)Math.Round(vol * 100);

                TxtDashboardCpu.Text = $"{cpu}%";
                CpuMeterBar.Width = Math.Max(2, (cpu / 100.0) * 110);

                TxtDashboardRam.Text = $"{ram}%";
                RamMeterBar.Width = Math.Max(2, (ram / 100.0) * 110);

                TxtDashboardVol.Text = $"{volPercent}%";
                VolumeMeterBar.Width = Math.Max(2, vol * 110);
            }

            TxtCompactTitle.Text = string.Empty;

            var bat = _sysInfoManager.GetBatteryInfo();
            if (bat.HasBattery)
            {
                string icon = bat.IsCharging ? "⚡" : "%";
                TxtExpandedBattery.Text = $"{bat.Percentage}{icon}";
                BatteryToolTip.Content = bat.StatusText;

                if (bat.Percentage <= 20 && !bat.IsCharging)
                    StatusDot.Background = GetBrush("IslandOrange", Colors.Orange);
                else
                    StatusDot.Background = GetBrush("IslandGreen", Colors.LimeGreen);
            }
            else
            {
                TxtExpandedBattery.Text = "AC Power";
                BatteryToolTip.Content = "Running on AC Power";
                StatusDot.Background = GetBrush("IslandAccent", Colors.DodgerBlue);
            }

            // Periodically check if media session was closed (e.g. Spotify quit)
            if (++_refreshMediaCounter % 2 == 0)
            {
                _ = _mediaManager.RefreshMediaInfoAsync();
            }
        }

        private bool _isTrueFullScreen = false;
        private bool _isForegroundMaximized = false;
        private bool _isDuckedForTabs = false;
        private int _tabDuckingBufferCounter = 0;

        private void CheckWindowObstructionState()
        {
            if (_isPinned || _isLyricsPinned || _isFloatingLyrics || _isSettingsOpen || _isExpanded) return;

            var currentMon = _cachedMonitor ?? MonitorService.GetMonitorByIndex(_currentMonitorIndex);
            _cachedMonitor = currentMon;

            bool isTrueFs = NativeMethods.IsForegroundTrueFullScreen(_hwnd, currentMon.Left, currentMon.Top, currentMon.Width, currentMon.Height);
            bool isMaximized = NativeMethods.IsForegroundMaximized(_hwnd, currentMon.Left, currentMon.Top, currentMon.Width, currentMon.Height);

            // 1. True Fullscreen Handling (YouTube Fullscreen with 'F', Games, VLC, Netflix)
            if (isTrueFs != _isTrueFullScreen)
            {
                _isTrueFullScreen = isTrueFs;
                if (_isTrueFullScreen)
                {
                    if (!_isPeeking)
                    {
                        HideToPeek();
                    }
                }
                else
                {
                    if (!_isAutoHide && _isPeeking)
                    {
                        ShowCompact();
                    }
                }
            }

            if (_isTrueFullScreen) return;

            _isForegroundMaximized = isMaximized;

            // 2. Maximized Window with Smart Tab Clearance (Option A)
            if (_isForegroundMaximized)
            {
                CheckTabClearanceZone(currentMon);
            }
            else
            {
                if (_isDuckedForTabs)
                {
                    _isDuckedForTabs = false;
                    _tabDuckingBufferCounter = 0;
                    if (!_isAutoHide && _isPeeking)
                    {
                        ShowCompact();
                    }
                }
            }
        }

        private void CheckTabClearanceZone(MonitorInfo currentMon)
        {
            if (_isPinned || _isLyricsPinned || _isFloatingLyrics || _isExpanded || _isSettingsOpen) return;

            if (MonitorService.GetCursorPos(out var pt))
            {
                // Tab row zone: top 44px of the screen
                int tabTop = currentMon.Top;
                int tabBottom = currentMon.Top + 44;

                // Center tab area behind/near Notch
                int centerX = currentMon.Left + (currentMon.Width / 2);
                double notchWidth = IslandBorder.ActualWidth > 0 ? IslandBorder.ActualWidth : CompactBaseWidth;
                int halfWidth = (int)Math.Round(notchWidth / 2.0) + 24;
                int leftBound = centerX - halfWidth;
                int rightBound = centerX + halfWidth;

                // If cursor is in the tab row (Y >= tabTop + 4 and Y <= tabBottom) and within horizontal notch bounds
                // Note: If cursor touches extreme top bezel (Y <= tabTop + 3), user is targeting the peek bar/notch, not tabs
                bool inTabZone = pt.Y >= (tabTop + 4) && pt.Y <= tabBottom &&
                                 pt.X >= leftBound && pt.X <= rightBound;

                if (inTabZone)
                {
                    _tabDuckingBufferCounter = 0;
                    if (!_isDuckedForTabs)
                    {
                        _isDuckedForTabs = true;
                        if (!_isPeeking)
                        {
                            HideToPeek();
                        }
                    }
                }
                else
                {
                    // User moved cursor away from center tab area (e.g. into webpage or side tabs)
                    if (_isDuckedForTabs)
                    {
                        // Buffer (~100ms) before restoring so cursor adjustments on tabs are smooth
                        if (++_tabDuckingBufferCounter >= 3)
                        {
                            _isDuckedForTabs = false;
                            _tabDuckingBufferCounter = 0;
                            if (!_isAutoHide && _isPeeking)
                            {
                                ShowCompact();
                            }
                        }
                    }
                    else
                    {
                        _tabDuckingBufferCounter = 0;
                    }
                }
            }
        }

        private void OnMediaChanged(MediaTrackInfo track)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _hasMedia = track.HasMedia;
                _isPlaying = track.IsPlaying;

                if (_hasMedia)
                {
                    _trayService.UpdateTooltip($"Aia Dinamic • {track.Title} - {track.Artist}");
                    if (_isExpanded)
                    {
                        if (_currentTab == ExpandedTab.Player)
                        {
                            MediaContentGrid.Visibility = Visibility.Visible;
                            SystemDashboardPanel.Visibility = Visibility.Collapsed;
                            DockDrawerPanel.Visibility = Visibility.Collapsed;
                        }
                        else
                        {
                            MediaContentGrid.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        MediaContentGrid.Visibility = Visibility.Collapsed;
                    }

                    TxtAppSource.Text = track.AppName;
                    TxtCompactTitle.Text = string.Empty;
                    TxtExpandedTitle.Text = track.Title;
                    TxtExpandedArtist.Text = track.Artist;
                    TxtExpandedAlbum.Text = track.Album;

                    if (track.Thumbnail != null)
                    {
                        MiniThumbImage.Source = track.Thumbnail;
                        MiniThumbBorder.Visibility = Visibility.Visible;
                        CompactIcon.Visibility = Visibility.Collapsed;
                        ExpandedAlbumArt.Source = track.Thumbnail;
                        KaraokeAlbumArt.Source = track.Thumbnail;
                    }
                    else
                    {
                        MiniThumbBorder.Visibility = Visibility.Collapsed;
                        CompactIcon.Visibility = Visibility.Visible;
                        ExpandedAlbumArt.Source = null;
                        KaraokeAlbumArt.Source = null;
                    }

                    TxtKaraokeTrackHeader.Text = $"{track.Title} • {track.Artist}";
                    UpdatePlayPauseVisuals(_isPlaying);
                    EqualizerPanel.Visibility = _isPlaying ? Visibility.Visible : Visibility.Collapsed;
                }
                else
                {
                    _trayService.UpdateTooltip("Aia Dinamic • Sistem Siap");
                    TxtAppSource.Text = "SYSTEM DASHBOARD";
                    TxtCompactTitle.Text = string.Empty;

                    if (_isExpanded)
                    {
                        // Maintain current tab! Do NOT force switch away from Player tab
                        if (_currentTab == ExpandedTab.Player)
                        {
                            MediaContentGrid.Visibility = Visibility.Visible;
                            TxtExpandedTitle.Text = "Tidak ada media";
                            TxtExpandedArtist.Text = "Buka pemutar musik untuk memutar lagu";
                            TxtExpandedAlbum.Text = string.Empty;
                        }
                        else
                        {
                            MediaContentGrid.Visibility = Visibility.Collapsed;
                        }
                    }
                    else
                    {
                        MediaContentGrid.Visibility = Visibility.Collapsed;
                    }

                    // If floating lyrics was active, only exit if NOT pinned!
                    if (_isFloatingLyrics && !_isLyricsPinned)
                    {
                        _isFloatingLyrics = false;
                        FloatingLyricsView.Visibility = Visibility.Collapsed;
                        if (_isAutoHide && !_isHovered && !_isPinned)
                        {
                            HideToPeek();
                        }
                        else
                        {
                            ShowCompact();
                        }
                    }
                    else if (_isFloatingLyrics && _isLyricsPinned)
                    {
                        // Keep pinned floating lyrics visible!
                        UpdateFloatingLyricsText();
                    }

                    // Reset karaoke
                    _isLyricsMode = false;
                    KaraokePanel.Visibility = Visibility.Collapsed;
                    if (!_isEqMode)
                    {
                        StandardMediaPanel.Visibility = Visibility.Visible;
                    }
                    MicIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);

                    // Reset album art vinyl
                    MiniThumbBorder.Visibility = Visibility.Collapsed;
                    CompactIcon.Visibility = Visibility.Collapsed;
                    ExpandedAlbumArt.Source = null;
                    KaraokeAlbumArt.Source = null;
                    MiniVinylRotate.Angle = 0;
                    ExpandedVinylRotate.Angle = 0;
                    KaraokeVinylRotate.Angle = 0;

                    // Flatten and clear spectrum
                    FlattenSpectrumBars();
                    _audioSpectrum.ClearLevels();
                    EqualizerPanel.Visibility = Visibility.Collapsed;
                    UpdatePlayPauseVisuals(false);

                    // If expanded, set to appropriate dashboard height
                    if (_isExpanded)
                    {
                        double targetH = _isEqMode ? ExpandedHeightEq : (_isLyricsMode ? ExpandedHeightKaraoke : ExpandedHeightNormal);
                        AnimateBorderHeight(targetH, 200);
                        UpdateNotchShape(ExpandedWidth, targetH);
                    }
                    else if (!_isPinned && !_isHovered && _isAutoHide && !_isEqMode)
                    {
                        _autoHideTimer.Start();
                    }
                }

                if (!_isExpanded && !_isPeeking && !_isFloatingLyrics)
                {
                    double targetWidth = GetCompactTargetWidth();
                    AnimateBorderWidth(targetWidth, 200);
                }

                if (_isLyricsMode && (_isExpanded || _isFloatingLyrics) && _hasMedia)
                {
                    _lastActiveLyricIndex = -2;
                    _ = LoadLyricsForCurrentTrackAsync();
                }
            });
        }

        private void UpdatePlayPauseVisuals(bool isPlaying)
        {
            try
            {
                PlayPauseIcon.Data = Geometry.Parse(isPlaying ? PausePathData : PlayPathData);
            }
            catch { }
        }

        #region Animations & States

        private void IslandBorder_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateNotchShape(e.NewSize.Width, e.NewSize.Height);
        }

        private void UpdateNotchShape(double w, double h)
        {
            if (NotchShapePath == null || w <= 0 || h <= 0) return;

            if (_isFloatingLyrics)
            {
                if (!_lyricStyleNotch)
                {
                    NotchShapePath.Visibility = Visibility.Collapsed;
                    return;
                }
            }

            NotchShapePath.Visibility = Visibility.Visible;

            if (_isPeeking)
            {
                var peekGeom = new RectangleGeometry(new Rect(0, 0, w, h), 1.5, 1.5);
                peekGeom.Freeze();
                NotchShapePath.Data = peekGeom;
                NotchShapePath.Fill = new SolidColorBrush(Color.FromRgb(45, 45, 50));
                NotchShapePath.Stroke = Brushes.Transparent;
                return;
            }

            // Authentic MacBook Pro Notch:
            // Top concave inverted fillets (rTop=8.0), bottom rounded corners (rBottom=16.0)
            // Left & Right corners flare seamlessly into the monitor bezel (Y = 0)
            double rTop = 8.0;
            double rBottom = _isExpanded ? ExpandedRadius : 16.0;
            if (h < (rTop + rBottom))
            {
                rBottom = Math.Max(4.0, h - rTop);
            }
            if (rBottom > (w / 2.0))
            {
                rBottom = Math.Max(2.0, w / 2.0 - 1.0);
            }

            NotchShapePath.Data = CreateMacBookNotchGeometry(w, h, rTop, rBottom);
            NotchShapePath.Fill = GetBrush("IslandBackground", Color.FromRgb(12, 12, 14));
            NotchShapePath.Stroke = GetBrush("IslandBorder", Color.FromRgb(42, 42, 46));
        }

        private static Geometry CreateMacBookNotchGeometry(double w, double h, double rTop, double rBottom)
        {
            // Positive coordinates: 0 to (w + 2*rTop)
            // Left ear: 0 to rTop
            // Notch body: rTop to (rTop + w)
            // Right ear: (rTop + w) to (w + 2*rTop)
            // Bounds: Rect(0, 0, w + 2 * rTop, h)
            // Parent Grid centers NotchShapePath (width = w + 2*rTop) exactly co-aligned with IslandBorder (width = w)!
            string pathData = string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "M 0,0 A {0:F1},{0:F1} 0 0 1 {0:F1},{0:F1} L {0:F1},{1:F1} A {2:F1},{2:F1} 0 0 0 {3:F1},{4:F1} L {5:F1},{4:F1} A {2:F1},{2:F1} 0 0 0 {6:F1},{1:F1} L {6:F1},{0:F1} A {0:F1},{0:F1} 0 0 1 {7:F1},0",
                rTop,                  // {0}
                h - rBottom,           // {1}
                rBottom,               // {2}
                rTop + rBottom,        // {3}
                h,                     // {4}
                rTop + w - rBottom,    // {5}
                rTop + w,              // {6}
                w + 2 * rTop           // {7}
            );
            var geom = Geometry.Parse(pathData);
            geom.Freeze();
            return geom;
        }

        private void HideToPeek()
        {
            if (_isPinned || _isLyricsPinned || _isFloatingLyrics || _isExpanded || _isSettingsOpen) return;
            if (!_isDuckedForTabs && _isHovered) return;
            if (!_isAutoHide && !_isTrueFullScreen && !_isDuckedForTabs) return;

            _isPeeking = true;
            _isExpanded = false;
            _isFloatingLyrics = false;
            _isCompactDockActive = false;
            RefreshCompactDockSubMode();
            FloatingLyricsView.Visibility = Visibility.Collapsed;


            if (BottomVolumeHUD != null && BottomVolumeHUD.Visibility == Visibility.Visible)
            {
                BottomVolumeHUD.Visibility = Visibility.Collapsed;
                _bottomVolumeTimer.Stop();
            }
            if (ExternalRightVolumeHUD != null && ExternalRightVolumeHUD.Visibility == Visibility.Visible)
            {
                ExternalRightVolumeHUD.Visibility = Visibility.Collapsed;
                _rightVolumeTimer.Stop();
            }

            // Fade out contents
            var fadeAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
            CompactView.BeginAnimation(OpacityProperty, fadeAnim);
            ExpandedView.BeginAnimation(OpacityProperty, fadeAnim);
            CompactView.Visibility = Visibility.Collapsed;
            ExpandedView.Visibility = Visibility.Collapsed;

            // Shrink capsule to subtle notch indicator at top bezel
            IslandBorder.Cursor = Cursors.Hand;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            var widthAnim = new DoubleAnimation(PeekWidth, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };
            var heightAnim = new DoubleAnimation(PeekHeight, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease };

            IslandBorder.Background = Brushes.Transparent;
            IslandBorder.BorderThickness = new Thickness(0);
            IslandBorder.BorderBrush = Brushes.Transparent;
            IslandBorder.Effect = null;
            widthAnim.Completed += (s, e) => UpdateNotchShape(PeekWidth, PeekHeight);
            IslandBorder.BeginAnimation(WidthProperty, widthAnim);
            IslandBorder.BeginAnimation(HeightProperty, heightAnim);
        }

        private void ShowCompact()
        {
            _isPeeking = false;
            _isExpanded = false;
            _isFloatingLyrics = false;
            _isCompactDockActive = false;
            RefreshCompactDockSubMode();
            FloatingLyricsView.Visibility = Visibility.Collapsed;


            if (ExternalRightVolumeHUD != null && ExternalRightVolumeHUD.Visibility == Visibility.Visible)
            {
                ExternalRightVolumeHUD.Visibility = Visibility.Collapsed;
                _rightVolumeTimer.Stop();
            }

            double targetWidth = GetCompactTargetWidth();
            IslandBorder.Cursor = Cursors.Hand;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            var widthAnim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease };
            var heightAnim = new DoubleAnimation(CompactHeight, TimeSpan.FromMilliseconds(250)) { EasingFunction = ease };

            IslandBorder.Background = Brushes.Transparent;
            IslandBorder.BorderThickness = new Thickness(0);
            IslandBorder.BorderBrush = Brushes.Transparent;
            IslandBorder.Effect = null;

            widthAnim.Completed += (s, e) => UpdateNotchShape(targetWidth, CompactHeight);
            IslandBorder.BeginAnimation(WidthProperty, widthAnim);
            IslandBorder.BeginAnimation(HeightProperty, heightAnim);

            // Fade in compact view
            ExpandedView.Visibility = Visibility.Collapsed;
            ExpandedView.Opacity = 0;
            CompactView.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200))
            {
                BeginTime = TimeSpan.FromMilliseconds(50),
                EasingFunction = ease
            };
            CompactView.BeginAnimation(OpacityProperty, fadeIn);
        }

        private void ExpandIsland()
        {
            _isPeeking = false;
            _isExpanded = true;
            _isFloatingLyrics = false;
            FloatingLyricsView.Visibility = Visibility.Collapsed;


            if (BottomVolumeHUD != null && BottomVolumeHUD.Visibility == Visibility.Visible)
            {
                BottomVolumeHUD.Visibility = Visibility.Collapsed;
                _bottomVolumeTimer.Stop();
            }

            var fadeOutCompact = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
            fadeOutCompact.Completed += (s, e) => CompactView.Visibility = Visibility.Collapsed;
            CompactView.BeginAnimation(OpacityProperty, fadeOutCompact);

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            IslandBorder.Cursor = Cursors.Hand;
            var widthAnim = new DoubleAnimation(ExpandedWidth, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
            var heightAnim = new DoubleAnimation(ExpandedHeight, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };

            IslandBorder.Background = Brushes.Transparent;
            IslandBorder.BorderThickness = new Thickness(0);
            IslandBorder.BorderBrush = Brushes.Transparent;
            IslandBorder.Effect = null;

            widthAnim.Completed += (s, e) => UpdateNotchShape(ExpandedWidth, ExpandedHeight);
            IslandBorder.BeginAnimation(WidthProperty, widthAnim);
            IslandBorder.BeginAnimation(HeightProperty, heightAnim);

            TxtAppSource.Text = _hasMedia ? _mediaManager.CurrentTrack.AppName : "SYSTEM DASHBOARD";

            // Activate tab: if compact dock active, open Apps; otherwise preserve user's chosen tab
            if (_isCompactDockActive)
            {
                SetExpandedTab(ExpandedTab.Apps);
            }
            else
            {
                SetExpandedTab(_currentTab);
            }

            ExpandedView.Visibility = Visibility.Visible;
            var fadeInExpanded = new DoubleAnimation(1, TimeSpan.FromMilliseconds(250))
            {
                BeginTime = TimeSpan.FromMilliseconds(60),
                EasingFunction = ease
            };
            ExpandedView.BeginAnimation(OpacityProperty, fadeInExpanded);
        }

        private void ShowFloatingLyrics()
        {
            if (_isSettingsOpen) return;

            _isPeeking = false;
            _isExpanded = false;
            _isFloatingLyrics = true;
            _isLyricsPinned = true;
            _isLyricsMode = true;
            _lastActiveLyricIndex = -2;
            IslandBorder.Cursor = Cursors.Arrow;

            if (BtnCloseFloatingLyrics != null)
            {
                BtnCloseFloatingLyrics.Opacity = 1;
            }

            var fadeAnim = new DoubleAnimation(0, TimeSpan.FromMilliseconds(100));
            CompactView.BeginAnimation(OpacityProperty, fadeAnim);
            ExpandedView.BeginAnimation(OpacityProperty, fadeAnim);
            CompactView.Visibility = Visibility.Collapsed;
            ExpandedView.Visibility = Visibility.Collapsed;

            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            if (_lyricStyleNotch)
            {
                IslandBorder.CornerRadius = new CornerRadius(0);
                IslandBorder.Background = Brushes.Transparent;
                IslandBorder.BorderThickness = new Thickness(0);
                IslandBorder.BorderBrush = Brushes.Transparent;
                IslandBorder.Effect = null;
                NotchShapePath.Visibility = Visibility.Visible;
            }
            else
            {
                NotchShapePath.Visibility = Visibility.Collapsed;
                IslandBorder.CornerRadius = new CornerRadius(0);
                IslandBorder.Background = Brushes.Transparent;
                IslandBorder.BorderThickness = new Thickness(0);
                IslandBorder.BorderBrush = Brushes.Transparent;
                IslandBorder.Effect = null;
            }

            FloatingLyricsView.Visibility = Visibility.Visible;
            var fadeInLyrics = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease };
            FloatingLyricsView.BeginAnimation(OpacityProperty, fadeInLyrics);

            UpdateFloatingLyricsText();
        }

        private void CloseFloatingLyrics()
        {
            _isLyricsPinned = false;
            _isFloatingLyrics = false;
            _isLyricsMode = false;
            _lastFloatingLyricIndex = -99;
            _lastActiveLyricIndex = -2;
            IslandBorder.Cursor = Cursors.Hand;

            FloatingLyricsView.Visibility = Visibility.Collapsed;
            MicIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);

            if (_isAutoHide && !_isHovered)
            {
                HideToPeek();
            }
            else
            {
                ShowCompact();
            }
        }

        private void UpdateFloatingLyricsText()
        {
            if (!_hasMedia || _currentLyrics == null)
            {
                string title = (_mediaManager?.CurrentTrack != null && !string.IsNullOrWhiteSpace(_mediaManager.CurrentTrack.Title))
                    ? $"{_mediaManager.CurrentTrack.Title} • {_mediaManager.CurrentTrack.Artist}"
                    : "♫ Menunggu musik diputar...";
                ApplyFloatingLyric(title, string.Empty, true, -1);
                return;
            }

            if (!_currentLyrics.HasSynced)
            {
                ApplyFloatingLyric(
                    $"{_mediaManager.CurrentTrack.Title} • {_mediaManager.CurrentTrack.Artist}",
                    "Lirik polos (tanpa sinkronisasi waktu)",
                    false,
                    -1);
                return;
            }

            TimeSpan currentPos = _mediaManager.GetCurrentPosition();
            var lines = _currentLyrics.SyncedLines;

            int activeIndex = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Timestamp <= currentPos)
                    activeIndex = i;
                else
                    break;
            }

            if (activeIndex == -1)
            {
                string nextLine = lines.Count > 0 ? lines[0].Text : string.Empty;
                ApplyFloatingLyric(
                    $"{_mediaManager.CurrentTrack.Title} • {_mediaManager.CurrentTrack.Artist}",
                    nextLine,
                    true,
                    -1);
            }
            else
            {
                string curLine = lines[activeIndex].Text;
                string nextLine = (activeIndex < lines.Count - 1) ? lines[activeIndex + 1].Text : string.Empty;
                ApplyFloatingLyric(curLine, nextLine, false, activeIndex);
            }
        }

        private void ApplyFloatingLyric(string currentText, bool isSecondary)
        {
            ApplyFloatingLyric(currentText, string.Empty, isSecondary, -1);
        }

        private void ApplyFloatingLyric(string currentText, string nextText = "", bool isSecondary = false, int activeIndex = -1)
        {
            currentText = LyricsService.CleanLyricText(currentText);
            nextText = LyricsService.CleanLyricText(nextText);

            bool lineChanged = (activeIndex != _lastFloatingLyricIndex);
            _lastFloatingLyricIndex = activeIndex;

            TxtFloatingLyricCurrent.Text = currentText;
            TxtFloatingLyricCurrent.Foreground = isSecondary ? GetBrush("IslandTextSecondary", Colors.Gray) : Brushes.White;

            if (_lyricShowNextPreview && !string.IsNullOrWhiteSpace(nextText))
            {
                TxtFloatingLyricNext.Text = nextText;
                TxtFloatingLyricNext.Visibility = Visibility.Visible;
                TxtFloatingLyricNext.Opacity = _lyricNextOpacity;
            }
            else
            {
                TxtFloatingLyricNext.Text = string.Empty;
                TxtFloatingLyricNext.Visibility = Visibility.Collapsed;
            }

            double curFontSize = GetLyricCurrentFontSize();
            double nextFontSize = GetLyricNextFontSize();

            var currentMon = _cachedMonitor ?? MonitorService.GetMonitorByIndex(_currentMonitorIndex);
            double screenWidth = currentMon?.Width > 0 ? currentMon.Width : SystemParameters.PrimaryScreenWidth;
            if (screenWidth <= 0) screenWidth = 1920;

            // Maximum allowed width on the user's active monitor with 60px safe margin
            double maxAllowedWidth = Math.Max(380.0, screenWidth - 60.0);

            // Symmetrical reserves: Left icon + Right dismiss + Margins & padding
            double iconReserve = Math.Clamp(curFontSize * 1.5, 36.0, 70.0);
            double paddingReserve = Math.Clamp(curFontSize * 1.6, 40.0, 80.0);
            double extraReserves = iconReserve * 2 + paddingReserve;

            double availableContentWidth = Math.Max(200.0, maxAllowedWidth - extraReserves);

            // 1. Dynamic Fit-to-Width Auto-Scale for Active Lyric line (eliminates '...' truncation)
            double effectiveCurFontSize = curFontSize;
            double widthCur = MeasureLyricWidth(currentText, curFontSize, FontWeights.SemiBold);
            if (widthCur > availableContentWidth && widthCur > 0)
            {
                double ratio = availableContentWidth / widthCur;
                // Auto scale down proportionally so the long sentence fits 100% without ellipsis
                effectiveCurFontSize = Math.Max(13.0, Math.Floor(curFontSize * ratio * 0.96));
                widthCur = MeasureLyricWidth(currentText, effectiveCurFontSize, FontWeights.SemiBold);
            }
            TxtFloatingLyricCurrent.FontSize = effectiveCurFontSize;

            // 2. Dynamic Fit-to-Width Auto-Scale for Upcoming Preview line
            double effectiveNextFontSize = nextFontSize;
            double widthNext = 0;
            if (_lyricShowNextPreview && !string.IsNullOrWhiteSpace(nextText))
            {
                widthNext = MeasureLyricWidth(nextText, nextFontSize, FontWeights.Normal);
                if (widthNext > availableContentWidth && widthNext > 0)
                {
                    double ratioNext = availableContentWidth / widthNext;
                    effectiveNextFontSize = Math.Max(10.5, Math.Floor(nextFontSize * ratioNext * 0.96));
                    widthNext = MeasureLyricWidth(nextText, effectiveNextFontSize, FontWeights.Normal);
                }
            }
            TxtFloatingLyricNext.FontSize = effectiveNextFontSize;

            // Smooth Teleprompter / Rolling Animation on line change
            if (lineChanged && FloatingLyricsStack != null && FloatingLyricsTranslate != null)
            {
                double rollDistance = Math.Clamp(effectiveCurFontSize * 0.45, 6.0, 28.0);
                var rollAnim = new DoubleAnimation(rollDistance, 0.0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                FloatingLyricsTranslate.BeginAnimation(TranslateTransform.YProperty, rollAnim);

                var fadeAnim = new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(200));
                TxtFloatingLyricCurrent.BeginAnimation(OpacityProperty, fadeAnim);
            }

            if (!_isFloatingLyrics) return;

            double contentWidth = Math.Max(widthCur, widthNext);
            double targetWidth = Math.Clamp(contentWidth + extraReserves, 380, maxAllowedWidth);
            double targetHeight = (_lyricShowNextPreview && !string.IsNullOrWhiteSpace(nextText))
                ? GetLyricTwoLinesHeight(effectiveCurFontSize, effectiveNextFontSize)
                : GetLyricSingleHeight(effectiveCurFontSize);

            AnimateBorderWidth(targetWidth, 180);
            AnimateBorderHeight(targetHeight, 180);
        }

        private double MeasureLyricWidth(string text, double fontSize, FontWeight fontWeight)
        {
            if (string.IsNullOrWhiteSpace(text)) return 200;
            try
            {
                var formattedText = new FormattedText(
                    text,
                    System.Globalization.CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    new Typeface(TxtFloatingLyricCurrent.FontFamily, FontStyles.Normal, fontWeight, FontStretches.Normal),
                    fontSize,
                    Brushes.White,
                    VisualTreeHelper.GetDpi(this).PixelsPerDip
                );
                return formattedText.Width;
            }
            catch
            {
                return text.Length * (fontSize * 0.62);
            }
        }

        private void AnimateBorderWidth(double targetWidth, double durationMs, IEasingFunction? easing = null, EventHandler? completed = null)
        {
            var anim = new DoubleAnimation(targetWidth, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = easing ?? new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            if (completed != null) anim.Completed += completed;
            IslandBorder.BeginAnimation(WidthProperty, anim);
        }

        private void AnimateBorderHeight(double targetHeight, double durationMs, IEasingFunction? easing = null, EventHandler? completed = null)
        {
            var anim = new DoubleAnimation(targetHeight, TimeSpan.FromMilliseconds(durationMs))
            {
                EasingFunction = easing ?? new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            if (completed != null) anim.Completed += completed;
            IslandBorder.BeginAnimation(HeightProperty, anim);
        }

        #endregion

        #region Mouse & Interaction Handlers

        private void IslandBorder_MouseEnter(object sender, MouseEventArgs e)
        {
            _isHovered = true;
            _autoHideTimer.Stop();

            if (_isShowingNotification || _isShowingVolumeHUD)
            {
                return;
            }

            if (_isFloatingLyrics)
            {
                return;
            }

            if (_isPeeking && !_isDuckedForTabs)
            {
                ShowCompact();
            }
        }

        private void IslandBorder_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;
            float delta = e.Delta > 0 ? 0.02f : -0.02f;
            _volumeService.ChangeVolume(delta);

            if (_isPeeking)
            {
                // 1. PEEK BAR (Sembunyi) -> Dropdown horizontal pill at top bezel
                ShowPeekingVolumeHUD(_volumeService.Volume, _volumeService.IsMuted);
            }
            else if (!_isExpanded)
            {
                // 2. NOTCH KECIL (Hover) -> Horizontal capsule pill BELOW the notch
                ShowBottomVolumeHUD(_volumeService.Volume, _volumeService.IsMuted);
            }
            else
            {
                // 3. NOTCH BESAR (Klik / Expanded Card) -> Vertical capsule pill OUTSIDE on the RIGHT
                ShowRightVolumeHUD(_volumeService.Volume, _volumeService.IsMuted);
            }
        }

        private void ShowBottomVolumeHUD(float volume, bool isMuted)
        {
            _bottomVolumeTimer.Stop();

            int percent = (int)Math.Round(volume * 100);
            TxtBottomVolumeIcon.Text = isMuted ? "🔇" : (percent == 0 ? "🔈" : (percent < 50 ? "🔉" : "🔊"));
            TxtBottomVolumePercent.Text = isMuted ? "Muted" : $"{percent}%";

            // Samakan panjang/lebar dengan notch kecil secara dinamis
            double targetWidth = IslandBorder.ActualWidth > 0 ? IslandBorder.ActualWidth : GetCompactTargetWidth();
            BottomVolumeHUD.Width = targetWidth;
            BottomVolumeHUD.Height = CompactHeight;
            BottomVolumeHUD.CornerRadius = new CornerRadius(CompactHeight / 2.0);

            // Hitung lebar track slider berdasarkan lebar dinamis
            double availableTrackWidth = Math.Max(20.0, targetWidth - 76.0);
            BottomVolumeTrack.Width = availableTrackWidth;
            double targetFill = Math.Max(2.0, volume * availableTrackWidth);

            var fillAnim = new DoubleAnimation(targetFill, TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BottomVolumeProgressBar.BeginAnimation(WidthProperty, fillAnim);
            BottomVolumeProgressBar.Background = isMuted ? GetBrush("IslandTextSecondary", Colors.Gray) : new SolidColorBrush(Colors.White);

            // Posisi Y persis 8px di bawah Notch Kecil
            double currentNotchHeight = IslandBorder.ActualHeight > 0 ? IslandBorder.ActualHeight : CompactHeight;
            double targetY = currentNotchHeight + 8.0;

            BottomVolumeHUD.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideDown = new DoubleAnimation(targetY, TimeSpan.FromMilliseconds(120))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            BottomVolumeHUD.BeginAnimation(OpacityProperty, fadeIn);
            BottomVolumeTransform.BeginAnimation(TranslateTransform.YProperty, slideDown);

            _bottomVolumeTimer.Start();
        }

        private void HideBottomVolumeHUD()
        {
            if (BottomVolumeHUD.Visibility != Visibility.Visible) return;

            double currentY = BottomVolumeTransform.Y;
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var slideUp = new DoubleAnimation(currentY - 10.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                if (BottomVolumeHUD.Opacity <= 0.05)
                {
                    BottomVolumeHUD.Visibility = Visibility.Collapsed;
                }
            };
            BottomVolumeHUD.BeginAnimation(OpacityProperty, fadeOut);
            BottomVolumeTransform.BeginAnimation(TranslateTransform.YProperty, slideUp);
        }

        private void ShowRightVolumeHUD(float volume, bool isMuted)
        {
            _rightVolumeTimer.Stop();

            int percent = (int)Math.Round(volume * 100);
            TxtExternalVolumeIcon.Text = isMuted ? "🔇" : (percent == 0 ? "🔈" : (percent < 50 ? "🔉" : "🔊"));
            TxtExternalVolumePercent.Text = isMuted ? "0%" : $"{percent}%";

            // Samakan tinggi persis dengan Notch Besar (Expanded Card)
            double currentNotchHeight = IslandBorder.ActualHeight > 0 ? IslandBorder.ActualHeight : ExpandedHeight;
            ExternalRightVolumeHUD.Height = currentNotchHeight;
            ExternalRightVolumeHUD.CornerRadius = new CornerRadius(10);
            ExternalRightVolumeTransform.Y = 0; // Sejajar flush dengan bagian atas notch

            // Hitung tinggi track vertical dynamically berdasarkan tinggi notch (padding/margin ~48px)
            double maxFillHeight = Math.Max(20.0, currentNotchHeight - 48.0);
            ExternalVolumeTrack.Height = maxFillHeight;

            double targetFill = Math.Max(2.0, volume * maxFillHeight);
            var fillAnim = new DoubleAnimation(targetFill, TimeSpan.FromMilliseconds(100))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ExternalVolumeFill.BeginAnimation(HeightProperty, fillAnim);
            ExternalVolumeFill.Background = isMuted ? GetBrush("IslandTextSecondary", Colors.Gray) : new SolidColorBrush(Colors.White);

            // Compute target X coordinate so it sits strictly OUTSIDE the right edge of Notch Besar
            double rightEdge = (IslandBorder.ActualWidth > 0 ? IslandBorder.ActualWidth : ExpandedWidth) / 2.0;
            double gap = 12.0;
            double targetX = rightEdge + gap + (ExternalRightVolumeHUD.Width / 2.0);

            ExternalRightVolumeHUD.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideIn = new DoubleAnimation(targetX, TimeSpan.FromMilliseconds(130))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            ExternalRightVolumeHUD.BeginAnimation(OpacityProperty, fadeIn);
            ExternalRightVolumeTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);

            _rightVolumeTimer.Start();
        }

        private void HideRightVolumeHUD()
        {
            if (ExternalRightVolumeHUD.Visibility != Visibility.Visible) return;

            double currentX = ExternalRightVolumeTransform.X;
            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var slideOut = new DoubleAnimation(currentX + 8.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) =>
            {
                if (ExternalRightVolumeHUD.Opacity <= 0.05)
                {
                    ExternalRightVolumeHUD.Visibility = Visibility.Collapsed;
                }
            };
            ExternalRightVolumeHUD.BeginAnimation(OpacityProperty, fadeOut);
            ExternalRightVolumeTransform.BeginAnimation(TranslateTransform.XProperty, slideOut);
        }

        private void ShowPeekingVolumeHUD(float volume, bool isMuted)
        {
            _isShowingVolumeHUD = true;
            _volumeHudTimer.Stop();
            _autoHideTimer.Stop();

            CompactView.Visibility = Visibility.Collapsed;
            ExpandedView.Visibility = Visibility.Collapsed;
            FloatingLyricsView.Visibility = Visibility.Collapsed;
            NotificationBannerView.Visibility = Visibility.Collapsed;

            int percent = (int)Math.Round(volume * 100);
            TxtVolumeIcon.Text = isMuted ? "🔇" : (percent == 0 ? "🔈" : (percent < 50 ? "🔉" : "🔊"));
            TxtVolumePercent.Text = isMuted ? "Muted" : $"{percent}%";
            VolumeProgressBar.Width = Math.Max(4, volume * 110);
            VolumeProgressBar.Background = isMuted ? GetBrush("IslandTextSecondary", Colors.Gray) : new SolidColorBrush(Colors.White);

            IslandBorder.Background = Brushes.Transparent;
            IslandBorder.BorderThickness = new Thickness(0);
            IslandBorder.BorderBrush = Brushes.Transparent;
            IslandBorder.Effect = null;
            UpdateNotchShape(220, 36);

            // Smooth dropdown from top bezel
            VolumeOverlayView.Visibility = Visibility.Visible;
            var fadeIn = new DoubleAnimation(1.0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            VolumeOverlayView.BeginAnimation(OpacityProperty, fadeIn);

            AnimateBorderWidth(220, 200, new CubicEase { EasingMode = EasingMode.EaseOut });
            AnimateBorderHeight(36, 200, new CubicEase { EasingMode = EasingMode.EaseOut });

            _volumeHudTimer.Start();
        }

        private void DismissPeekingVolumeHUD()
        {
            _volumeHudTimer.Stop();

            var fadeOut = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            VolumeOverlayView.BeginAnimation(OpacityProperty, fadeOut);

            AnimateBorderWidth(PeekWidth, 220, new CubicEase { EasingMode = EasingMode.EaseIn });
            AnimateBorderHeight(PeekHeight, 220, new CubicEase { EasingMode = EasingMode.EaseIn }, completed: (s, e) =>
            {
                if (!_isShowingVolumeHUD) return;
                _isShowingVolumeHUD = false;
                VolumeOverlayView.Visibility = Visibility.Collapsed;

                if (_isPeeking)
                {
                    IslandBorder.Background = Brushes.Transparent;
                    IslandBorder.BorderThickness = new Thickness(0);
                    IslandBorder.BorderBrush = Brushes.Transparent;
                    IslandBorder.Effect = null;
                    UpdateNotchShape(PeekWidth, PeekHeight);
                }
            });
        }

        private void OnNotificationReceived(NotificationItem item)
        {
            ShowNotificationBanner(item);
        }

        private void ShowNotificationBanner(NotificationItem item)
        {
            Dispatcher.InvokeAsync(() =>
            {
                _isShowingNotification = true;
                _notificationTimer.Stop();
                _autoHideTimer.Stop();

                CompactView.Visibility = Visibility.Collapsed;
                ExpandedView.Visibility = Visibility.Collapsed;
                FloatingLyricsView.Visibility = Visibility.Collapsed;
                VolumeOverlayView.Visibility = Visibility.Collapsed;

                TxtNotifIcon.Text = item.IconSymbol;
                TxtNotifApp.Text = item.AppName;
                TxtNotifTitle.Text = item.Title;
                TxtNotifMessage.Text = string.IsNullOrWhiteSpace(item.Message) ? "Notifikasi baru diterima" : item.Message;

                IslandBorder.Background = Brushes.Transparent;
                IslandBorder.BorderThickness = new Thickness(0);
                IslandBorder.BorderBrush = Brushes.Transparent;
                IslandBorder.Effect = null;
                UpdateNotchShape(440, 50);

                AnimateBorderWidth(440, 250);
                AnimateBorderHeight(50, 250);

                NotificationBannerView.Visibility = Visibility.Visible;
                var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
                NotificationBannerView.BeginAnimation(OpacityProperty, fadeIn);

                _notificationTimer.Start();
            });
        }

        private void DismissNotification()
        {
            _notificationTimer.Stop();
            _isShowingNotification = false;
            NotificationBannerView.Visibility = Visibility.Collapsed;

            if (_isExpanded)
            {
                ExpandedView.Visibility = Visibility.Visible;
                AnimateBorderWidth(ExpandedWidth, 200);
                AnimateBorderHeight(ExpandedHeight, 200);
            }
            else if (_isFloatingLyrics)
            {
                FloatingLyricsView.Visibility = Visibility.Visible;
                UpdateFloatingLyricsText();
            }
            else if (_isPeeking)
            {
                HideToPeek();
            }
            else
            {
                CompactView.Visibility = Visibility.Visible;
                AnimateBorderWidth(GetCompactTargetWidth(), 200);
                AnimateBorderHeight(CompactHeight, 200);
            }
        }

        private void NotificationBanner_Click(object sender, MouseButtonEventArgs e)
        {
            DismissNotification();
        }

        #region App Dock, Shortcuts & Segmented Tabs Management

        private double GetCompactTargetWidth()
        {
            double baseWidth = _showNetworkSpeedMeter ? 245.0 : CompactBaseWidth;
            double width = baseWidth;
            if (_isCompactDockActive && _dockService.Shortcuts.Count > 0)
            {
                width += (_dockService.Shortcuts.Count * 28.0) + 16.0;
            }
            return width;
        }

        private void RefreshDockUI()
        {
            RefreshCompactDockSubMode();
        }

        private void RefreshCompactDockSubMode()
        {
            DockInlineList.Visibility = (_isCompactDockActive && _dockService.Shortcuts.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
            GridDrawerIcon.Fill = _isCompactDockActive
                ? GetBrush("IslandGreen", Color.FromRgb(48, 209, 88))
                : new SolidColorBrush(Color.FromRgb(142, 142, 147));

            if (!_isExpanded && !_isPeeking && !_isFloatingLyrics)
            {
                AnimateBorderWidth(GetCompactTargetWidth(), 220);
                AnimateBorderHeight(CompactHeight, 220);
            }
        }

        private void SetExpandedTab(ExpandedTab tab)
        {
            _currentTab = tab;

            // Reset tab button states
            TabBtnPlayer.Background = Brushes.Transparent;
            TxtTabPlayer.Foreground = new SolidColorBrush(Color.FromRgb(142, 142, 147));

            TabBtnApps.Background = Brushes.Transparent;
            TxtTabApps.Foreground = new SolidColorBrush(Color.FromRgb(142, 142, 147));
            TabAppsIcon.Fill = new SolidColorBrush(Color.FromRgb(142, 142, 147));

            TabBtnSystem.Background = Brushes.Transparent;
            TxtTabSystem.Foreground = new SolidColorBrush(Color.FromRgb(142, 142, 147));

            // Hide panels
            MediaContentGrid.Visibility = Visibility.Collapsed;
            DockDrawerPanel.Visibility = Visibility.Collapsed;
            SystemDashboardPanel.Visibility = Visibility.Collapsed;

            var activeBg = new SolidColorBrush(Color.FromRgb(44, 44, 46));

            switch (tab)
            {
                case ExpandedTab.Player:
                    TabBtnPlayer.Background = activeBg;
                    TxtTabPlayer.Foreground = Brushes.White;
                    MediaContentGrid.Visibility = Visibility.Visible;
                    if (_isExpanded)
                    {
                        double targetH = _isEqMode ? ExpandedHeightEq : ((_hasMedia && _isLyricsMode) ? ExpandedHeightKaraoke : ExpandedHeightNormal);
                        AnimateBorderHeight(targetH, 200);
                        UpdateNotchShape(ExpandedWidth, targetH);
                    }
                    if (_isLyricsMode && _hasMedia)
                    {
                        _ = LoadLyricsForCurrentTrackAsync();
                    }
                    break;

                case ExpandedTab.Apps:
                    TabBtnApps.Background = activeBg;
                    TxtTabApps.Foreground = GetBrush("IslandGreen", Color.FromRgb(48, 209, 88));
                    TabAppsIcon.Fill = GetBrush("IslandGreen", Color.FromRgb(48, 209, 88));
                    DockDrawerPanel.Visibility = Visibility.Visible;
                    if (_isExpanded)
                    {
                        AnimateBorderHeight(ExpandedHeightNormal, 200);
                    }
                    break;

                case ExpandedTab.System:
                    TabBtnSystem.Background = activeBg;
                    TxtTabSystem.Foreground = GetBrush("IslandAccent", Color.FromRgb(10, 132, 255));
                    SystemDashboardPanel.Visibility = Visibility.Visible;
                    UpdateClockAndBattery();
                    if (_isExpanded)
                    {
                        AnimateBorderHeight(ExpandedHeightNormal, 200);
                    }
                    break;
            }
        }

        private void TabBtnPlayer_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            SetExpandedTab(ExpandedTab.Player);
        }

        private void TabBtnApps_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            SetExpandedTab(ExpandedTab.Apps);
        }

        private void TabBtnSystem_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            SetExpandedTab(ExpandedTab.System);
        }

        private DateTime _dockOpenedTime = DateTime.MinValue;
        private object? _pressedDockShortcut = null;

        private void BtnGridDrawer_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (_isExpanded)
            {
                SetExpandedTab(ExpandedTab.Apps);
                return;
            }

            if (_dockService.Shortcuts.Count == 0)
            {
                ExpandIsland();
                SetExpandedTab(ExpandedTab.Apps);
                return;
            }

            // In compact view: Toggle compact dock submode
            _isCompactDockActive = !_isCompactDockActive;
            if (_isCompactDockActive)
            {
                _dockOpenedTime = DateTime.UtcNow;
                _pressedDockShortcut = null;
            }
            RefreshCompactDockSubMode();
        }

        private void DockItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement elem)
            {
                _pressedDockShortcut = elem.Tag;
            }
        }

        private void DockItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;

            // 1. Safety Cooldown Window: ignore clicks within 200ms of drawer opening
            if ((DateTime.UtcNow - _dockOpenedTime).TotalMilliseconds < 200)
            {
                _pressedDockShortcut = null;
                return;
            }

            // 2. Launch or toggle application shortcut
            if (sender is FrameworkElement elem && elem.Tag is AppShortcut shortcut)
            {
                _pressedDockShortcut = null;
                _dockService.LaunchOrToggle(shortcut);
            }
        }

        private void DockItem_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
            if (sender is FrameworkElement elem && elem.Tag is AppShortcut shortcut)
            {
                var menu = new ContextMenu
                {
                    Background = new SolidColorBrush(Color.FromRgb(28, 28, 30)),
                    Foreground = new SolidColorBrush(Color.FromRgb(242, 242, 247)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(44, 44, 46))
                };

                var titleItem = new MenuItem
                {
                    Header = $"📌 {shortcut.Name}",
                    IsEnabled = false,
                    FontWeight = FontWeights.Bold
                };
                menu.Items.Add(titleItem);
                menu.Items.Add(new Separator { Background = new SolidColorBrush(Color.FromRgb(44, 44, 46)) });

                var openItem = new MenuItem { Header = "▶ Buka / Beralih Aplikasi" };
                openItem.Click += (s, args) => _dockService.LaunchOrToggle(shortcut);
                menu.Items.Add(openItem);

                var removeItem = new MenuItem { Header = "🗑️ Hapus Pintasan Ini" };
                removeItem.Click += (s, args) =>
                {
                    _dockService.RemoveShortcut(shortcut.Id);
                    if (!_isExpanded && !_isPeeking && !_isFloatingLyrics)
                    {
                        AnimateBorderWidth(GetCompactTargetWidth(), 200);
                    }
                };
                menu.Items.Add(removeItem);

                menu.PlacementTarget = elem;
                menu.IsOpen = true;
            }
        }

        private void MenuDockMode_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag)
            {
                if (Enum.TryParse<DockDisplayMode>(tag, true, out var mode))
                {
                    _dockService.SaveMode(mode);
                    _settingsService.Current.DockMode = mode.ToString();
                    _settingsService.Save();
                    RefreshDockUI();
                    UpdateContextMenuCheckmarks();

                    if (!_isExpanded && !_isPeeking && !_isFloatingLyrics)
                    {
                        AnimateBorderWidth(GetCompactTargetWidth(), 200);
                    }
                }
            }
        }

        private void IslandBorder_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropOverlay.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void IslandBorder_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                DropOverlay.Visibility = Visibility.Visible;
                e.Effects = DragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void IslandBorder_DragLeave(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }

        private void IslandBorder_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]?)e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0)
                {
                    int addedCount = 0;
                    foreach (var file in files)
                    {
                        var shortcut = _dockService.AddShortcutFromPath(file);
                        if (shortcut != null)
                        {
                            addedCount++;
                        }
                    }

                    if (addedCount > 0 && !_isExpanded && !_isPeeking && !_isFloatingLyrics)
                    {
                        AnimateBorderWidth(GetCompactTargetWidth(), 200);
                    }
                }
            }
            e.Handled = true;
        }

        private static bool IsVisualChildOf(DependencyObject? child, DependencyObject? parent)
        {
            if (child == null || parent == null) return false;
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                if (current is Visual or System.Windows.Media.Media3D.Visual3D)
                {
                    current = VisualTreeHelper.GetParent(current);
                }
                else
                {
                    current = LogicalTreeHelper.GetParent(current);
                }
            }
            return false;
        }

        #endregion

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == App.WM_SHOW_INSTANCE)
            {
                Dispatcher.Invoke(() =>
                {
                    _isHovered = true;
                    _autoHideTimer.Stop();
                    NativeMethods.SetForegroundWindow(_hwnd);
                    if (_isPeeking)
                    {
                        ShowCompact();
                    }
                    else if (!_isExpanded)
                    {
                        ExpandIsland();
                    }
                });
                handled = true;
                return IntPtr.Zero;
            }
            return _trayService.HandleMessage(msg, wParam, lParam, ref handled);
        }

        protected override void OnClosed(EventArgs e)
        {
            _trayService.Dispose();
            base.OnClosed(e);
        }

        private void IslandBorder_MouseLeave(object sender, MouseEventArgs e)
        {
            _isHovered = false;

            if (_isFloatingLyrics)
            {
                return;
            }

            if (!_isPinned && !_isEqMode)
            {
                if (_isExpanded)
                {
                    _autoHideTimer.Start();
                }
                else if (_isTrueFullScreen)
                {
                    _autoHideTimer.Start();
                }
                else if (_isLyricsMode && _hasMedia)
                {
                    _autoHideTimer.Start();
                }
                else if (_isAutoHide)
                {
                    _autoHideTimer.Start();
                }
            }
        }

        private void IslandBorder_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDuckedForTabs = false;
            _tabDuckingBufferCounter = 0;

            if (e.OriginalSource is DependencyObject dep)
            {
                // Never intercept clicks destined for interactive child components
                if (IsVisualChildOf(dep, BtnCloseFloatingLyrics) ||
                    IsVisualChildOf(dep, DockInlineList) || IsVisualChildOf(dep, BtnGridDrawer) ||
                    IsVisualChildOf(dep, TabBtnPlayer) || IsVisualChildOf(dep, TabBtnApps) || IsVisualChildOf(dep, TabBtnSystem) ||
                    IsVisualChildOf(dep, BtnPin) || IsVisualChildOf(dep, MediaContentGrid) ||
                    IsVisualChildOf(dep, DockDrawerPanel) || IsVisualChildOf(dep, SystemDashboardPanel) ||
                    IsVisualChildOf(dep, EqualizerStagePanel))
                {
                    return;
                }
            }

            if (_isFloatingLyrics)
            {
                // When floating lyrics is active, clicking on the lyrics text or floating capsule does nothing.
                // The user must explicitly click the [X] button (BtnCloseFloatingLyrics) to close floating lyrics.
                e.Handled = true;
                return;
            }

            if (e.ClickCount == 2)
            {
                TogglePin();
                e.Handled = true;
            }
            else
            {
                if (_isPeeking)
                {
                    ShowCompact();
                    e.Handled = true;
                }
                else if (!_isExpanded)
                {
                    ExpandIsland();
                    e.Handled = true;
                }
                else if (!_isEqMode)
                {
                    // Click on notch header or blank area in expanded view collapses back to compact notch
                    ShowCompact();
                    e.Handled = true;
                }
            }
        }

        private void TogglePin()
        {
            _isPinned = !_isPinned;
            MenuItemPin.IsChecked = _isPinned;
            PinIcon.Fill = _isPinned ? GetBrush("IslandAccent", Colors.DodgerBlue) : GetBrush("IslandTextSecondary", Colors.Gray);

            _settingsService.Current.IsPinned = _isPinned;
            _settingsService.Save();

            if (_isPinned)
            {
                _autoHideTimer.Stop();
                if (_isPeeking) ShowCompact();
            }
            else
            {
                if (!_isHovered && _isAutoHide)
                {
                    _autoHideTimer.Start();
                }
            }
        }

        private void BtnPin_Click(object sender, RoutedEventArgs e)
        {
            TogglePin();
        }

        private void BtnCollapse_Click(object sender, RoutedEventArgs e)
        {
            _isPinned = false;
            MenuItemPin.IsChecked = false;
            PinIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);

            _settingsService.Current.IsPinned = false;
            _settingsService.Save();

            if (_isAutoHide && !_isHovered)
            {
                HideToPeek();
            }
            else
            {
                ShowCompact();
            }
        }

        private void MenuItemAutoHide_Click(object sender, RoutedEventArgs e)
        {
            _isAutoHide = !_isAutoHide;
            _settingsService.Current.IsAutoHide = _isAutoHide;
            _settingsService.Save();
            UpdateContextMenuCheckmarks();

            if (_isAutoHide && !_isHovered && !_isPinned)
            {
                if (_isLyricsMode && _hasMedia)
                {
                    ShowFloatingLyrics();
                }
                else
                {
                    HideToPeek();
                }
            }
            else if (!_isAutoHide && _isPeeking)
            {
                if (_isLyricsMode && _hasMedia)
                {
                    ShowFloatingLyrics();
                }
                else if (!_isTrueFullScreen && !_isDuckedForTabs)
                {
                    ShowCompact();
                }
            }
        }

        private void MenuItemStartup_Click(object sender, RoutedEventArgs e)
        {
            _settingsService.SetRunAtStartup(!_settingsService.Current.RunAtStartup);
            UpdateContextMenuCheckmarks();
        }

        private void MenuItemSpeedMeter_Click(object sender, RoutedEventArgs e)
        {
            _showNetworkSpeedMeter = !_showNetworkSpeedMeter;
            _settingsService.Current.ShowNetworkSpeedMeter = _showNetworkSpeedMeter;
            _settingsService.Save();

            if (MenuItemSpeedMeter != null)
            {
                MenuItemSpeedMeter.IsChecked = _showNetworkSpeedMeter;
            }

            if (NetworkSpeedPanel != null)
            {
                NetworkSpeedPanel.Visibility = _showNetworkSpeedMeter ? Visibility.Visible : Visibility.Collapsed;
            }

            if (!_isExpanded && !_isPeeking && !_isFloatingLyrics)
            {
                double targetWidth = GetCompactTargetWidth();
                AnimateBorderWidth(targetWidth, 200);
                UpdateNotchShape(targetWidth, CompactHeight);
            }

            UpdateContextMenuCheckmarks();
        }

        private void MenuSpeedUnit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag)
            {
                _speedMeterUnit = tag;
                _settingsService.Current.SpeedMeterUnit = tag;
                _settingsService.Save();
                UpdateContextMenuCheckmarks();

                if (_showNetworkSpeedMeter && NetworkSpeedPanel != null && NetworkSpeedPanel.Visibility == Visibility.Visible)
                {
                    bool useBits = string.Equals(_speedMeterUnit, "Bits", StringComparison.OrdinalIgnoreCase);
                    _sysInfoManager.GetNetworkSpeed(out double rxBytes, out double txBytes);
                    TxtSpeedDownload.Text = SystemInfoManager.FormatNetworkSpeed(rxBytes, useBits);
                    TxtSpeedUpload.Text = SystemInfoManager.FormatNetworkSpeed(txBytes, useBits);
                }
            }
        }

        #endregion

        #region Media & Volume Buttons

        private void BtnLyrics_Click(object sender, RoutedEventArgs e)
        {
            if (_isEqMode)
            {
                _isEqMode = false;
                EqualizerStagePanel.Visibility = Visibility.Collapsed;
                EqIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);
            }

            if (_isFloatingLyrics || _isLyricsPinned)
            {
                CloseFloatingLyrics();
            }
            else
            {
                _isLyricsMode = true;
                _isLyricsPinned = true;
                MicIcon.Fill = GetBrush("IslandAccent", Colors.DodgerBlue);

                // Instantly pin and show floating lyrics notch so it never hides even when apps are maximized
                _ = LoadLyricsForCurrentTrackAsync();
                ShowFloatingLyrics();
            }
        }

        private void UpdateLyricsView()
        {
            if (_isLyricsMode)
            {
                _isEqMode = false;
                EqualizerStagePanel.Visibility = Visibility.Collapsed;
                EqIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);
                StandardMediaPanel.Visibility = Visibility.Collapsed;
                KaraokePanel.Visibility = Visibility.Visible;
                MicIcon.Fill = GetBrush("IslandAccent", Colors.DodgerBlue);

                if (ExpandedAlbumArt.Source != null)
                {
                    KaraokeAlbumArt.Source = ExpandedAlbumArt.Source;
                }
                TxtKaraokeTrackHeader.Text = $"{_mediaManager.CurrentTrack.Title} • {_mediaManager.CurrentTrack.Artist}";

                if (_isExpanded)
                {
                    AnimateBorderHeight(ExpandedHeightKaraoke, 200);
                }
                _ = LoadLyricsForCurrentTrackAsync();
            }
            else
            {
                if (_isFloatingLyrics)
                {
                    _isFloatingLyrics = false;
                    FloatingLyricsView.Visibility = Visibility.Collapsed;
                    IslandBorder.Background = Brushes.Transparent;
                    IslandBorder.BorderThickness = new Thickness(0);
                    IslandBorder.BorderBrush = Brushes.Transparent;
                    IslandBorder.Effect = null;
                    UpdateNotchShape(ExpandedWidth, ExpandedHeight);

                    if (_isAutoHide && !_isHovered)
                    {
                        HideToPeek();
                    }
                    else
                    {
                        ShowCompact();
                    }
                }

                StandardMediaPanel.Visibility = Visibility.Visible;
                KaraokePanel.Visibility = Visibility.Collapsed;
                MicIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);

                if (_isExpanded)
                {
                    AnimateBorderHeight(ExpandedHeightNormal, 200);
                }
            }
        }

        private async Task LoadLyricsForCurrentTrackAsync()
        {
            if (!_hasMedia || string.IsNullOrWhiteSpace(_mediaManager.CurrentTrack.Title))
            {
                KaraokeSyncContainer.Visibility = Visibility.Visible;
                KaraokePlainContainer.Visibility = Visibility.Collapsed;
                TxtKaraokePrev.Text = string.Empty;
                TxtKaraokeCurrent.Text = "Tidak ada lagu yang sedang diputar.";
                TxtKaraokeCurrent.Foreground = GetBrush("IslandTextSecondary", Colors.Gray);
                TxtKaraokeNext.Text = string.Empty;
                return;
            }

            string trackKey = $"{_mediaManager.CurrentTrack.Title}_{_mediaManager.CurrentTrack.Artist}";
            if (trackKey == _currentLyricsKey && _currentLyrics != null)
            {
                _lastActiveLyricIndex = -2;
                UpdateKaraokePosition();
                return;
            }

            if (ExpandedAlbumArt.Source != null)
            {
                KaraokeAlbumArt.Source = ExpandedAlbumArt.Source;
            }
            TxtKaraokeTrackHeader.Text = $"{_mediaManager.CurrentTrack.Title} • {_mediaManager.CurrentTrack.Artist}";
            TxtKaraokePrev.Text = string.Empty;
            TxtKaraokeCurrent.Text = "🎵 Mengambil lirik lagu...";
            TxtKaraokeCurrent.Foreground = GetBrush("IslandAccent", Colors.DodgerBlue);
            TxtKaraokeNext.Text = string.Empty;

            var lyrics = await _lyricsService.FetchLyricsAsync(_mediaManager.CurrentTrack.Title, _mediaManager.CurrentTrack.Artist);

            if (lyrics != null)
            {
                _currentLyrics = lyrics;
                _currentLyricsKey = trackKey;

                if (lyrics.HasSynced)
                {
                    KaraokeSyncContainer.Visibility = Visibility.Visible;
                    KaraokePlainContainer.Visibility = Visibility.Collapsed;
                    _lastActiveLyricIndex = -2;
                    UpdateKaraokePosition();
                }
                else
                {
                    KaraokeSyncContainer.Visibility = Visibility.Collapsed;
                    KaraokePlainContainer.Visibility = Visibility.Visible;
                    TxtKaraokePlain.Text = lyrics.PlainText;

                    if (_isFloatingLyrics)
                    {
                        ApplyFloatingLyric($"🎵 {_mediaManager.CurrentTrack.Title} • {_mediaManager.CurrentTrack.Artist}");
                    }
                }
            }
            else
            {
                _currentLyrics = null;
                _currentLyricsKey = string.Empty;
                KaraokeSyncContainer.Visibility = Visibility.Visible;
                KaraokePlainContainer.Visibility = Visibility.Collapsed;
                TxtKaraokePrev.Text = string.Empty;
                TxtKaraokeCurrent.Text = "Lirik tidak ditemukan untuk lagu ini.";
                TxtKaraokeCurrent.Foreground = GetBrush("IslandTextSecondary", Colors.Gray);
                TxtKaraokeNext.Text = string.Empty;

                if (_isFloatingLyrics)
                {
                    ApplyFloatingLyric($"🎵 {_mediaManager.CurrentTrack.Title} • {_mediaManager.CurrentTrack.Artist}", true);
                }
            }
        }

        private void UpdateKaraokePosition()
        {
            if (_currentLyrics == null || !_currentLyrics.HasSynced) return;

            TimeSpan currentPos = _mediaManager.GetCurrentPosition();
            var lines = _currentLyrics.SyncedLines;

            int activeIndex = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                if (lines[i].Timestamp <= currentPos)
                {
                    activeIndex = i;
                }
                else
                {
                    break;
                }
            }

            if (activeIndex == _lastActiveLyricIndex) return;
            _lastActiveLyricIndex = activeIndex;

            if (activeIndex == -1)
            {
                // Before first vocal line (Intro / Instrumental)
                TxtKaraokePrev.Text = string.Empty;
                TxtKaraokeCurrent.Text = "🎵 (Musik Instrumen...)";
                TxtKaraokeCurrent.Foreground = GetBrush("IslandTextSecondary", Colors.Gray);
                TxtKaraokeNext.Text = lines.Count > 0 ? $"Lirik pertama: {lines[0].Text}" : string.Empty;
                TxtKaraokeNext.Opacity = _lyricNextOpacity;

                if (_isFloatingLyrics)
                {
                    string firstLine = lines.Count > 0 ? lines[0].Text : string.Empty;
                    ApplyFloatingLyric(
                        $"{_mediaManager.CurrentTrack.Title} • {_mediaManager.CurrentTrack.Artist}",
                        firstLine,
                        true,
                        -1);
                }
            }
            else
            {
                TxtKaraokeCurrent.Foreground = GetBrush("IslandAccent", Colors.DodgerBlue);
                TxtKaraokePrev.Text = activeIndex > 0 ? lines[activeIndex - 1].Text : string.Empty;
                TxtKaraokeCurrent.Text = lines[activeIndex].Text;
                string nextLine = activeIndex < lines.Count - 1 ? lines[activeIndex + 1].Text : string.Empty;
                TxtKaraokeNext.Text = nextLine;
                TxtKaraokeNext.Opacity = _lyricNextOpacity;

                if (_isFloatingLyrics)
                {
                    ApplyFloatingLyric(lines[activeIndex].Text, nextLine, false, activeIndex);
                }
            }
        }

        private async void BtnPlayPause_Click(object sender, RoutedEventArgs e)
        {
            await _mediaManager.TogglePlayPauseAsync();
        }

        private async void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            await _mediaManager.SkipNextAsync();
        }

        private async void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            await _mediaManager.SkipPreviousAsync();
        }

        private void BtnMute_Click(object sender, RoutedEventArgs e)
        {
            _volumeService.ToggleMute();
            ShowRightVolumeHUD(_volumeService.Volume, _volumeService.IsMuted);
        }

        private void BtnVolDown_Click(object sender, RoutedEventArgs e)
        {
            _volumeService.ChangeVolume(-0.05f);
            ShowRightVolumeHUD(_volumeService.Volume, _volumeService.IsMuted);
        }

        private void BtnVolUp_Click(object sender, RoutedEventArgs e)
        {
            _volumeService.ChangeVolume(+0.05f);
            ShowRightVolumeHUD(_volumeService.Volume, _volumeService.IsMuted);
        }

        #region Studio Equalizer Event Handlers & Sync

        private void BtnEqToggle_Click(object sender, RoutedEventArgs e)
        {
            _isEqMode = !_isEqMode;
            if (_isEqMode)
            {
                _isLyricsMode = false;
                _autoHideTimer.Stop();
                if (!_isExpanded)
                {
                    ExpandIsland();
                }
            }
            UpdateEqualizerView();
        }

        private void MenuItemEqualizer_Click(object sender, RoutedEventArgs e)
        {
            _isEqMode = true;
            _isLyricsMode = false;
            _autoHideTimer.Stop();

            if (!_isExpanded)
            {
                ExpandIsland();
            }
            UpdateEqualizerView();
        }

        private void UpdateEqualizerView()
        {
            if (_isEqMode)
            {
                StandardMediaPanel.Visibility = Visibility.Collapsed;
                KaraokePanel.Visibility = Visibility.Collapsed;
                EqualizerStagePanel.Visibility = Visibility.Visible;
                EqIcon.Fill = GetBrush("IslandGreen", Color.FromRgb(48, 209, 88));
                MicIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);
                AnimateBorderHeight(ExpandedHeightEq, 200);
                UpdateNotchShape(ExpandedWidth, ExpandedHeightEq);
                SyncEqualizerUi();
            }
            else
            {
                EqualizerStagePanel.Visibility = Visibility.Collapsed;
                EqIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);

                if (_isLyricsMode)
                {
                    KaraokePanel.Visibility = Visibility.Visible;
                    StandardMediaPanel.Visibility = Visibility.Collapsed;
                    MicIcon.Fill = GetBrush("IslandAccent", Colors.DodgerBlue);
                    AnimateBorderHeight(ExpandedHeightKaraoke, 200);
                    UpdateNotchShape(ExpandedWidth, ExpandedHeightKaraoke);
                }
                else
                {
                    StandardMediaPanel.Visibility = Visibility.Visible;
                    KaraokePanel.Visibility = Visibility.Collapsed;
                    MicIcon.Fill = GetBrush("IslandTextSecondary", Colors.Gray);
                    AnimateBorderHeight(ExpandedHeightNormal, 200);
                    UpdateNotchShape(ExpandedWidth, ExpandedHeightNormal);
                }
            }
        }

        private void SyncEqualizerUi()
        {
            if (!IsLoaded || _isUpdatingEqSliders) return;
            if (BtnEqBypass == null || SliderEq0 == null) return;
            _isUpdatingEqSliders = true;
            try
            {
                // Re-check APO status in case user recently installed it
                _equalizer.CheckAndRefreshApoStatus();

                // Update Driver Status Badge
                if (BtnEqDriverHelp != null && TxtEqDriverStatus != null)
                {
                    if (_equalizer.IsApoInstalled)
                    {
                        BtnEqDriverHelp.Visibility = Visibility.Visible;
                        BtnEqDriverHelp.Background = new SolidColorBrush(Color.FromArgb(35, 48, 209, 88));
                        BtnEqDriverHelp.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 209, 88));
                        TxtEqDriverStatus.Text = "⚡ Aktif";
                        TxtEqDriverStatus.Foreground = GetBrush("IslandGreen", Color.FromRgb(48, 209, 88));
                        BtnEqDriverHelp.ToolTip = "Driver Equalizer APO aktif dan sinkron real-time dengan audio Windows!";
                    }
                    else
                    {
                        BtnEqDriverHelp.Visibility = Visibility.Visible;
                        BtnEqDriverHelp.Background = new SolidColorBrush(Color.FromArgb(35, 255, 159, 10));
                        BtnEqDriverHelp.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 159, 10));
                        TxtEqDriverStatus.Text = "⚠️ Driver";
                        TxtEqDriverStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 159, 10));
                        BtnEqDriverHelp.ToolTip = "Driver Windows Equalizer APO belum terdeteksi. Klik untuk bantuan / unduh driver agar efek suara aktif di semua aplikasi.";
                    }
                }

                // Sync bypass button
                if (_equalizer.IsEnabled)
                {
                    BtnEqBypass.Background = new SolidColorBrush(Color.FromArgb(40, 48, 209, 88));
                    BtnEqBypass.BorderBrush = new SolidColorBrush(Color.FromRgb(48, 209, 88));
                    TxtEqBypass.Text = "ON";
                    TxtEqBypass.Foreground = GetBrush("IslandGreen", Color.FromRgb(48, 209, 88));
                    BtnEqBypass.ToolTip = "Equalizer Aktif (Klik untuk Matikan / Bypass)";
                }
                else
                {
                    BtnEqBypass.Background = new SolidColorBrush(Color.FromArgb(35, 255, 69, 58));
                    BtnEqBypass.BorderBrush = new SolidColorBrush(Color.FromRgb(255, 69, 58));
                    TxtEqBypass.Text = "OFF";
                    TxtEqBypass.Foreground = new SolidColorBrush(Color.FromRgb(255, 69, 58));
                    BtnEqBypass.ToolTip = "Equalizer Bypass / Nonaktif (Klik untuk Aktifkan)";
                }

                // Sync preset pills active state
                Button[] pills = new Button[] { BtnPresetFlat, BtnPresetHarman, BtnPresetBass, BtnPresetVocal, BtnPresetRock, BtnPresetPop, BtnPresetEdm, BtnPresetJazz, BtnPresetClassical };
                foreach (var pill in pills)
                {
                    if (pill == null) continue;
                    string tag = pill.Tag?.ToString() ?? "";
                    bool isActive = string.Equals(tag, _equalizer.CurrentPreset, StringComparison.OrdinalIgnoreCase) ||
                                    (tag == "Harman Target" && _equalizer.CurrentPreset == "Harman") ||
                                    (tag == "Bass Boost" && _equalizer.CurrentPreset == "Bass") ||
                                    (tag == "Vocal Clarity" && (_equalizer.CurrentPreset == "Vocal" || _equalizer.CurrentPreset == "Podcast / Voice")) ||
                                    (tag == "Rock / V-Shape" && _equalizer.CurrentPreset == "Rock") ||
                                    (tag == "Pop / Modern" && _equalizer.CurrentPreset == "Pop") ||
                                    (tag == "Electronic / EDM" && _equalizer.CurrentPreset == "EDM") ||
                                    (tag == "Jazz / Acoustic" && _equalizer.CurrentPreset == "Jazz") ||
                                    (tag == "Classical / Hall" && _equalizer.CurrentPreset == "Classical");

                    if (isActive)
                    {
                        pill.Background = new SolidColorBrush(Color.FromArgb(60, 48, 209, 88));
                        pill.Foreground = GetBrush("IslandGreen", Color.FromRgb(48, 209, 88));
                    }
                    else
                    {
                        pill.Background = new SolidColorBrush(Color.FromRgb(28, 28, 30));
                        pill.Foreground = GetBrush("IslandTextSecondary", Color.FromRgb(142, 142, 147));
                    }
                }

                // Sync sliders & dB labels
                Slider[] sliders = new Slider[] { SliderEq0, SliderEq1, SliderEq2, SliderEq3, SliderEq4, SliderEq5, SliderEq6, SliderEq7, SliderEq8, SliderEq9 };
                TextBlock[] dbTexts = new TextBlock[] { TxtEqDb0, TxtEqDb1, TxtEqDb2, TxtEqDb3, TxtEqDb4, TxtEqDb5, TxtEqDb6, TxtEqDb7, TxtEqDb8, TxtEqDb9 };

                for (int i = 0; i < EqualizerService.BandCount; i++)
                {
                    float val = _equalizer.BandGains[i];
                    sliders[i].Value = val;
                    dbTexts[i].Text = FormatDb(val);
                    dbTexts[i].Foreground = Math.Abs(val) > 0.05f 
                        ? GetBrush("IslandGreen", Color.FromRgb(48, 209, 88))
                        : GetBrush("IslandTextSecondary", Color.FromRgb(142, 142, 147));
                }

                SliderEqPreamp.Value = _equalizer.Preamp;
                TxtEqDbPreamp.Text = FormatDb(_equalizer.Preamp);

                if (MenuItemEqEnabled != null)
                {
                    MenuItemEqEnabled.IsChecked = _equalizer.IsEnabled;
                    MenuItemEqEnabled.Header = FormatMenuHeader("⚡ Equalizer Aktif (ON)", _equalizer.IsEnabled);
                }
            }
            finally
            {
                _isUpdatingEqSliders = false;
            }
        }

        private void BtnEqDriverHelp_Click(object sender, RoutedEventArgs e)
        {
            _equalizer.CheckAndRefreshApoStatus();

            if (_equalizer.IsApoInstalled)
            {
                MessageBox.Show(
                    "Driver Equalizer APO sudah aktif dan terhubung!\n\nSetiap perubahan fader equalizer di Aia Dinamic langsung berefek ke seluruh suara Windows (Spotify, YouTube, Game, dsb).",
                    "Aia Dinamic - Studio Equalizer Engine",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                var result = MessageBox.Show(
                    "Agar Equalizer dapat mengubah suara global di Windows (Spotify, YouTube, Game, dll) secara native dengan latensi 0ms, Windows memerlukan driver audio 'Equalizer APO'.\n\n" +
                    "Paket instalasi driver sudah disiapkan langsung di Aia Dinamic!\n\n" +
                    "Langkah mudah:\n" +
                    "1. Klik 'Ya' untuk langsung menjalankan instalasi driver.\n" +
                    "2. Centang perangkat speaker / headphone Anda lalu klik OK.\n" +
                    "3. Equalizer Aia Dinamic akan langsung aktif berefek ke semua aplikasi!\n\n" +
                    "Jalankan instalasi driver sekarang?",
                    "Aia Dinamic - Pasang Driver Equalizer",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    _equalizer.LaunchApoInstallerOrDownload();
                }
            }
        }

        private static string FormatDb(double val)
        {
            if (Math.Abs(val) < 0.2) return "0";
            return val > 0 ? $"+{val:0.#}" : $"{val:0.#}";
        }

        private void PresetScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer sv)
            {
                if (e.Delta < 0)
                    sv.LineRight();
                else
                    sv.LineLeft();
                e.Handled = true;
            }
        }

        private void MenuPreset_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item && item.Tag is string presetName)
            {
                _equalizer.ApplyPreset(presetName);
                SyncEqualizerUi();
            }
        }

        private void PresetPill_Click(object sender, RoutedEventArgs e)
        {
            if (!IsLoaded || _isUpdatingEqSliders) return;
            if (sender is Button btn && btn.Tag is string presetName)
            {
                _equalizer.ApplyPreset(presetName);
                SyncEqualizerUi();
            }
        }

        private void SliderEq_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _isUpdatingEqSliders) return;
            if (BtnEqBypass == null || SliderEq0 == null) return;
            if (sender is Slider slider && int.TryParse(slider.Tag?.ToString(), out int idx))
            {
                _equalizer.SetBandGain(idx, (float)e.NewValue);

                TextBlock[] dbTexts = new TextBlock[] { TxtEqDb0, TxtEqDb1, TxtEqDb2, TxtEqDb3, TxtEqDb4, TxtEqDb5, TxtEqDb6, TxtEqDb7, TxtEqDb8, TxtEqDb9 };
                if (idx >= 0 && idx < dbTexts.Length)
                {
                    dbTexts[idx].Text = FormatDb(e.NewValue);
                    dbTexts[idx].Foreground = Math.Abs(e.NewValue) > 0.05 
                        ? GetBrush("IslandGreen", Color.FromRgb(48, 209, 88))
                        : GetBrush("IslandTextSecondary", Color.FromRgb(142, 142, 147));
                }

                // Clear active pill highlight since user manually tweaked
                Button[] pills = new Button[] { BtnPresetFlat, BtnPresetHarman, BtnPresetBass, BtnPresetVocal, BtnPresetRock, BtnPresetPop, BtnPresetEdm, BtnPresetJazz, BtnPresetClassical };
                foreach (var pill in pills)
                {
                    if (pill == null) continue;
                    pill.Background = new SolidColorBrush(Color.FromRgb(28, 28, 30));
                    pill.Foreground = GetBrush("IslandTextSecondary", Color.FromRgb(142, 142, 147));
                }
            }
        }

        private void SliderEqPreamp_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (!IsLoaded || _isUpdatingEqSliders) return;
            if (BtnEqBypass == null || SliderEq0 == null) return;
            _equalizer.SetPreamp((float)e.NewValue);
            TxtEqDbPreamp.Text = FormatDb(e.NewValue);
        }

        private void BtnEqBypass_Click(object sender, RoutedEventArgs e)
        {
            _equalizer.SetEnabled(!_equalizer.IsEnabled);
            SyncEqualizerUi();
            UpdateContextMenuCheckmarks();
        }

        private void MenuItemEqToggle_Click(object sender, RoutedEventArgs e)
        {
            _equalizer.SetEnabled(!_equalizer.IsEnabled);
            SyncEqualizerUi();
            UpdateContextMenuCheckmarks();
        }

        #endregion

        private async void TxtKaraokePrev_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentLyrics != null && _currentLyrics.HasSynced && _lastActiveLyricIndex > 0)
            {
                var target = _currentLyrics.SyncedLines[_lastActiveLyricIndex - 1].Timestamp;
                await _mediaManager.SeekToAsync(target);
            }
        }

        private async void TxtKaraokeCurrent_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentLyrics != null && _currentLyrics.HasSynced && _lastActiveLyricIndex >= 0 && _lastActiveLyricIndex < _currentLyrics.SyncedLines.Count)
            {
                var target = _currentLyrics.SyncedLines[_lastActiveLyricIndex].Timestamp;
                await _mediaManager.SeekToAsync(target);
            }
        }

        private async void TxtKaraokeNext_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_currentLyrics != null && _currentLyrics.HasSynced && _lastActiveLyricIndex + 1 < _currentLyrics.SyncedLines.Count)
            {
                var target = _currentLyrics.SyncedLines[_lastActiveLyricIndex + 1].Timestamp;
                await _mediaManager.SeekToAsync(target);
            }
        }

        private void MenuItemPin_Click(object sender, RoutedEventArgs e)
        {
            TogglePin();
            UpdateContextMenuCheckmarks();
        }

        private async void MenuItemRefresh_Click(object sender, RoutedEventArgs e)
        {
            await _mediaManager.RefreshMediaInfoAsync();
        }

        private void MenuItemExit_Click(object sender, RoutedEventArgs e)
        {
            _trayService.Dispose();
            _audioSpectrum.Dispose();
            Application.Current.Shutdown();
        }

        #endregion

        #region Multi-Monitor Support

        private static string FormatMenuHeader(string title, bool isChecked)
        {
            if (string.IsNullOrEmpty(title)) return title;
            if (title.StartsWith("✓ ")) title = title.Substring(2);
            else if (title.StartsWith("   ")) title = title.Substring(3);
            else if (title.StartsWith("  ")) title = title.Substring(2);

            return title.Trim();
        }

        private void UpdateContextMenuCheckmarks()
        {
            // 1. Pin
            if (MenuItemPin != null)
            {
                MenuItemPin.IsChecked = _isPinned;
                MenuItemPin.Header = FormatMenuHeader("📌 Pin / Pertahankan Terbuka", _isPinned);
            }

            // 2. Auto-Hide
            if (MenuItemAutoHide != null)
            {
                MenuItemAutoHide.IsChecked = _isAutoHide;
                MenuItemAutoHide.Header = FormatMenuHeader("⚡ Sembunyi Otomatis (Auto-Hide)", _isAutoHide);
            }

            // 2b. Network Speed Meter (TX / RX)
            if (MenuItemSpeedMeter != null)
            {
                MenuItemSpeedMeter.IsChecked = _showNetworkSpeedMeter;
                MenuItemSpeedMeter.Header = FormatMenuHeader("⚡ Tampilkan Speed Meter", _showNetworkSpeedMeter);
            }
            bool isBits = string.Equals(_speedMeterUnit, "Bits", StringComparison.OrdinalIgnoreCase);
            if (MenuSpeedUnitBytes != null)
            {
                MenuSpeedUnitBytes.IsChecked = !isBits;
                MenuSpeedUnitBytes.Header = FormatMenuHeader("📦 Satuan: Byte/s (KB/s, MB/s)", !isBits);
            }
            if (MenuSpeedUnitBits != null)
            {
                MenuSpeedUnitBits.IsChecked = isBits;
                MenuSpeedUnitBits.Header = FormatMenuHeader("🌐 Satuan: bit/s (kbps, Mbps)", isBits);
            }

            // 3. Startup
            if (MenuItemStartup != null)
            {
                bool startup = _settingsService.Current.RunAtStartup;
                MenuItemStartup.IsChecked = startup;
                MenuItemStartup.Header = FormatMenuHeader("🚀 Jalankan saat Windows Mulai", startup);
            }

            // 4. Dock Mode
            string currentDock = _settingsService.Current.DockMode;
            if (MenuDockMode != null)
            {
                foreach (var obj in MenuDockMode.Items)
                {
                    if (obj is MenuItem mi && mi.Tag is string tag)
                    {
                        bool active = string.Equals(tag, currentDock, StringComparison.OrdinalIgnoreCase);
                        mi.IsChecked = active;
                        string cleanTitle = tag switch
                        {
                            "Inline" => "🏷️ Model 1: Sejajar di Kapsul",
                            "GridButton" => "🪟 Model 2: Tombol Grid Minimalis (⊞)",
                            "Hidden" => "🚫 Sembunyikan Dock",
                            _ => mi.Header.ToString() ?? tag
                        };
                        mi.Header = FormatMenuHeader(cleanTitle, active);
                    }
                }
            }

            // 5. Monitor
            RefreshMonitorMenu();

            // 6. Spectrum Settings
            UpdateSpectrumMenuCheckmarks();

            // 7. Lyrics Presentation Style
            if (MenuItemLyricStyleNotch != null) MenuItemLyricStyleNotch.IsChecked = _lyricStyleNotch;
            if (MenuItemLyricStylePlain != null) MenuItemLyricStylePlain.IsChecked = !_lyricStyleNotch;
            if (MenuItemLyricTwoLines != null) MenuItemLyricTwoLines.IsChecked = _lyricShowNextPreview;
            if (MenuItemLyricSingleLine != null) MenuItemLyricSingleLine.IsChecked = !_lyricShowNextPreview;

            double curVal = GetLyricCurrentFontSize();
            if (MenuLyricSize12 != null) MenuLyricSize12.IsChecked = Math.Abs(curVal - 12.0) < 1.0;
            if (MenuLyricSize16 != null) MenuLyricSize16.IsChecked = Math.Abs(curVal - 16.0) < 1.0;
            if (MenuLyricSize20 != null) MenuLyricSize20.IsChecked = Math.Abs(curVal - 20.0) < 1.0;
            if (MenuLyricSize28 != null) MenuLyricSize28.IsChecked = Math.Abs(curVal - 28.0) < 1.0;
            if (MenuLyricSize38 != null) MenuLyricSize38.IsChecked = Math.Abs(curVal - 38.0) < 1.0;
            if (MenuLyricSize52 != null) MenuLyricSize52.IsChecked = Math.Abs(curVal - 52.0) < 1.0;
            if (MenuLyricSize72 != null) MenuLyricSize72.IsChecked = Math.Abs(curVal - 72.0) < 1.0;

            if (MenuLyricOpacity35 != null) MenuLyricOpacity35.IsChecked = Math.Abs(_lyricNextOpacity - 0.35) < 0.05;
            if (MenuLyricOpacity50 != null) MenuLyricOpacity50.IsChecked = Math.Abs(_lyricNextOpacity - 0.50) < 0.05;
            if (MenuLyricOpacity70 != null) MenuLyricOpacity70.IsChecked = Math.Abs(_lyricNextOpacity - 0.70) < 0.05;
            if (MenuLyricOpacity100 != null) MenuLyricOpacity100.IsChecked = Math.Abs(_lyricNextOpacity - 1.00) < 0.05;
        }

        private void MenuItemLyricStyleNotch_Click(object sender, RoutedEventArgs e)
        {
            _lyricStyleNotch = true;
            _settingsService.Current.LyricStyleNotch = true;
            _settingsService.Save();
            UpdateContextMenuCheckmarks();
            if (_isFloatingLyrics)
            {
                UpdateNotchShape(IslandBorder.Width, IslandBorder.Height);
                UpdateFloatingLyricsText();
            }
        }

        private void MenuItemLyricStylePlain_Click(object sender, RoutedEventArgs e)
        {
            _lyricStyleNotch = false;
            _settingsService.Current.LyricStyleNotch = false;
            _settingsService.Save();
            UpdateContextMenuCheckmarks();
            if (_isFloatingLyrics)
            {
                NotchShapePath.Visibility = Visibility.Collapsed;
                UpdateFloatingLyricsText();
            }
        }

        private void MenuItemLyricTwoLines_Click(object sender, RoutedEventArgs e)
        {
            _lyricShowNextPreview = true;
            _settingsService.Current.LyricShowNextPreview = true;
            _settingsService.Save();
            UpdateContextMenuCheckmarks();
            if (_isFloatingLyrics)
            {
                UpdateFloatingLyricsText();
            }
        }

        private void MenuItemLyricSingleLine_Click(object sender, RoutedEventArgs e)
        {
            _lyricShowNextPreview = false;
            _settingsService.Current.LyricShowNextPreview = false;
            _settingsService.Save();
            UpdateContextMenuCheckmarks();
            if (_isFloatingLyrics)
            {
                UpdateFloatingLyricsText();
            }
        }

        private void ApplyLyricNextOpacity(double opacity, bool save = true)
        {
            _lyricNextOpacity = Math.Clamp(opacity, 0.15, 1.0);
            if (save)
            {
                _settingsService.Current.LyricNextOpacity = _lyricNextOpacity;
                _settingsService.Save();
            }

            if (TxtFloatingLyricNext != null)
            {
                TxtFloatingLyricNext.Opacity = _lyricNextOpacity;
            }

            if (TxtKaraokeNext != null)
            {
                TxtKaraokeNext.Opacity = _lyricNextOpacity;
            }

            UpdateContextMenuCheckmarks();

            if (_isFloatingLyrics)
            {
                UpdateFloatingLyricsText();
            }
        }

        private void MenuItemLyricOpacity_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tagStr && double.TryParse(tagStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double op))
            {
                ApplyLyricNextOpacity(op, true);
            }
        }

        private double GetLyricCurrentFontSize()
        {
            if (double.TryParse(_lyricFontSize, out double s)) return Math.Clamp(s, 10.0, 72.0);
            return _lyricFontSize switch
            {
                "Small" => 12.0,
                "Medium" => 16.0,
                "Large" => 28.0,
                "ExtraLarge" => 38.0,
                "Jumbo" => 52.0,
                "Max" or "72" => 72.0,
                _ => 16.0
            };
        }

        private double GetLyricNextFontSize()
        {
            double cur = GetLyricCurrentFontSize();
            if (cur <= 14) return cur * 0.82;
            if (cur <= 24) return cur * 0.70;
            if (cur <= 40) return cur * 0.60;
            return cur * 0.52;
        }

        private double GetLyricSingleHeight(double? customCurSize = null)
        {
            double cur = customCurSize ?? GetLyricCurrentFontSize();
            return Math.Max(38.0, cur * 1.45 + 20.0);
        }

        private double GetLyricTwoLinesHeight(double? customCurSize = null, double? customNextSize = null)
        {
            double cur = customCurSize ?? GetLyricCurrentFontSize();
            double next = customNextSize ?? GetLyricNextFontSize();
            return Math.Max(48.0, cur * 1.30 + next * 1.25 + 24.0);
        }

        private void ApplyLyricFontSize(string size, bool save = true)
        {
            _lyricFontSize = size;
            if (save)
            {
                _settingsService.Current.LyricFontSize = size;
                _settingsService.Save();
            }

            double curSize = GetLyricCurrentFontSize();
            double nextSize = GetLyricNextFontSize();

            if (TxtFloatingLyricCurrent != null)
            {
                TxtFloatingLyricCurrent.FontSize = curSize;
                double blur = Math.Clamp(curSize * 0.45, 6.0, 24.0);
                if (TxtFloatingLyricCurrent.Effect is DropShadowEffect shadow)
                {
                    shadow.BlurRadius = blur;
                }
            }

            if (TxtFloatingLyricNext != null)
            {
                TxtFloatingLyricNext.FontSize = nextSize;
            }

            if (TxtKaraokeCurrent != null)
            {
                TxtKaraokeCurrent.FontSize = Math.Clamp(curSize * 0.85, 13.0, 24.0);
            }
            if (TxtKaraokePrev != null)
            {
                TxtKaraokePrev.FontSize = Math.Clamp(curSize * 0.60, 9.5, 14.0);
            }
            if (TxtKaraokeNext != null)
            {
                TxtKaraokeNext.FontSize = Math.Clamp(curSize * 0.60, 9.5, 14.0);
            }

            UpdateContextMenuCheckmarks();

            _lastActiveLyricIndex = -2;
            if (_isFloatingLyrics)
            {
                UpdateFloatingLyricsText();
            }
        }

        private void MenuLyricSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && mi.Tag is string tag)
            {
                ApplyLyricFontSize(tag, true);
            }
        }

        private void BtnCloseFloatingLyrics_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            CloseFloatingLyrics();
        }

        private void UpdateSpectrumMenuCheckmarks()
        {
            string theme = _settingsService.Current.SpectrumTheme;
            float sens = _settingsService.Current.AudioSensitivity;

            if (MenuThemeNeoGreen != null)
            {
                bool isSel = (theme == "NeoGreen");
                MenuThemeNeoGreen.IsChecked = isSel;
                MenuThemeNeoGreen.Header = FormatMenuHeader("🟢 Neo Green (Default)", isSel);
            }
            if (MenuThemeRainbow != null)
            {
                bool isSel = (theme == "Rainbow");
                MenuThemeRainbow.IsChecked = isSel;
                MenuThemeRainbow.Header = FormatMenuHeader("🌈 Rainbow Spectrum", isSel);
            }
            if (MenuThemeCyberBlue != null)
            {
                bool isSel = (theme == "CyberCyan" || theme == "CyberBlue");
                MenuThemeCyberBlue.IsChecked = isSel;
                MenuThemeCyberBlue.Header = FormatMenuHeader("🔵 Cyber Cyan", isSel);
            }
            if (MenuThemeTokyoNeon != null)
            {
                bool isSel = (theme == "TokyoNeon");
                MenuThemeTokyoNeon.IsChecked = isSel;
                MenuThemeTokyoNeon.Header = FormatMenuHeader("💜 Tokyo Neon", isSel);
            }
            if (MenuThemeCrimsonFire != null)
            {
                bool isSel = (theme == "CrimsonFire");
                MenuThemeCrimsonFire.IsChecked = isSel;
                MenuThemeCrimsonFire.Header = FormatMenuHeader("🔥 Crimson Red", isSel);
            }
            if (MenuThemeDeepOcean != null)
            {
                bool isSel = (theme == "DeepOcean");
                MenuThemeDeepOcean.IsChecked = isSel;
                MenuThemeDeepOcean.Header = FormatMenuHeader("🌊 Deep Ocean", isSel);
            }
            if (MenuThemeChampagneGold != null)
            {
                bool isSel = (theme == "ChampagneGold");
                MenuThemeChampagneGold.IsChecked = isSel;
                MenuThemeChampagneGold.Header = FormatMenuHeader("✨ Champagne Gold", isSel);
            }
            if (MenuThemeSakuraPink != null)
            {
                bool isSel = (theme == "SakuraPink");
                MenuThemeSakuraPink.IsChecked = isSel;
                MenuThemeSakuraPink.Header = FormatMenuHeader("🌸 Sakura Pink", isSel);
            }
            if (MenuThemeWhite != null)
            {
                bool isSel = (theme == "White");
                MenuThemeWhite.IsChecked = isSel;
                MenuThemeWhite.Header = FormatMenuHeader("⚪ Minimalist White", isSel);
            }
            if (MenuThemeSunset != null)
            {
                bool isSel = (theme == "Sunset");
                MenuThemeSunset.IsChecked = isSel;
                MenuThemeSunset.Header = FormatMenuHeader("🟠 Sunset Amber", isSel);
            }

            if (MenuModelBottomUp != null)
            {
                bool isSel = (_spectrumModel == "BottomUp");
                MenuModelBottomUp.IsChecked = isSel;
                MenuModelBottomUp.Header = FormatMenuHeader("📊 Model 1: Batang Bawah (Bottom-Up) [Klasik]", isSel);
            }
            if (MenuModelCenterMirrored != null)
            {
                bool isSel = (_spectrumModel == "CenterMirrored");
                MenuModelCenterMirrored.IsChecked = isSel;
                MenuModelCenterMirrored.Header = FormatMenuHeader("📱 Model 2: Simetris Tengah (Center-Mirrored) [Dynamic Island]", isSel);
            }
            if (MenuModelTopDown != null)
            {
                bool isSel = (_spectrumModel == "TopDown");
                MenuModelTopDown.IsChecked = isSel;
                MenuModelTopDown.Header = FormatMenuHeader("🔻 Model 3: Gantung Bezel (Top-Down) [Stalaktit]", isSel);
            }
            if (MenuModelNeedles != null)
            {
                bool isSel = (_spectrumModel == "Needles");
                MenuModelNeedles.IsChecked = isSel;
                MenuModelNeedles.Header = FormatMenuHeader("⚡ Model 4: Jarum Minimalis (Cyber Needles)", isSel);
            }
            if (MenuModelBouncingBeads != null)
            {
                bool isSel = (_spectrumModel == "BouncingBeads");
                MenuModelBouncingBeads.IsChecked = isSel;
                MenuModelBouncingBeads.Header = FormatMenuHeader("🫧 Model 5: Butiran Melompat (Bouncing Beads)", isSel);
            }
            if (MenuModelSegmentedVU != null)
            {
                bool isSel = (_spectrumModel == "SegmentedVU");
                MenuModelSegmentedVU.IsChecked = isSel;
                MenuModelSegmentedVU.Header = FormatMenuHeader("🪟 Model 6: Blok Bertingkat (Retro Digital VU)", isSel);
            }
            if (MenuModelChunkyBars != null)
            {
                bool isSel = (_spectrumModel == "ChunkyBars");
                MenuModelChunkyBars.IsChecked = isSel;
                MenuModelChunkyBars.Header = FormatMenuHeader("🧱 Model 7: Batang Tebal DJ Studio (Chunky Club Bars)", isSel);
            }

            if (MenuSens10 != null)
            {
                bool isSel = Math.Abs(sens - 1.0f) < 0.05f;
                MenuSens10.IsChecked = isSel;
                MenuSens10.Header = FormatMenuHeader("Normal (1.0x)", isSel);
            }
            if (MenuSens15 != null)
            {
                bool isSel = Math.Abs(sens - 1.5f) < 0.05f;
                MenuSens15.IsChecked = isSel;
                MenuSens15.Header = FormatMenuHeader("High (1.5x)", isSel);
            }
            if (MenuSens20 != null)
            {
                bool isSel = Math.Abs(sens - 2.0f) < 0.05f;
                MenuSens20.IsChecked = isSel;
                MenuSens20.Header = FormatMenuHeader("Ultra Boost (2.0x)", isSel);
            }

            if (MenuAudioFormatInfo != null)
            {
                MenuAudioFormatInfo.Header = $"ℹ️ Format: {_audioSpectrum.AudioFormatInfo}";
            }

            if (MenuItemEqEnabled != null)
            {
                MenuItemEqEnabled.IsChecked = _equalizer.IsEnabled;
                MenuItemEqEnabled.Header = FormatMenuHeader("⚡ Equalizer Aktif (ON)", _equalizer.IsEnabled);
            }

            if (MenuEqPresets != null)
            {
                foreach (var item in MenuEqPresets.Items)
                {
                    if (item is MenuItem mi && mi.Tag is string tagStr)
                    {
                        bool isSel = string.Equals(tagStr, _equalizer.CurrentPreset, StringComparison.OrdinalIgnoreCase) ||
                                     (tagStr == "Harman Target" && _equalizer.CurrentPreset == "Harman") ||
                                     (tagStr == "Bass Boost" && _equalizer.CurrentPreset == "Bass") ||
                                     (tagStr == "Vocal Clarity" && (_equalizer.CurrentPreset == "Vocal" || _equalizer.CurrentPreset == "Podcast / Voice")) ||
                                     (tagStr == "Rock / V-Shape" && _equalizer.CurrentPreset == "Rock") ||
                                     (tagStr == "Pop / Modern" && _equalizer.CurrentPreset == "Pop") ||
                                     (tagStr == "Electronic / EDM" && _equalizer.CurrentPreset == "EDM") ||
                                     (tagStr == "Jazz / Acoustic" && _equalizer.CurrentPreset == "Jazz") ||
                                     (tagStr == "Classical / Hall" && _equalizer.CurrentPreset == "Classical");
                        mi.IsChecked = isSel;
                    }
                }
            }
        }

        private void ApplyMonitorPosition(int monitorIndex)
        {
            var mon = MonitorService.GetMonitorByIndex(monitorIndex);
            _cachedMonitor = mon;
            _currentMonitorIndex = mon.Index;
            MonitorService.DockWindowToMonitor(this, mon);
            CheckWindowObstructionState();
        }

        private void MonitorFollowTimer_Tick(object? sender, EventArgs e)
        {
            if (_settingsService.Current.FollowCursorMonitor && !_isSettingsOpen && !_isExpanded)
            {
                if (MonitorService.GetCursorPos(out var pt))
                {
                    var targetMon = MonitorService.GetMonitorFromPoint(pt.X, pt.Y);
                    if (targetMon != null && targetMon.Index != _currentMonitorIndex)
                    {
                        // Trigger relocation when cursor approaches the top bezel of that monitor
                        if (pt.Y >= targetMon.Top && pt.Y <= targetMon.Top + 55 &&
                            pt.X >= targetMon.Left && pt.X <= targetMon.Left + targetMon.Width)
                        {
                            ApplyMonitorPosition(targetMon.Index);
                            RefreshMonitorMenu();
                        }
                    }
                }
            }

            CheckWindowObstructionState();
        }

        private void RefreshMonitorMenu()
        {
            if (MenuMonitorSelect == null) return;
            MenuMonitorSelect.Items.Clear();

            var monitors = MonitorService.GetAllMonitors();
            bool follow = _settingsService.Current.FollowCursorMonitor;

            foreach (var mon in monitors)
            {
                bool isSel = !follow && (_currentMonitorIndex == mon.Index);
                var item = new MenuItem
                {
                    Header = FormatMenuHeader(mon.DisplayName, isSel),
                    IsCheckable = true,
                    IsChecked = isSel,
                    Tag = mon.Index
                };
                item.Click += (s, e) =>
                {
                    _settingsService.Current.FollowCursorMonitor = false;
                    _settingsService.Current.TargetMonitorIndex = mon.Index;
                    _settingsService.Save();
                    ApplyMonitorPosition(mon.Index);
                    UpdateContextMenuCheckmarks();
                };
                MenuMonitorSelect.Items.Add(item);
            }

            MenuMonitorSelect.Items.Add(new Separator());

            var itemFollow = new MenuItem
            {
                Header = FormatMenuHeader("🖱️ Ikuti Kursor Mouse (Dynamic Follow)", follow),
                IsCheckable = true,
                IsChecked = follow
            };
            itemFollow.Click += (s, e) =>
            {
                _settingsService.Current.FollowCursorMonitor = true;
                _settingsService.Save();
                UpdateContextMenuCheckmarks();
            };
            MenuMonitorSelect.Items.Add(itemFollow);
        }

        #endregion

        #region Spectrum Settings & Themes



        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            _isSettingsOpen = true;
            _autoHideTimer.Stop();
            UpdateContextMenuCheckmarks();
        }

        private void ContextMenu_Closed(object sender, RoutedEventArgs e)
        {
            _isSettingsOpen = false;
            if (!IslandBorder.IsMouseOver && !_isPinned && !_isLyricsPinned && !_isFloatingLyrics && !_isEqMode)
            {
                if (_isAutoHide || (_isLyricsMode && _hasMedia))
                {
                    _autoHideTimer.Start();
                }
            }
        }

        private void SetSpectrumColors(SolidColorBrush[] brushes)
        {
            _currentThemeBrushes = brushes;
            if (brushes.Length >= 20)
            {
                ExpBar1.Fill = brushes[0];
                ExpBar2.Fill = brushes[1];
                ExpBar3.Fill = brushes[2];
                ExpBar4.Fill = brushes[3];
                ExpBar5.Fill = brushes[4];
                ExpBar6.Fill = brushes[5];
                ExpBar7.Fill = brushes[6];
                ExpBar8.Fill = brushes[7];
                ExpBar9.Fill = brushes[8];
                ExpBar10.Fill = brushes[9];
                ExpBar11.Fill = brushes[10];
                ExpBar12.Fill = brushes[11];
                ExpBar13.Fill = brushes[12];
                ExpBar14.Fill = brushes[13];
                ExpBar15.Fill = brushes[14];
                ExpBar16.Fill = brushes[15];
                ExpBar17.Fill = brushes[16];
                ExpBar18.Fill = brushes[17];
                ExpBar19.Fill = brushes[18];
                ExpBar20.Fill = brushes[19];
            }
            if (brushes.Length >= 4)
            {
                EqBar1.Fill = brushes[0];
                EqBar2.Fill = brushes[1];
                EqBar3.Fill = brushes[2];
                EqBar4.Fill = brushes[3];
            }
        }

        private void ApplySpectrumTheme(string theme, bool save = true)
        {
            if (save)
            {
                _settingsService.Current.SpectrumTheme = theme;
                _settingsService.Save();
            }
            UpdateSpectrumMenuCheckmarks();

            if (MenuThemeNeoGreen != null) MenuThemeNeoGreen.IsChecked = (theme == "NeoGreen");
            if (MenuThemeRainbow != null) MenuThemeRainbow.IsChecked = (theme == "Rainbow");
            if (MenuThemeCyberBlue != null) MenuThemeCyberBlue.IsChecked = (theme == "CyberCyan" || theme == "CyberBlue");
            if (MenuThemeTokyoNeon != null) MenuThemeTokyoNeon.IsChecked = (theme == "TokyoNeon");
            if (MenuThemeCrimsonFire != null) MenuThemeCrimsonFire.IsChecked = (theme == "CrimsonFire");
            if (MenuThemeDeepOcean != null) MenuThemeDeepOcean.IsChecked = (theme == "DeepOcean");
            if (MenuThemeChampagneGold != null) MenuThemeChampagneGold.IsChecked = (theme == "ChampagneGold");
            if (MenuThemeSakuraPink != null) MenuThemeSakuraPink.IsChecked = (theme == "SakuraPink");
            if (MenuThemeWhite != null) MenuThemeWhite.IsChecked = (theme == "White");
            if (MenuThemeSunset != null) MenuThemeSunset.IsChecked = (theme == "Sunset");

            switch (theme)
            {
                case "Rainbow":
                    var rainbow = new SolidColorBrush[]
                    {
                        new(Color.FromRgb(255, 45, 85)),   // Rose Pink
                        new(Color.FromRgb(255, 59, 48)),   // Red
                        new(Color.FromRgb(255, 90, 20)),   // Red-Orange
                        new(Color.FromRgb(255, 130, 0)),   // Orange
                        new(Color.FromRgb(255, 175, 0)),   // Amber
                        new(Color.FromRgb(255, 214, 10)),  // Gold
                        new(Color.FromRgb(210, 225, 0)),   // Yellow-Green
                        new(Color.FromRgb(140, 220, 30)),  // Lime
                        new(Color.FromRgb(52, 199, 89)),   // Neo Green 1
                        new(Color.FromRgb(30, 200, 140)),  // Mint
                        new(Color.FromRgb(0, 199, 190)),   // Aqua
                        new(Color.FromRgb(0, 180, 225)),   // Cyan
                        new(Color.FromRgb(0, 155, 245)),   // Sky Blue
                        new(Color.FromRgb(10, 132, 255)),  // Royal Blue
                        new(Color.FromRgb(50, 115, 250)),  // Cobalt
                        new(Color.FromRgb(94, 92, 230)),   // Indigo
                        new(Color.FromRgb(130, 85, 230)),  // Violet
                        new(Color.FromRgb(175, 82, 222)),  // Purple
                        new(Color.FromRgb(215, 60, 175)),  // Orchid
                        new(Color.FromRgb(240, 50, 130))   // Magenta
                    };
                    SetSpectrumColors(rainbow);
                    break;

                case "TokyoNeon":
                    var tokyoColors = new SolidColorBrush[20];
                    for (int i = 0; i < 20; i++)
                    {
                        double t = i / 19.0;
                        byte r = (byte)(191 + (64 * t));
                        byte g = (byte)(90 - (45 * t));
                        byte b = (byte)(242 - (115 * t));
                        tokyoColors[i] = new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                    SetSpectrumColors(tokyoColors);
                    break;

                case "CrimsonFire":
                    var fireColors = new SolidColorBrush[20];
                    for (int i = 0; i < 20; i++)
                    {
                        double t = i / 19.0;
                        byte r = 255;
                        byte g = (byte)(25 + (124 * t));
                        byte b = (byte)(20 - (20 * t));
                        fireColors[i] = new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                    SetSpectrumColors(fireColors);
                    break;

                case "DeepOcean":
                    var oceanColors = new SolidColorBrush[20];
                    for (int i = 0; i < 20; i++)
                    {
                        double t = i / 19.0;
                        byte r = 0;
                        byte g = (byte)(64 + (165 * t));
                        byte b = (byte)(221 + (34 * t));
                        oceanColors[i] = new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                    SetSpectrumColors(oceanColors);
                    break;

                case "ChampagneGold":
                    var goldColors = new SolidColorBrush[20];
                    for (int i = 0; i < 20; i++)
                    {
                        double t = i / 19.0;
                        byte r = (byte)(212 + (43 * t));
                        byte g = (byte)(175 + (65 * t));
                        byte b = (byte)(55 + (124 * t));
                        goldColors[i] = new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                    SetSpectrumColors(goldColors);
                    break;

                case "SakuraPink":
                    var sakuraColors = new SolidColorBrush[20];
                    for (int i = 0; i < 20; i++)
                    {
                        double t = i / 19.0;
                        byte r = (byte)(255 - (31 * t));
                        byte g = (byte)(166 - (80 * t));
                        byte b = (byte)(201 + (52 * t));
                        sakuraColors[i] = new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                    SetSpectrumColors(sakuraColors);
                    break;

                case "CyberCyan":
                case "CyberBlue":
                    var cyan = new SolidColorBrush(Color.FromRgb(0, 235, 255));
                    var blue = GetBrush("IslandAccent", Color.FromRgb(10, 132, 255));
                    var deep = new SolidColorBrush(Color.FromRgb(0, 70, 215));
                    var cyberColors = new SolidColorBrush[20];
                    for (int i = 0; i < 7; i++) cyberColors[i] = cyan;
                    for (int i = 7; i < 14; i++) cyberColors[i] = blue;
                    for (int i = 14; i < 20; i++) cyberColors[i] = deep;
                    SetSpectrumColors(cyberColors);
                    break;

                case "White":
                    var white = GetBrush("IslandTextPrimary", Color.FromRgb(242, 242, 247));
                    SetSpectrumColors(Enumerable.Repeat(white, 20).ToArray());
                    break;

                case "Sunset":
                    var sunsetColors = new SolidColorBrush[20];
                    for (int i = 0; i < 20; i++)
                    {
                        byte r = 255;
                        byte g = (byte)Math.Clamp(60 + (i * 10), 0, 255);
                        byte b = (byte)Math.Clamp(30 + (i * 9), 0, 255);
                        sunsetColors[i] = new SolidColorBrush(Color.FromRgb(r, g, b));
                    }
                    SetSpectrumColors(sunsetColors);
                    break;

                case "NeoGreen":
                default:
                    var green = GetBrush("IslandGreen", Color.FromRgb(48, 209, 88));
                    SetSpectrumColors(Enumerable.Repeat(green, 20).ToArray());
                    break;
            }
        }

        private void ApplyAudioSensitivity(float sens, bool save = true)
        {
            _audioSpectrum.Sensitivity = sens;
            if (save)
            {
                _settingsService.Current.AudioSensitivity = sens;
                _settingsService.Save();
            }
            UpdateSpectrumMenuCheckmarks();

            if (MenuSens10 != null) MenuSens10.IsChecked = Math.Abs(sens - 1.0f) < 0.05f;
            if (MenuSens15 != null) MenuSens15.IsChecked = Math.Abs(sens - 1.5f) < 0.05f;
            if (MenuSens20 != null) MenuSens20.IsChecked = Math.Abs(sens - 2.0f) < 0.05f;
        }

        private void MenuColor_NeoGreen(object sender, RoutedEventArgs e) => ApplySpectrumTheme("NeoGreen");
        private void MenuColor_Rainbow(object sender, RoutedEventArgs e) => ApplySpectrumTheme("Rainbow");
        private void MenuColor_CyberBlue(object sender, RoutedEventArgs e) => ApplySpectrumTheme("CyberCyan");
        private void MenuColor_TokyoNeon(object sender, RoutedEventArgs e) => ApplySpectrumTheme("TokyoNeon");
        private void MenuColor_CrimsonFire(object sender, RoutedEventArgs e) => ApplySpectrumTheme("CrimsonFire");
        private void MenuColor_DeepOcean(object sender, RoutedEventArgs e) => ApplySpectrumTheme("DeepOcean");
        private void MenuColor_ChampagneGold(object sender, RoutedEventArgs e) => ApplySpectrumTheme("ChampagneGold");
        private void MenuColor_SakuraPink(object sender, RoutedEventArgs e) => ApplySpectrumTheme("SakuraPink");
        private void MenuColor_White(object sender, RoutedEventArgs e) => ApplySpectrumTheme("White");
        private void MenuColor_Sunset(object sender, RoutedEventArgs e) => ApplySpectrumTheme("Sunset");

        private DrawingBrush? _segmentedMask;
        private DrawingBrush? _compactSegmentedMask;

        private DrawingBrush GetSegmentedMask()
        {
            if (_segmentedMask != null) return _segmentedMask;
            _segmentedMask = CreateSegmentedMask(3.2, 1.4);
            return _segmentedMask;
        }

        private DrawingBrush GetCompactSegmentedMask()
        {
            if (_compactSegmentedMask != null) return _compactSegmentedMask;
            _compactSegmentedMask = CreateSegmentedMask(2.4, 1.0);
            return _compactSegmentedMask;
        }

        private static DrawingBrush CreateSegmentedMask(double segmentHeight, double gapHeight)
        {
            double total = segmentHeight + gapHeight;
            var group = new DrawingGroup();
            group.Children.Add(new GeometryDrawing(
                Brushes.Black,
                null,
                new RectangleGeometry(new Rect(0, 0, 1000, segmentHeight))
            ));
            var brush = new DrawingBrush(group)
            {
                TileMode = TileMode.Tile,
                Viewport = new Rect(0, 0, 1000, total),
                ViewportUnits = BrushMappingMode.Absolute,
                Viewbox = new Rect(0, 0, 1000, total),
                ViewboxUnits = BrushMappingMode.Absolute,
                AlignmentX = AlignmentX.Left,
                AlignmentY = AlignmentY.Bottom
            };
            brush.Freeze();
            return brush;
        }

        private static void SetBarY(Rectangle? bar, double y)
        {
            if (bar == null) return;
            if (bar.RenderTransform is TranslateTransform tt)
            {
                tt.Y = y;
            }
            else
            {
                bar.RenderTransform = new TranslateTransform(0, y);
            }
        }

        private void ApplySpectrumModel(string model, bool save = true)
        {
            _spectrumModel = string.IsNullOrEmpty(model) ? "BottomUp" : model;
            if (save)
            {
                _settingsService.Current.SpectrumModel = _spectrumModel;
                _settingsService.Save();
            }

            UpdateSpectrumMenuCheckmarks();

            var bars = new[] {
                ExpBar1, ExpBar2, ExpBar3, ExpBar4, ExpBar5,
                ExpBar6, ExpBar7, ExpBar8, ExpBar9, ExpBar10,
                ExpBar11, ExpBar12, ExpBar13, ExpBar14, ExpBar15,
                ExpBar16, ExpBar17, ExpBar18, ExpBar19, ExpBar20
            };
            var compactBars = new[] { EqBar1, EqBar2, EqBar3, EqBar4 };

            switch (_spectrumModel)
            {
                case "CenterMirrored":
                    if (ExpandedSpectrumPanel != null)
                    {
                        ExpandedSpectrumPanel.Height = 52;
                        ExpandedSpectrumPanel.VerticalAlignment = VerticalAlignment.Center;
                        ExpandedSpectrumPanel.OpacityMask = null;
                    }
                    if (EqualizerPanel != null)
                    {
                        EqualizerPanel.Height = 18;
                        EqualizerPanel.VerticalAlignment = VerticalAlignment.Center;
                        EqualizerPanel.OpacityMask = null;
                    }
                    foreach (var bar in bars)
                    {
                        if (bar == null) continue;
                        bar.VerticalAlignment = VerticalAlignment.Center;
                        bar.Width = 5.5;
                        bar.Margin = new Thickness(1.8, 0, 1.8, 0);
                        bar.RadiusX = 2.75;
                        bar.RadiusY = 2.75;
                        SetBarY(bar, 0);
                    }
                    foreach (var cbar in compactBars)
                    {
                        if (cbar == null) continue;
                        cbar.VerticalAlignment = VerticalAlignment.Center;
                        cbar.Width = 3.0;
                        cbar.Margin = new Thickness(1.0, 0, 1.0, 0);
                        cbar.RadiusX = 1.5;
                        cbar.RadiusY = 1.5;
                        SetBarY(cbar, 0);
                    }
                    break;

                case "TopDown":
                    if (ExpandedSpectrumPanel != null)
                    {
                        ExpandedSpectrumPanel.Height = 52;
                        ExpandedSpectrumPanel.VerticalAlignment = VerticalAlignment.Top;
                        ExpandedSpectrumPanel.OpacityMask = null;
                    }
                    if (EqualizerPanel != null)
                    {
                        EqualizerPanel.Height = 18;
                        EqualizerPanel.VerticalAlignment = VerticalAlignment.Top;
                        EqualizerPanel.OpacityMask = null;
                    }
                    foreach (var bar in bars)
                    {
                        if (bar == null) continue;
                        bar.VerticalAlignment = VerticalAlignment.Top;
                        bar.Width = 5.5;
                        bar.Margin = new Thickness(1.8, 0, 1.8, 0);
                        bar.RadiusX = 2.5;
                        bar.RadiusY = 2.5;
                        SetBarY(bar, 0);
                    }
                    foreach (var cbar in compactBars)
                    {
                        if (cbar == null) continue;
                        cbar.VerticalAlignment = VerticalAlignment.Top;
                        cbar.Width = 3.0;
                        cbar.Margin = new Thickness(1.0, 0, 1.0, 0);
                        cbar.RadiusX = 1.0;
                        cbar.RadiusY = 1.0;
                        SetBarY(cbar, 0);
                    }
                    break;

                case "Needles":
                    if (ExpandedSpectrumPanel != null)
                    {
                        ExpandedSpectrumPanel.Height = 52;
                        ExpandedSpectrumPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        ExpandedSpectrumPanel.OpacityMask = null;
                    }
                    if (EqualizerPanel != null)
                    {
                        EqualizerPanel.Height = 18;
                        EqualizerPanel.VerticalAlignment = VerticalAlignment.Center;
                        EqualizerPanel.OpacityMask = null;
                    }
                    foreach (var bar in bars)
                    {
                        if (bar == null) continue;
                        bar.VerticalAlignment = VerticalAlignment.Bottom;
                        bar.Width = 3.0;
                        bar.Margin = new Thickness(3.0, 0, 3.0, 0);
                        bar.RadiusX = 1.5;
                        bar.RadiusY = 1.5;
                        SetBarY(bar, 0);
                    }
                    foreach (var cbar in compactBars)
                    {
                        if (cbar == null) continue;
                        cbar.VerticalAlignment = VerticalAlignment.Center;
                        cbar.Width = 2.0;
                        cbar.Margin = new Thickness(1.5, 0, 1.5, 0);
                        cbar.RadiusX = 1.0;
                        cbar.RadiusY = 1.0;
                        SetBarY(cbar, 0);
                    }
                    break;

                case "BouncingBeads":
                    if (ExpandedSpectrumPanel != null)
                    {
                        ExpandedSpectrumPanel.Height = 52;
                        ExpandedSpectrumPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        ExpandedSpectrumPanel.OpacityMask = null;
                    }
                    if (EqualizerPanel != null)
                    {
                        EqualizerPanel.Height = 18;
                        EqualizerPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        EqualizerPanel.OpacityMask = null;
                    }
                    foreach (var bar in bars)
                    {
                        if (bar == null) continue;
                        bar.VerticalAlignment = VerticalAlignment.Bottom;
                        bar.Width = 6.0;
                        bar.Height = 6.0;
                        bar.Margin = new Thickness(1.6, 0, 1.6, 0);
                        bar.RadiusX = 3.0;
                        bar.RadiusY = 3.0;
                        SetBarY(bar, 0);
                    }
                    foreach (var cbar in compactBars)
                    {
                        if (cbar == null) continue;
                        cbar.VerticalAlignment = VerticalAlignment.Bottom;
                        cbar.Width = 3.5;
                        cbar.Height = 3.5;
                        cbar.Margin = new Thickness(0.8, 0, 0.8, 0);
                        cbar.RadiusX = 1.75;
                        cbar.RadiusY = 1.75;
                        SetBarY(cbar, 0);
                    }
                    break;

                case "SegmentedVU":
                    if (ExpandedSpectrumPanel != null)
                    {
                        ExpandedSpectrumPanel.Height = 52;
                        ExpandedSpectrumPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        ExpandedSpectrumPanel.OpacityMask = GetSegmentedMask();
                    }
                    if (EqualizerPanel != null)
                    {
                        EqualizerPanel.Height = 18;
                        EqualizerPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        EqualizerPanel.OpacityMask = GetCompactSegmentedMask();
                    }
                    foreach (var bar in bars)
                    {
                        if (bar == null) continue;
                        bar.VerticalAlignment = VerticalAlignment.Bottom;
                        bar.Width = 6.0;
                        bar.Margin = new Thickness(1.5, 0, 1.5, 0);
                        bar.RadiusX = 1.0;
                        bar.RadiusY = 1.0;
                        SetBarY(bar, 0);
                    }
                    foreach (var cbar in compactBars)
                    {
                        if (cbar == null) continue;
                        cbar.VerticalAlignment = VerticalAlignment.Bottom;
                        cbar.Width = 3.2;
                        cbar.Margin = new Thickness(0.9, 0, 0.9, 0);
                        cbar.RadiusX = 0.5;
                        cbar.RadiusY = 0.5;
                        SetBarY(cbar, 0);
                    }
                    break;

                case "ChunkyBars":
                    if (ExpandedSpectrumPanel != null)
                    {
                        ExpandedSpectrumPanel.Height = 52;
                        ExpandedSpectrumPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        ExpandedSpectrumPanel.OpacityMask = null;
                    }
                    if (EqualizerPanel != null)
                    {
                        EqualizerPanel.Height = 18;
                        EqualizerPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        EqualizerPanel.OpacityMask = null;
                    }
                    foreach (var bar in bars)
                    {
                        if (bar == null) continue;
                        bar.VerticalAlignment = VerticalAlignment.Bottom;
                        bar.Width = 8.0;
                        bar.Margin = new Thickness(0.9, 0, 0.9, 0);
                        bar.RadiusX = 1.5;
                        bar.RadiusY = 1.5;
                        SetBarY(bar, 0);
                    }
                    foreach (var cbar in compactBars)
                    {
                        if (cbar == null) continue;
                        cbar.VerticalAlignment = VerticalAlignment.Bottom;
                        cbar.Width = 4.2;
                        cbar.Margin = new Thickness(0.8, 0, 0.8, 0);
                        cbar.RadiusX = 1.0;
                        cbar.RadiusY = 1.0;
                        SetBarY(cbar, 0);
                    }
                    break;

                case "BottomUp":
                default:
                    if (ExpandedSpectrumPanel != null)
                    {
                        ExpandedSpectrumPanel.Height = 52;
                        ExpandedSpectrumPanel.VerticalAlignment = VerticalAlignment.Bottom;
                        ExpandedSpectrumPanel.OpacityMask = null;
                    }
                    if (EqualizerPanel != null)
                    {
                        EqualizerPanel.Height = 18;
                        EqualizerPanel.VerticalAlignment = VerticalAlignment.Center;
                        EqualizerPanel.OpacityMask = null;
                    }
                    foreach (var bar in bars)
                    {
                        if (bar == null) continue;
                        bar.VerticalAlignment = VerticalAlignment.Bottom;
                        bar.Width = 5.5;
                        bar.Margin = new Thickness(1.8, 0, 1.8, 0);
                        bar.RadiusX = 2.5;
                        bar.RadiusY = 2.5;
                        SetBarY(bar, 0);
                    }
                    foreach (var cbar in compactBars)
                    {
                        if (cbar == null) continue;
                        cbar.VerticalAlignment = VerticalAlignment.Center;
                        cbar.Width = 3.0;
                        cbar.Margin = new Thickness(1.0, 0, 1.0, 0);
                        cbar.RadiusX = 1.0;
                        cbar.RadiusY = 1.0;
                        SetBarY(cbar, 0);
                    }
                    break;
            }
        }

        private void MenuModel_BottomUp(object sender, RoutedEventArgs e) => ApplySpectrumModel("BottomUp");
        private void MenuModel_CenterMirrored(object sender, RoutedEventArgs e) => ApplySpectrumModel("CenterMirrored");
        private void MenuModel_TopDown(object sender, RoutedEventArgs e) => ApplySpectrumModel("TopDown");
        private void MenuModel_Needles(object sender, RoutedEventArgs e) => ApplySpectrumModel("Needles");
        private void MenuModel_BouncingBeads(object sender, RoutedEventArgs e) => ApplySpectrumModel("BouncingBeads");
        private void MenuModel_SegmentedVU(object sender, RoutedEventArgs e) => ApplySpectrumModel("SegmentedVU");
        private void MenuModel_ChunkyBars(object sender, RoutedEventArgs e) => ApplySpectrumModel("ChunkyBars");

        private void MenuSens_Normal(object sender, RoutedEventArgs e) => ApplyAudioSensitivity(1.0f);
        private void MenuSens_High(object sender, RoutedEventArgs e) => ApplyAudioSensitivity(1.5f);
        private void MenuSens_Boost(object sender, RoutedEventArgs e) => ApplyAudioSensitivity(2.0f);

        private void MenuRestartAudioSpectrum_Click(object sender, RoutedEventArgs e)
        {
            _audioSpectrum.Start();
            UpdateSpectrumMenuCheckmarks();
        }

        #endregion
    }
}
