using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace DynamicIsland.Services
{
    public class MediaTrackInfo
    {
        public string Title { get; set; } = "Not Playing";
        public string Artist { get; set; } = "No media detected";
        public string Album { get; set; } = string.Empty;
        public string AppName { get; set; } = "NOW PLAYING";
        public BitmapImage? Thumbnail { get; set; }
        public bool IsPlaying { get; set; }
        public bool HasMedia { get; set; }
    }

    public class MediaManager
    {
        private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
        private GlobalSystemMediaTransportControlsSession? _currentSession;

        private readonly object _timelineLock = new();
        private TimeSpan _cachedPosition = TimeSpan.Zero;
        private TimeSpan _cachedEndTime = TimeSpan.Zero;
        private DateTimeOffset _cachedTimelineLastUpdatedTime = DateTimeOffset.UtcNow;
        private bool _cachedIsPlaying = false;
        private readonly System.Threading.Timer _timelineSyncTimer;

        public event Action<MediaTrackInfo>? MediaChanged;

        public MediaTrackInfo CurrentTrack { get; private set; } = new MediaTrackInfo();

        public MediaManager()
        {
            // Low-frequency background sync (every 3 seconds) on ThreadPool to prevent timeline drift
            _timelineSyncTimer = new System.Threading.Timer(_ =>
            {
                if (_cachedIsPlaying && _currentSession != null)
                {
                    UpdateTimelineCache();
                }
            }, null, 3000, 3000);
        }

        private void UpdateTimelineCache()
        {
            try
            {
                var session = _currentSession;
                if (session == null)
                {
                    lock (_timelineLock)
                    {
                        _cachedPosition = TimeSpan.Zero;
                        _cachedEndTime = TimeSpan.Zero;
                        _cachedIsPlaying = false;
                    }
                    return;
                }

                var timeline = session.GetTimelineProperties();
                var playback = session.GetPlaybackInfo();
                bool isPlaying = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                lock (_timelineLock)
                {
                    if (timeline != null)
                    {
                        _cachedPosition = timeline.Position;
                        _cachedEndTime = timeline.EndTime;
                        _cachedTimelineLastUpdatedTime = timeline.LastUpdatedTime != default ? timeline.LastUpdatedTime : DateTimeOffset.UtcNow;
                    }
                    _cachedIsPlaying = isPlaying;
                }
            }
            catch { }
        }

        public async Task InitializeAsync()
        {
            try
            {
                _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                if (_sessionManager != null)
                {
                    _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                    UpdateCurrentSession(_sessionManager.GetCurrentSession());
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"GSMTC Init error: {ex.Message}");
            }
        }

        private void SessionManager_CurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
        {
            UpdateCurrentSession(sender.GetCurrentSession());
        }

        private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? session)
        {
            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
            }

            _currentSession = session;

            if (_currentSession != null)
            {
                _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;
            }

            UpdateTimelineCache();
            _ = RefreshMediaInfoAsync();
        }

        private void CurrentSession_PlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
        {
            UpdateTimelineCache();
            _ = RefreshMediaInfoAsync();
        }

        private void CurrentSession_MediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
        {
            _ = RefreshMediaInfoAsync();
        }

        private void CurrentSession_TimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
        {
            UpdateTimelineCache();
        }

        public async Task RefreshMediaInfoAsync()
        {
            try
            {
                if (_sessionManager != null)
                {
                    var activeSession = _sessionManager.GetCurrentSession();
                    if (activeSession != _currentSession)
                    {
                        UpdateCurrentSession(activeSession);
                        return;
                    }

                    var sessions = _sessionManager.GetSessions();
                    if (sessions == null || sessions.Count == 0)
                    {
                        CurrentTrack = new MediaTrackInfo { HasMedia = false, IsPlaying = false };
                        UpdateTimelineCache();
                MediaChanged?.Invoke(CurrentTrack);
                        return;
                    }
                }

                if (_currentSession == null)
                {
                    CurrentTrack = new MediaTrackInfo { HasMedia = false, IsPlaying = false };
                    UpdateTimelineCache();
                MediaChanged?.Invoke(CurrentTrack);
                    return;
                }

                var playbackInfo = _currentSession.GetPlaybackInfo();
                if (playbackInfo == null || playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed)
                {
                    CurrentTrack = new MediaTrackInfo { HasMedia = false, IsPlaying = false };
                    UpdateTimelineCache();
                MediaChanged?.Invoke(CurrentTrack);
                    return;
                }

                bool isPlaying = playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

                var mediaProperties = await _currentSession.TryGetMediaPropertiesAsync();
                if (mediaProperties == null || (string.IsNullOrWhiteSpace(mediaProperties.Title) && string.IsNullOrWhiteSpace(mediaProperties.Artist)))
                {
                    CurrentTrack = new MediaTrackInfo { HasMedia = false, IsPlaying = isPlaying };
                    UpdateTimelineCache();
                MediaChanged?.Invoke(CurrentTrack);
                    return;
                }

                string rawTitle = mediaProperties.Title ?? string.Empty;
                string rawArtist = mediaProperties.Artist ?? string.Empty;
                string album = mediaProperties.AlbumTitle ?? string.Empty;
                string appName = FormatSourceAppName(_currentSession.SourceAppUserModelId);

                string finalTitle = rawTitle;
                string finalArtist = rawArtist;

                // Smart extraction for YouTube / Browser media:
                if (appName.Contains("CHROME") || appName.Contains("EDGE") || appName.Contains("YOUTUBE") ||
                    appName.Contains("BRAVE") || appName.Contains("FIREFOX") || appName.Contains("OPERA") ||
                    rawArtist.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    var (parsedTitle, parsedArtist) = LyricsService.ParseTrackAndArtist(rawTitle, rawArtist);
                    if (!string.IsNullOrWhiteSpace(parsedTitle))
                    {
                        finalTitle = parsedTitle;
                        if (!string.IsNullOrWhiteSpace(parsedArtist))
                        {
                            finalArtist = parsedArtist;
                        }
                    }
                }

                string safeTitle = string.IsNullOrWhiteSpace(finalTitle) ? "Unknown Title" : finalTitle;
                string safeArtist = string.IsNullOrWhiteSpace(finalArtist) ? "Unknown Artist" : finalArtist;

                // 1. FAST PATH: Check if track is identical to CurrentTrack (Prevents re-decoding album art every 2s)
                if (CurrentTrack.HasMedia &&
                    CurrentTrack.Title == safeTitle &&
                    CurrentTrack.Artist == safeArtist &&
                    CurrentTrack.Album == album &&
                    CurrentTrack.AppName == appName)
                {
                    if (CurrentTrack.IsPlaying != isPlaying)
                    {
                        CurrentTrack.IsPlaying = isPlaying;
                        UpdateTimelineCache();
                MediaChanged?.Invoke(CurrentTrack);
                    }
                    return;
                }

                // 2. Track changed: Load and downscale thumbnail (1400px -> 128px, saving ~99% RAM)
                BitmapImage? thumb = null;
                if (mediaProperties.Thumbnail != null)
                {
                    try
                    {
                        using var streamRef = await mediaProperties.Thumbnail.OpenReadAsync();
                        using var netStream = streamRef.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await netStream.CopyToAsync(ms);
                        ms.Position = 0;

                        thumb = new BitmapImage();
                        thumb.BeginInit();
                        thumb.DecodePixelWidth = 128; // Downscale to 128px width (saves ~7.8 MB RAM per track)
                        thumb.CacheOption = BitmapCacheOption.OnLoad;
                        thumb.StreamSource = ms;
                        thumb.EndInit();
                        thumb.Freeze(); // Crucial for cross-thread WPF UI updates
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to load album art: {ex.Message}");
                    }
                }

                CurrentTrack = new MediaTrackInfo
                {
                    Title = safeTitle,
                    Artist = safeArtist,
                    Album = album,
                    AppName = appName,
                    Thumbnail = thumb,
                    IsPlaying = isPlaying,
                    HasMedia = true
                };

                UpdateTimelineCache();
                MediaChanged?.Invoke(CurrentTrack);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error fetching media info: {ex.Message}");
            }
        }

        public static string FormatSourceAppName(string? rawId)
        {
            if (string.IsNullOrWhiteSpace(rawId))
                return "NOW PLAYING";

            string lower = rawId.ToLowerInvariant();

            if (lower.Contains("spotify")) return "SPOTIFY";
            if (lower.Contains("chrome")) return "GOOGLE CHROME";
            if (lower.Contains("edge") || lower.Contains("msedge")) return "MICROSOFT EDGE";
            if (lower.Contains("applemusic") || lower.Contains("apple.music")) return "APPLE MUSIC";
            if (lower.Contains("firefox")) return "FIREFOX";
            if (lower.Contains("brave")) return "BRAVE";
            if (lower.Contains("vlc")) return "VLC PLAYER";
            if (lower.Contains("opera")) return "OPERA";
            if (lower.Contains("discord")) return "DISCORD";
            if (lower.Contains("foobar")) return "FOOBAR2000";
            if (lower.Contains("aimp")) return "AIMP";
            if (lower.Contains("tidal")) return "TIDAL";
            if (lower.Contains("youtube")) return "YOUTUBE";
            if (lower.Contains("itunes")) return "ITUNES";
            if (lower.Contains("zunemusic") || lower.Contains("microsoft.media.player")) return "WINDOWS MEDIA";

            try
            {
                string filename = Path.GetFileNameWithoutExtension(rawId);
                if (!string.IsNullOrWhiteSpace(filename) && !filename.Contains("!"))
                {
                    return filename.ToUpperInvariant();
                }
                int exclamation = rawId.IndexOf('!');
                if (exclamation > 0)
                {
                    string part = rawId.Substring(exclamation + 1);
                    return part.ToUpperInvariant();
                }
            }
            catch { }

            return "MEDIA PLAYER";
        }

        public async Task TogglePlayPauseAsync()
        {
            if (_currentSession != null)
            {
                try
                {
                    lock (_timelineLock)
                    {
                        if (_cachedIsPlaying)
                        {
                            var elapsed = DateTimeOffset.UtcNow - _cachedTimelineLastUpdatedTime;
                            if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(4))
                                _cachedPosition += elapsed;
                            _cachedIsPlaying = false;
                            _cachedTimelineLastUpdatedTime = DateTimeOffset.UtcNow;
                        }
                        else
                        {
                            _cachedIsPlaying = true;
                            _cachedTimelineLastUpdatedTime = DateTimeOffset.UtcNow;
                        }
                    }

                    await _currentSession.TryTogglePlayPauseAsync();
                    UpdateTimelineCache();
                }
                catch { }
            }
        }

        public async Task SkipNextAsync()
        {
            if (_currentSession != null)
            {
                try
                {
                    await _currentSession.TrySkipNextAsync();
                    UpdateTimelineCache();
                }
                catch { }
            }
        }

        public async Task SkipPreviousAsync()
        {
            if (_currentSession != null)
            {
                try
                {
                    await _currentSession.TrySkipPreviousAsync();
                    UpdateTimelineCache();
                }
                catch { }
            }
        }

        public async Task<bool> SeekToAsync(TimeSpan position)
        {
            lock (_timelineLock)
            {
                _cachedPosition = position;
                _cachedTimelineLastUpdatedTime = DateTimeOffset.UtcNow;
            }

            if (_currentSession != null)
            {
                try
                {
                    bool res = await _currentSession.TryChangePlaybackPositionAsync(position.Ticks);
                    UpdateTimelineCache();
                    return res;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Seek error: {ex.Message}");
                }
            }
            return false;
        }

        public TimeSpan GetCurrentPosition()
        {
            lock (_timelineLock)
            {
                if (_cachedIsPlaying)
                {
                    var elapsed = DateTimeOffset.UtcNow - _cachedTimelineLastUpdatedTime;
                    if (elapsed > TimeSpan.Zero && elapsed < TimeSpan.FromHours(4))
                    {
                        var extrapolated = _cachedPosition + elapsed;
                        if (_cachedEndTime > TimeSpan.Zero && extrapolated > _cachedEndTime)
                            return _cachedEndTime;
                        return extrapolated;
                    }
                }
                return _cachedPosition;
            }
        }
    }
}
