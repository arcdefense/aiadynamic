using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DynamicIsland.Services
{
    public class LyricLine
    {
        public TimeSpan Timestamp { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class LyricsResult
    {
        public bool HasSynced => SyncedLines != null && SyncedLines.Count > 0;
        public List<LyricLine> SyncedLines { get; set; } = new();
        public string PlainText { get; set; } = string.Empty;
    }

    public class LyricsService
    {
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        // In-memory cache for ultra-fast, zero-network retrieval
        private static readonly ConcurrentDictionary<string, LyricsResult> _cache = new(StringComparer.OrdinalIgnoreCase);

        private static void CacheLyrics(string key, LyricsResult result)
        {
            if (_cache.Count > 60)
            {
                _cache.Clear(); // Keep memory footprint bounded
            }
            _cache[key] = result;
        }

        static LyricsService()
        {
            try
            {
                _httpClient.DefaultRequestHeaders.Clear();
                _httpClient.DefaultRequestHeaders.Add("User-Agent", "DynamicIsland/1.0 (Windows; https://github.com/dynamicisland)");
            }
            catch { }
        }

        public async Task<LyricsResult?> FetchLyricsAsync(string rawTitle, string rawArtist)
        {
            if (string.IsNullOrWhiteSpace(rawTitle)) return null;

            var (cleanTitle, cleanArtist) = ParseTrackAndArtist(rawTitle, rawArtist);
            if (string.IsNullOrWhiteSpace(cleanTitle)) return null;

            string cacheKey = $"{cleanArtist}___{cleanTitle}".ToLowerInvariant();
            if (_cache.TryGetValue(cacheKey, out var cachedLyrics))
            {
                return cachedLyrics;
            }

            try
            {
                // Strategy 1: Direct exact match if artist and title are known
                if (!string.IsNullOrWhiteSpace(cleanArtist) && !string.IsNullOrWhiteSpace(cleanTitle))
                {
                    string urlGet = $"https://lrclib.net/api/get?artist_name={Uri.EscapeDataString(cleanArtist)}&track_name={Uri.EscapeDataString(cleanTitle)}";
                    var res = await QueryLrclibEndpointAsync(urlGet);
                    if (res != null)
                    {
                        CacheLyrics(cacheKey, res);
                        return res;
                    }

                    // Strategy 2: Specific search with artist and track_name
                    string urlSearch = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(cleanTitle)}&artist_name={Uri.EscapeDataString(cleanArtist)}";
                    res = await QueryLrclibSearchAsync(urlSearch);
                    if (res != null)
                    {
                        CacheLyrics(cacheKey, res);
                        return res;
                    }
                }

                // Strategy 3: Broad query search: "{cleanArtist} {cleanTitle}" or just "{cleanTitle}"
                string broadQuery = string.IsNullOrWhiteSpace(cleanArtist) 
                    ? cleanTitle 
                    : $"{cleanArtist} {cleanTitle}";
                
                string urlBroad = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(broadQuery)}";
                var resBroad = await QueryLrclibSearchAsync(urlBroad);
                if (resBroad != null)
                {
                    CacheLyrics(cacheKey, resBroad);
                    return resBroad;
                }

                // Strategy 4: Inverted query if separator was in reverse (e.g. "{cleanTitle} {cleanArtist}")
                if (!string.IsNullOrWhiteSpace(cleanArtist) && !string.IsNullOrWhiteSpace(cleanTitle))
                {
                    string revQuery = $"{cleanTitle} {cleanArtist}";
                    string urlRev = $"https://lrclib.net/api/search?q={Uri.EscapeDataString(revQuery)}";
                    var resRev = await QueryLrclibSearchAsync(urlRev);
                    if (resRev != null)
                    {
                        CacheLyrics(cacheKey, resRev);
                        return resRev;
                    }
                }

                // Strategy 5: Query with cleanTitle only (many tracks match title perfectly)
                if (!string.IsNullOrWhiteSpace(cleanTitle) && cleanTitle != broadQuery)
                {
                    string urlTitleOnly = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(cleanTitle)}";
                    var resTitle = await QueryLrclibSearchAsync(urlTitleOnly);
                    if (resTitle != null)
                    {
                        CacheLyrics(cacheKey, resTitle);
                        return resTitle;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Lyrics fetch error: {ex.Message}");
            }

            return null;
        }

        private static async Task<LyricsResult?> QueryLrclibEndpointAsync(string url)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    return ExtractLyricsResult(doc.RootElement);
                }
            }
            catch { }
            return null;
        }

        private static async Task<LyricsResult?> QueryLrclibSearchAsync(string url)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return null;

                string json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

                int count = Math.Min(doc.RootElement.GetArrayLength(), 8);
                LyricsResult? firstPlainMatch = null;

                for (int i = 0; i < count; i++)
                {
                    var lyrics = ExtractLyricsResult(doc.RootElement[i]);
                    if (lyrics != null)
                    {
                        if (lyrics.HasSynced)
                        {
                            return lyrics; // Best match: has live synced karaoke lines!
                        }
                        firstPlainMatch ??= lyrics;
                    }
                }

                return firstPlainMatch;
            }
            catch { }
            return null;
        }

        private static LyricsResult? ExtractLyricsResult(JsonElement element)
        {
            var res = new LyricsResult();

            // 1. Check syncedLyrics (for Karaoke sync)
            if (element.TryGetProperty("syncedLyrics", out var synced) &&
                synced.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(synced.GetString()))
            {
                string rawSynced = synced.GetString()!;
                res.SyncedLines = ParseLrc(rawSynced);
            }

            // 2. Check plainLyrics
            if (element.TryGetProperty("plainLyrics", out var plain) &&
                plain.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(plain.GetString()))
            {
                var rawPlain = plain.GetString()!;
                var plainLines = rawPlain.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var cleanedPlainLines = new List<string>();
                foreach (var pl in plainLines)
                {
                    string cleanedLine = CleanLyricText(pl);
                    if (!string.IsNullOrWhiteSpace(cleanedLine))
                    {
                        cleanedPlainLines.Add(cleanedLine);
                    }
                }
                res.PlainText = string.Join(Environment.NewLine, cleanedPlainLines);
            }
            else if (res.HasSynced)
            {
                // If plainLyrics is absent, create plain text from synced lines
                var lines = new List<string>();
                foreach (var line in res.SyncedLines)
                {
                    lines.Add(line.Text);
                }
                res.PlainText = string.Join(Environment.NewLine, lines);
            }

            if (res.HasSynced || !string.IsNullOrWhiteSpace(res.PlainText))
            {
                return res;
            }

            return null;
        }

        public static string CleanLyricText(string input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            // 1. Strip all timestamp tags: [00:00:00], [00:00.00], [00:00], [0:00.00], etc.
            string cleaned = Regex.Replace(input, @"\[\d{1,2}:\d{2}(?::\d{2})?(?:[.,]\d{1,3})?\]", "");

            // 2. Strip LRC metadata tags: [ti:...], [ar:...], [al:...], [by:...], [offset:...], [length:...], [re:...], [ve:...]
            cleaned = Regex.Replace(cleaned, @"\[[a-zA-Z]{1,10}:[^\]]*\]", "");

            // 3. Remove standalone empty brackets []
            cleaned = Regex.Replace(cleaned, @"\[\s*\]", "");

            // 4. Normalize multiple whitespaces
            cleaned = Regex.Replace(cleaned, @"[ \t]+", " ").Trim();

            return cleaned;
        }

        public static List<LyricLine> ParseLrc(string lrc)
        {
            var list = new List<LyricLine>();
            if (string.IsNullOrWhiteSpace(lrc)) return list;

            var rawLines = lrc.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            var timestampRegex = new Regex(@"\[(\d{1,2}):(\d{2})(?::(\d{2}))?(?:[.,](\d{1,3}))?\]");

            foreach (var rawLine in rawLines)
            {
                if (string.IsNullOrWhiteSpace(rawLine)) continue;

                // Find all timestamps in this line
                var timeMatches = timestampRegex.Matches(rawLine);
                if (timeMatches.Count == 0) continue;

                // Clean lyric text: strip all timestamps and metadata tags
                string text = CleanLyricText(rawLine);
                if (string.IsNullOrWhiteSpace(text)) continue;

                foreach (Match m in timeMatches)
                {
                    if (int.TryParse(m.Groups[1].Value, out int part1) &&
                        int.TryParse(m.Groups[2].Value, out int part2))
                    {
                        int hours = 0;
                        int min = part1;
                        int sec = part2;
                        int ms = 0;

                        if (m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out int part3))
                        {
                            hours = part1;
                            min = part2;
                            sec = part3;
                        }

                        if (m.Groups[4].Success)
                        {
                            string msStr = m.Groups[4].Value.PadRight(3, '0');
                            if (msStr.Length > 3) msStr = msStr.Substring(0, 3);
                            int.TryParse(msStr, out ms);
                        }

                        list.Add(new LyricLine
                        {
                            Timestamp = new TimeSpan(0, hours, min, sec, ms),
                            Text = text
                        });
                    }
                }
            }

            list.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
            return list;
        }

        public static (string Title, string Artist) ParseTrackAndArtist(string rawTitle, string rawArtist)
        {
            if (string.IsNullOrWhiteSpace(rawTitle))
                return (string.Empty, string.Empty);

            string title = rawTitle.Trim();
            string artist = rawArtist?.Trim() ?? string.Empty;

            // 1. Remove browser / platform suffixes like " - YouTube", " - YouTube Music"
            title = Regex.Replace(title, @"\s*-\s*YouTube(\s*Music)?\s*$", "", RegexOptions.IgnoreCase);

            // 2. Remove bracketed junk / noise tags (e.g. (Official Music Video), [Official Audio], etc.)
            string noisePattern = @"\s*[\(\[\{【「『](?:" +
                @"official\s+(?:music\s+)?video|" +
                @"official\s+audio|" +
                @"official\s+lyric\s+video|" +
                @"official\s+visualizer|" +
                @"music\s+video|" +
                @"lyric\s+video|" +
                @"lyrics?\s+video|" +
                @"visualizer|" +
                @"video\s+klip|" +
                @"clip\s+officiel|" +
                @"color\s+coded\s+lyrics|" +
                @"easy\s+lyrics|" +
                @"lyrics?|" +
                @"lirik|" +
                @"audio|" +
                @"mv|" +
                @"m/v|" +
                @"hd|" +
                @"hq|" +
                @"4k|" +
                @"1080p|" +
                @"720p|" +
                @"remastered(?:\s+\d{4})?|" +
                @"\d{4}\s+remaster|" +
                @"live(?:\s+at\s+.*?)?|" +
                @"acoustic|" +
                @"cover|" +
                @"version|" +
                @"feat\..*?|" +
                @"ft\..*?|" +
                @"prod\..*?" +
                @")[\)\]\}】」』]";
            title = Regex.Replace(title, noisePattern, "", RegexOptions.IgnoreCase);

            // Strip trailing "| Official Video" or "// Official Video"
            title = Regex.Replace(title, @"\s*[\|\/]{1,2}\s*(?:official|music|video|audio|lyrics?).*$", "", RegexOptions.IgnoreCase);

            // 3. Check if artist is a dummy / platform name
            bool isDummyArtist = string.IsNullOrWhiteSpace(artist) ||
                artist.Equals("YouTube", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("YouTube Music", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Google Chrome", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Microsoft Edge", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Chrome", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Firefox", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Brave", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Opera", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Unknown Artist", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                artist.Equals("Various Artists", StringComparison.OrdinalIgnoreCase);

            // 4. Try splitting title on common delimiters: " - ", " – ", " — ", " : "
            string[] separators = new[] { " - ", " – ", " — ", " : " };
            foreach (var sep in separators)
            {
                int idx = title.IndexOf(sep, StringComparison.Ordinal);
                if (idx > 0 && idx < title.Length - sep.Length)
                {
                    string left = title.Substring(0, idx).Trim();
                    string right = title.Substring(idx + sep.Length).Trim();

                    if (!string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right))
                    {
                        left = left.Trim('"', '\'', '“', '”', '‘', '’');
                        right = right.Trim('"', '\'', '“', '”', '‘', '’');

                        if (isDummyArtist)
                        {
                            // In YouTube: "Artist - Title" is the standard
                            return (right, left);
                        }
                        else
                        {
                            if (left.Equals(artist, StringComparison.OrdinalIgnoreCase))
                            {
                                return (right, artist);
                            }
                            return (right, left);
                        }
                    }
                }
            }

            // 5. If no separator found, clean up quotes and dummy artist
            title = title.Trim('"', '\'', '“', '”', '‘', '’').Trim();
            if (isDummyArtist)
            {
                artist = string.Empty;
            }
            else
            {
                artist = Regex.Replace(artist, @"\s*-\s*Topic$", "", RegexOptions.IgnoreCase);
                artist = Regex.Replace(artist, @"VEVO$", "", RegexOptions.IgnoreCase);
                artist = CleanArtist(artist);
            }

            return (title, artist);
        }

        private static string CleanArtist(string artist)
        {
            if (string.IsNullOrWhiteSpace(artist)) return string.Empty;

            int sep = artist.IndexOfAny(new[] { ',', ';', '/' });
            if (sep > 0) return artist.Substring(0, sep).Trim();
            return artist.Trim();
        }
    }
}
