using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace DynamicIsland.Services
{
    /// <summary>
    /// Professional 10-Band Graphic Equalizer Engine & Settings Manager.
    /// Based on ISO 266 1-Octave Standard Frequencies and Robert Bristow-Johnson Audio DSP Math.
    /// Supports real-time bridge to Windows Equalizer APO for true 0ms system-wide hardware EQ.
    /// </summary>
    public class EqualizerService
    {
        public const int BandCount = 10;

        // ISO 266 Standard 1-Octave Center Frequencies (Hz)
        public static readonly float[] CenterFrequencies = new float[BandCount]
        {
            31.5f,  // 0: Sub-bass (rumble & deep sub)
            63f,    // 1: Kick & Sub (bass drum & bassline punch)
            125f,   // 2: Upper Bass (warmth & snare body)
            250f,   // 3: Low-Mid (vocal body & guitar warmth)
            500f,   // 4: Mid (vocal core & acoustic instruments)
            1000f,  // 5: Center-Mid (vocal articulation & lead synth)
            2000f,  // 6: Upper-Mid / Presence (clarity & edge)
            4000f,  // 7: Attack / Brilliance (stick hits & guitar crunch)
            8000f,  // 8: Treble / Cymbals (airiness & hi-hat shimmer)
            16000f  // 9: Air / Sparkle (ultra-high breath & ambient room)
        };

        public static readonly string[] BandLabels = new string[BandCount]
        {
            "31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k"
        };

        // Standard Q factor for 1-octave band spacing
        public const float StandardQ = 1.414f;

        private readonly float[] _bandGains = new float[BandCount];
        public float[] BandGains => _bandGains;

        public float Preamp { get; set; } = 0.0f; // -12dB to 0dB
        public bool IsEnabled { get; set; } = true;
        public string CurrentPreset { get; set; } = "Flat";

        public event Action? SettingsChanged;

        private static readonly Dictionary<string, (float[] Gains, float Preamp)> Presets = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Flat"] = (new float[] { 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f }, 0.0f),
            ["Harman Target"] = (new float[] { +5.0f, +4.0f, +2.0f, 0.0f, -0.5f, -1.0f, +2.0f, +3.5f, +1.0f, -1.5f }, -5.0f),
            ["Harman"] = (new float[] { +5.0f, +4.0f, +2.0f, 0.0f, -0.5f, -1.0f, +2.0f, +3.5f, +1.0f, -1.5f }, -5.0f),
            ["Bass Boost"] = (new float[] { +6.0f, +6.0f, +3.5f, +1.0f, -1.0f, -1.5f, 0.0f, +1.0f, +1.5f, 0.0f }, -6.0f),
            ["Bass"] = (new float[] { +6.0f, +6.0f, +3.5f, +1.0f, -1.0f, -1.5f, 0.0f, +1.0f, +1.5f, 0.0f }, -6.0f),
            ["Vocal Clarity"] = (new float[] { -2.0f, -1.5f, 0.0f, -1.5f, +1.5f, +2.5f, +3.5f, +3.0f, +2.0f, 0.0f }, -3.5f),
            ["Vocal"] = (new float[] { -2.0f, -1.5f, 0.0f, -1.5f, +1.5f, +2.5f, +3.5f, +3.0f, +2.0f, 0.0f }, -3.5f),
            ["Podcast / Voice"] = (new float[] { -2.0f, -1.5f, 0.0f, -1.5f, +1.5f, +2.5f, +3.5f, +3.0f, +2.0f, 0.0f }, -3.5f),
            ["Rock / V-Shape"] = (new float[] { +4.0f, +4.5f, +2.5f, 0.0f, -1.5f, -1.0f, +1.5f, +3.5f, +4.0f, +3.0f }, -4.5f),
            ["Rock"] = (new float[] { +4.0f, +4.5f, +2.5f, 0.0f, -1.5f, -1.0f, +1.5f, +3.5f, +4.0f, +3.0f }, -4.5f),
            ["Pop / Modern"] = (new float[] { +2.5f, +3.5f, +2.0f, 0.0f, -1.0f, 0.0f, +2.5f, +3.0f, +2.5f, +2.0f }, -3.5f),
            ["Pop"] = (new float[] { +2.5f, +3.5f, +2.0f, 0.0f, -1.0f, 0.0f, +2.5f, +3.0f, +2.5f, +2.0f }, -3.5f),
            ["Electronic / EDM"] = (new float[] { +6.0f, +5.0f, +2.0f, -1.0f, -2.0f, 0.0f, +1.5f, +3.0f, +4.5f, +5.0f }, -6.0f),
            ["EDM"] = (new float[] { +6.0f, +5.0f, +2.0f, -1.0f, -2.0f, 0.0f, +1.5f, +3.0f, +4.5f, +5.0f }, -6.0f),
            ["Jazz / Acoustic"] = (new float[] { +1.5f, +2.5f, +1.5f, +0.5f, 0.0f, +1.0f, +2.0f, +1.5f, +0.5f, +1.0f }, -2.5f),
            ["Jazz"] = (new float[] { +1.5f, +2.5f, +1.5f, +0.5f, 0.0f, +1.0f, +2.0f, +1.5f, +0.5f, +1.0f }, -2.5f),
            ["Classical / Hall"] = (new float[] { +1.0f, +1.0f, 0.0f, 0.0f, 0.0f, 0.0f, +1.5f, +2.0f, +1.0f, +1.5f }, -2.0f),
            ["Classical"] = (new float[] { +1.0f, +1.0f, 0.0f, 0.0f, 0.0f, 0.0f, +1.5f, +2.0f, +1.0f, +1.5f }, -2.0f)
        };

        public IEnumerable<string> AvailablePresets => Presets.Keys;

        private readonly string _settingsFilePath;
        private string? _apoConfigPath;
        public bool IsApoInstalled { get; private set; }

        public EqualizerService()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string folder = Path.Combine(appData, "AiaDinamic");
            Directory.CreateDirectory(folder);
            _settingsFilePath = Path.Combine(folder, "equalizer_settings.json");

            DetectApoInstallation();
            LoadSettings();
            SyncToApo();
        }

        public bool CheckAndRefreshApoStatus()
        {
            bool prevInstalled = IsApoInstalled;
            DetectApoInstallation();
            if (!prevInstalled && IsApoInstalled)
            {
                SyncToApo();
            }
            return IsApoInstalled;
        }

        public void LaunchApoInstallerOrDownload()
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string bundledPath = Path.Combine(baseDir, "tools", "EqualizerAPO-x64.exe");
                string localPath = Path.Combine(baseDir, "EqualizerAPO-x64.exe");

                string? targetPath = null;
                if (File.Exists(bundledPath)) targetPath = bundledPath;
                else if (File.Exists(localPath)) targetPath = localPath;

                if (!string.IsNullOrEmpty(targetPath))
                {
                    var psi = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = targetPath,
                        UseShellExecute = true,
                        Verb = "runas"
                    };
                    System.Diagnostics.Process.Start(psi);
                    return;
                }

                // If not found locally, open official direct download link
                var webPsi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://sourceforge.net/projects/equalizerapo/files/latest/download",
                    UseShellExecute = true
                };
                System.Diagnostics.Process.Start(webPsi);
            }
            catch { }
        }

        public void OpenApoDownloadPage() => LaunchApoInstallerOrDownload();

        private void DetectApoInstallation()
        {
            try
            {
                // Check common Equalizer APO paths
                string standardPath = @"C:\Program Files\EqualizerAPO\config\config.txt";
                string standardPath86 = @"C:\Program Files (x86)\EqualizerAPO\config\config.txt";

                if (File.Exists(standardPath))
                {
                    _apoConfigPath = standardPath;
                    IsApoInstalled = true;
                    return;
                }

                if (File.Exists(standardPath86))
                {
                    _apoConfigPath = standardPath86;
                    IsApoInstalled = true;
                    return;
                }

                // Check directory even if config.txt isn't created yet
                string dir64 = @"C:\Program Files\EqualizerAPO\config";
                if (Directory.Exists(dir64))
                {
                    _apoConfigPath = standardPath;
                    if (!File.Exists(standardPath))
                    {
                        File.WriteAllText(standardPath, "# Equalizer APO Configuration file\n");
                    }
                    IsApoInstalled = true;
                    return;
                }

                string dir86 = @"C:\Program Files (x86)\EqualizerAPO\config";
                if (Directory.Exists(dir86))
                {
                    _apoConfigPath = standardPath86;
                    if (!File.Exists(standardPath86))
                    {
                        File.WriteAllText(standardPath86, "# Equalizer APO Configuration file\n");
                    }
                    IsApoInstalled = true;
                    return;
                }

                // Check Windows Registry for custom install path
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\EqualizerAPO");
                if (key != null)
                {
                    string? installPath = key.GetValue("InstallPath") as string;
                    if (!string.IsNullOrWhiteSpace(installPath))
                    {
                        string customConfig = Path.Combine(installPath, "config", "config.txt");
                        string configDir = Path.Combine(installPath, "config");
                        if (Directory.Exists(configDir))
                        {
                            _apoConfigPath = customConfig;
                            if (!File.Exists(customConfig))
                            {
                                File.WriteAllText(customConfig, "# Equalizer APO Configuration file\n");
                            }
                            IsApoInstalled = true;
                            return;
                        }
                    }
                }
            }
            catch
            {
                // Registry read may fail under restricted rights
            }

            IsApoInstalled = false;
            _apoConfigPath = null;
        }

        public void SetBandGain(int bandIndex, float gainDb)
        {
            if (bandIndex < 0 || bandIndex >= BandCount) return;

            float clamped = Math.Clamp(gainDb, -12.0f, 12.0f);
            if (Math.Abs(_bandGains[bandIndex] - clamped) > 0.05f)
            {
                _bandGains[bandIndex] = clamped;
                CurrentPreset = "Custom";
                SyncState();
            }
        }

        public void SetPreamp(float preampDb)
        {
            float clamped = Math.Clamp(preampDb, -12.0f, 0.0f);
            if (Math.Abs(Preamp - clamped) > 0.05f)
            {
                Preamp = clamped;
                SyncState();
            }
        }

        public void SetEnabled(bool enabled)
        {
            if (IsEnabled != enabled)
            {
                IsEnabled = enabled;
                SyncState();
            }
        }

        public void ApplyPreset(string presetName)
        {
            if (Presets.TryGetValue(presetName, out var preset))
            {
                for (int i = 0; i < BandCount; i++)
                {
                    _bandGains[i] = preset.Gains[i];
                }
                Preamp = preset.Preamp;
                CurrentPreset = presetName;
                SyncState();
            }
        }

        public void ResetToFlat()
        {
            ApplyPreset("Flat");
        }

        private void SyncState()
        {
            SaveSettings();
            SyncToApo();
            SettingsChanged?.Invoke();
        }

        private void SyncToApo()
        {
            if (!IsApoInstalled)
            {
                CheckAndRefreshApoStatus();
            }

            if (!IsApoInstalled || string.IsNullOrEmpty(_apoConfigPath))
                return;

            try
            {
                string dir = Path.GetDirectoryName(_apoConfigPath) ?? "";
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string configText = File.Exists(_apoConfigPath) ? File.ReadAllText(_apoConfigPath) : "";
                const string startTag = "# AIA_DINAMIC_EQ_START";
                const string endTag = "# AIA_DINAMIC_EQ_END";

                string block;
                if (IsEnabled)
                {
                    var inv = System.Globalization.CultureInfo.InvariantCulture;
                    var sb = new System.Text.StringBuilder();
                    sb.AppendLine(startTag);
                    float cleanPreamp = Math.Abs(Preamp) < 0.001f ? 0.0f : Preamp;
                    sb.AppendLine($"Preamp: {cleanPreamp.ToString("0.0", inv)} dB");

                    sb.Append("GraphicEQ: ");
                    for (int i = 0; i < BandCount; i++)
                    {
                        if (i > 0) sb.Append("; ");
                        sb.Append($"{CenterFrequencies[i].ToString("0.#", inv)} {_bandGains[i].ToString("0.0", inv)}");
                    }
                    sb.AppendLine();
                    sb.Append(endTag);
                    block = sb.ToString();
                }
                else
                {
                    // Bypass state
                    block = $"{startTag}\n# Equalizer Bypass Mode\nPreamp: 0.0 dB\nGraphicEQ: 31.5 0.0; 63.0 0.0; 125.0 0.0; 250.0 0.0; 500.0 0.0; 1000.0 0.0; 2000.0 0.0; 4000.0 0.0; 8000.0 0.0; 16000.0 0.0\n{endTag}";
                }

                int sIdx = configText.IndexOf(startTag, StringComparison.Ordinal);
                int eIdx = configText.IndexOf(endTag, StringComparison.Ordinal);

                string newContent;
                if (sIdx >= 0 && eIdx >= sIdx)
                {
                    // Replace existing block
                    newContent = configText.Substring(0, sIdx) + block + configText.Substring(eIdx + endTag.Length);
                }
                else
                {
                    // Append block to end of config.txt
                    newContent = configText.TrimEnd() + Environment.NewLine + Environment.NewLine + block + Environment.NewLine;
                }

                File.WriteAllText(_apoConfigPath, newContent);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"APO sync error: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                var state = new EqualizerState
                {
                    Gains = _bandGains,
                    Preamp = Preamp,
                    IsEnabled = IsEnabled,
                    CurrentPreset = CurrentPreset
                };

                string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    string json = File.ReadAllText(_settingsFilePath);
                    var state = JsonSerializer.Deserialize<EqualizerState>(json);
                    if (state != null && state.Gains != null && state.Gains.Length == BandCount)
                    {
                        for (int i = 0; i < BandCount; i++)
                        {
                            _bandGains[i] = Math.Clamp(state.Gains[i], -12.0f, 12.0f);
                        }
                        Preamp = Math.Clamp(state.Preamp, -12.0f, 0.0f);
                        IsEnabled = state.IsEnabled;
                        CurrentPreset = state.CurrentPreset ?? "Flat";
                        return;
                    }
                }
            }
            catch { }

            // Default fallback
            ResetToFlat();
        }

        private class EqualizerState
        {
            public float[]? Gains { get; set; }
            public float Preamp { get; set; }
            public bool IsEnabled { get; set; }
            public string? CurrentPreset { get; set; }
        }
    }

    /// <summary>
    /// Robert Bristow-Johnson Audio DSP Peaking Biquad Filter implementation.
    /// Provides pure IIR mathematical filtering for internal audio processing.
    /// </summary>
    public class BiquadPeakingFilter
    {
        private double _b0, _b1, _b2;
        private double _a1, _a2;
        private double _x1, _x2;
        private double _y1, _y2;

        public void SetParameters(double sampleRate, double centerFreq, double gainDb, double q = 1.414)
        {
            double A = Math.Pow(10.0, gainDb / 40.0);
            double w0 = 2.0 * Math.PI * centerFreq / sampleRate;
            double cosW0 = Math.Cos(w0);
            double sinW0 = Math.Sin(w0);
            double alpha = sinW0 / (2.0 * q);

            double b0Temp = 1.0 + alpha * A;
            double b1Temp = -2.0 * cosW0;
            double b2Temp = 1.0 - alpha * A;
            double a0Temp = 1.0 + alpha / A;
            double a1Temp = -2.0 * cosW0;
            double a2Temp = 1.0 - alpha / A;

            _b0 = b0Temp / a0Temp;
            _b1 = b1Temp / a0Temp;
            _b2 = b2Temp / a0Temp;
            _a1 = a1Temp / a0Temp;
            _a2 = a2Temp / a0Temp;
        }

        public double Process(double sample)
        {
            double outSample = (_b0 * sample) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);

            _x2 = _x1;
            _x1 = sample;
            _y2 = _y1;
            _y1 = outSample;

            return outSample;
        }

        public void Reset()
        {
            _x1 = _x2 = _y1 = _y2 = 0.0;
        }
    }
}
