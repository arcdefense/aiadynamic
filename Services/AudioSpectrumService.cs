using System;
using System.IO;
using System.Numerics;
using System.Threading;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Dmo;
using NAudio.Wave;

namespace DynamicIsland.Services
{
    /// <summary>
    /// Universal High-Performance Audio Spectrum Visualizer Engine (Winamp / RTA Pro Style).
    /// Features:
    /// - Full bit-depth support: 32-bit Float, 32-bit Int, 24-bit PCM (Realtek/DACs), 16-bit PCM, 8-bit.
    /// - Multi-channel acoustic downmixing (Stereo, 5.1 & 7.1 Surround with Center/LFE).
    /// - Dynamic Hardware Audio presence auto-detection (HasAudioSignal).
    /// - Automatic audio endpoint device-change tracking (Headphones / Speakers / Bluetooth via IMMNotificationClient).
    /// - 20 Logarithmic Frequency Bands from 50 Hz (Kick/Bass) to 16 kHz (Crisp Highs).
    /// - Snappy 0.80 gravity falloff for energetic dance with the beat.
    /// </summary>
    public class AudioSpectrumService : IDisposable, IMMNotificationClient
    {
        private WasapiLoopbackCapture? _capture;
        private MMDeviceEnumerator? _deviceEnumerator;
        public const int BandCount = 20;

        // 20 Standard Logarithmic Musical Bands (50 Hz to 16 kHz)
        private static readonly double[] CenterFrequencies = new double[BandCount]
        {
            50.0,   68.0,   92.0,   124.0,  168.0,  // Bass & Kick (0..4)
            228.0,  309.0,  419.0,  567.0,  768.0,  // Low-Mid & Vocal Body (5..9)
            1041.0, 1410.0, 1911.0, 2588.0, 3507.0, // Mid & Vocal Clarity (10..14)
            4750.0, 6435.0, 8718.0, 11811.0, 16000.0// Treble, Cymbals & Air (15..19)
        };

        // Standard progressive visual weighting (compensates for natural acoustic roll-off)
        private static readonly float[] BandWeights = new float[BandCount];

        private readonly float[] _bandLevels = new float[BandCount];
        private readonly float[] _targetLevels = new float[BandCount];
        private readonly float[] _compactLevels = new float[4];
        private readonly float[] _outputLevels = new float[BandCount];
        private readonly object _lock = new();

        public float Sensitivity { get; set; } = 1.0f;

        private bool _isDisposed = false;
        private readonly object _captureSync = new();
        private System.Threading.Timer? _reconnectTimer;
        private long _lastAudioSignalTick = 0;

        public bool HasAudioSignal => (Environment.TickCount64 - Interlocked.Read(ref _lastAudioSignalTick)) < 1500;
        public string AudioFormatInfo { get; private set; } = "Initializing...";
        public string? LastError { get; private set; }
        public bool IsCapturing => _capture != null;

        private const int FftSize = 2048;
        private const int HopSize = 1024; // 50% Overlap
        private const int RingBufferSize = 8192;

        private readonly float[] _ringBuffer = new float[RingBufferSize];
        private int _ringWritePos = 0;
        private int _samplesAccumulated = 0;

        private readonly Complex[] _fftBuffer = new Complex[FftSize];
        private readonly float[] _hannWindow = new float[FftSize];

        private struct BandBoundary
        {
            public int StartBin;
            public int EndBin;
        }

        private readonly BandBoundary[] _bandBoundaries = new BandBoundary[BandCount];
        private int _currentSampleRate = 48000;

        static AudioSpectrumService()
        {
            // Precompute natural progressive visual weight from 1.0x (50Hz) to 4.7x (16kHz)
            for (int i = 0; i < BandCount; i++)
            {
                double c = CenterFrequencies[i];
                BandWeights[i] = (float)(1.0 + Math.Log10(c / 50.0) * 1.5);
            }
        }

        public AudioSpectrumService()
        {
            for (int i = 0; i < FftSize; i++)
            {
                _hannWindow[i] = (float)(0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (FftSize - 1))));
            }

            RecalculateBandBoundaries(_currentSampleRate);

            try
            {
                _deviceEnumerator = new MMDeviceEnumerator();
                _deviceEnumerator.RegisterEndpointNotificationCallback(this);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"MMDeviceEnumerator notification registration failed: {ex.Message}");
            }
        }

        private void RecalculateBandBoundaries(int sampleRate)
        {
            _currentSampleRate = sampleRate > 0 ? sampleRate : 48000;
            double binWidth = (double)_currentSampleRate / FftSize;

            int currentStart = 1; // Bin 0 is DC offset (0 Hz), start strictly at bin 1
            for (int b = 0; b < BandCount; b++)
            {
                double c = CenterFrequencies[b];
                double fHigh = (b == BandCount - 1) ? 18000.0 : Math.Sqrt(c * CenterFrequencies[b + 1]);

                int targetEnd = Math.Max(currentStart, (int)Math.Round(fHigh / binWidth));
                if (b == BandCount - 1)
                {
                    targetEnd = Math.Max(targetEnd, (int)Math.Floor(18000.0 / binWidth));
                }

                targetEnd = Math.Min(targetEnd, FftSize / 2 - 1);

                _bandBoundaries[b] = new BandBoundary
                {
                    StartBin = currentStart,
                    EndBin = targetEnd
                };

                // Strictly disjoint: next band starts at the very next bin (zero shared bins)
                currentStart = targetEnd + 1;
            }
        }

        public void Start()
        {
            if (_isDisposed) return;
            Task.Run(() =>
            {
                lock (_captureSync)
                {
                    if (_isDisposed) return;
                    StopInternal();

                    try
                    {
                        var newCapture = new WasapiLoopbackCapture();
                        newCapture.DataAvailable += OnDataAvailable;
                        newCapture.RecordingStopped += OnRecordingStopped;

                        if (newCapture.WaveFormat != null && newCapture.WaveFormat.SampleRate > 0)
                        {
                            RecalculateBandBoundaries(newCapture.WaveFormat.SampleRate);
                            AudioFormatInfo = $"{newCapture.WaveFormat.SampleRate} Hz, {newCapture.WaveFormat.BitsPerSample}-bit, {newCapture.WaveFormat.Channels}ch";
                        }

                        newCapture.StartRecording();
                        _capture = newCapture;
                        LastError = null;
                    }
                    catch (Exception ex)
                    {
                        LastError = ex.Message;
                        System.Diagnostics.Debug.WriteLine($"WASAPI Loopback start error: {ex.Message}");
                        ScheduleReconnect(2500);
                    }
                }
            });
        }

        private void OnRecordingStopped(object? sender, StoppedEventArgs e)
        {
            if (!_isDisposed)
            {
                ScheduleReconnect(1000);
            }
        }

        private void ScheduleReconnect(int delayMs = 2000)
        {
            if (_isDisposed) return;
            try
            {
                _reconnectTimer?.Dispose();
                _reconnectTimer = new System.Threading.Timer(_ =>
                {
                    if (!_isDisposed)
                    {
                        Start();
                    }
                }, null, delayMs, System.Threading.Timeout.Infinite);
            }
            catch { }
        }

        private void StopInternal()
        {
            try
            {
                if (_capture != null)
                {
                    var oldCapture = _capture;
                    _capture = null;
                    oldCapture.DataAvailable -= OnDataAvailable;
                    oldCapture.RecordingStopped -= OnRecordingStopped;
                    try { oldCapture.StopRecording(); } catch { }
                    try { oldCapture.Dispose(); } catch { }
                }
            }
            catch { }
        }

        public void Stop()
        {
            Task.Run(() =>
            {
                lock (_captureSync)
                {
                    StopInternal();
                }
            });
        }

        private void OnDataAvailable(object? sender, WaveInEventArgs e)
        {
            if (e.BytesRecorded == 0 || _capture == null) return;

            int bytesPerSample = _capture.WaveFormat.BitsPerSample / 8;
            int channels = _capture.WaveFormat.Channels;
            if (bytesPerSample <= 0 || channels <= 0) return;

            int frameSize = bytesPerSample * channels;
            int totalFrames = e.BytesRecorded / frameSize;
            if (totalFrames <= 0) return;

            bool isFloat = _capture.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat ||
                           (_capture.WaveFormat is WaveFormatExtensible ext && ext.SubFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT);

            for (int f = 0; f < totalFrames; f++)
            {
                int offset = f * frameSize;
                float monoSample = 0f;

                if (bytesPerSample == 4) // 32-bit Float or 32-bit Int
                {
                    if (isFloat)
                    {
                        float left = BitConverter.ToSingle(e.Buffer, offset);
                        if (channels == 1)
                        {
                            monoSample = left;
                        }
                        else
                        {
                            float right = BitConverter.ToSingle(e.Buffer, offset + 4);
                            if (channels >= 4) // 5.1 / 7.1 Surround (Mix Center & Subwoofer/LFE)
                            {
                                float center = BitConverter.ToSingle(e.Buffer, offset + 8);
                                float lfe = BitConverter.ToSingle(e.Buffer, offset + 12);
                                monoSample = (left + right) * 0.35f + center * 0.15f + lfe * 0.15f;
                            }
                            else
                            {
                                monoSample = (left + right) * 0.5f;
                            }
                        }
                    }
                    else // 32-bit Signed Integer PCM
                    {
                        int left = BitConverter.ToInt32(e.Buffer, offset);
                        if (channels == 1)
                        {
                            monoSample = left / 2147483648.0f;
                        }
                        else
                        {
                            int right = BitConverter.ToInt32(e.Buffer, offset + 4);
                            if (channels >= 4)
                            {
                                int center = BitConverter.ToInt32(e.Buffer, offset + 8);
                                int lfe = BitConverter.ToInt32(e.Buffer, offset + 12);
                                monoSample = ((left + right) * 0.35f + center * 0.15f + lfe * 0.15f) / 2147483648.0f;
                            }
                            else
                            {
                                monoSample = ((left + right) * 0.5f) / 2147483648.0f;
                            }
                        }
                    }
                }
                else if (bytesPerSample == 3) // 24-bit PCM (Standard for Realtek HD Audio & USB DACs)
                {
                    int left24 = (e.Buffer[offset] << 8) | (e.Buffer[offset + 1] << 16) | ((sbyte)e.Buffer[offset + 2] << 24);
                    if (channels == 1)
                    {
                        monoSample = left24 / 2147483648.0f;
                    }
                    else
                    {
                        int right24 = (e.Buffer[offset + 3] << 8) | (e.Buffer[offset + 4] << 16) | ((sbyte)e.Buffer[offset + 5] << 24);
                        if (channels >= 4)
                        {
                            int center24 = (e.Buffer[offset + 6] << 8) | (e.Buffer[offset + 7] << 16) | ((sbyte)e.Buffer[offset + 8] << 24);
                            int lfe24 = (e.Buffer[offset + 9] << 8) | (e.Buffer[offset + 10] << 16) | ((sbyte)e.Buffer[offset + 11] << 24);
                            monoSample = ((left24 + right24) * 0.35f + center24 * 0.15f + lfe24 * 0.15f) / 2147483648.0f;
                        }
                        else
                        {
                            monoSample = ((left24 + right24) * 0.5f) / 2147483648.0f;
                        }
                    }
                }
                else if (bytesPerSample == 2) // 16-bit PCM Short
                {
                    short left16 = BitConverter.ToInt16(e.Buffer, offset);
                    if (channels == 1)
                    {
                        monoSample = left16 / 32768.0f;
                    }
                    else
                    {
                        short right16 = BitConverter.ToInt16(e.Buffer, offset + 2);
                        if (channels >= 4)
                        {
                            short center16 = BitConverter.ToInt16(e.Buffer, offset + 4);
                            short lfe16 = BitConverter.ToInt16(e.Buffer, offset + 6);
                            monoSample = ((left16 + right16) * 0.35f + center16 * 0.15f + lfe16 * 0.15f) / 32768.0f;
                        }
                        else
                        {
                            monoSample = ((left16 + right16) * 0.5f) / 32768.0f;
                        }
                    }
                }
                else if (bytesPerSample == 1) // 8-bit unsigned PCM
                {
                    monoSample = (e.Buffer[offset] - 128) / 128.0f;
                }

                if (float.IsNaN(monoSample) || float.IsInfinity(monoSample)) monoSample = 0f;

                _ringBuffer[_ringWritePos] = monoSample;
                _ringWritePos = (_ringWritePos + 1) % RingBufferSize;
                _samplesAccumulated++;

                if (_samplesAccumulated >= HopSize)
                {
                    _samplesAccumulated -= HopSize;
                    ProcessFftFrame();
                }
            }
        }

        private void ProcessFftFrame()
        {
            int readStart = (_ringWritePos - FftSize + RingBufferSize) % RingBufferSize;
            for (int i = 0; i < FftSize; i++)
            {
                int idx = (readStart + i) % RingBufferSize;
                float sample = _ringBuffer[idx];
                _fftBuffer[i] = new Complex(sample * _hannWindow[i], 0);
            }

            Fft(_fftBuffer);

            float sens = Sensitivity;
            float maxBandVal = 0f;

            lock (_lock)
            {
                for (int b = 0; b < BandCount; b++)
                {
                    var bound = _bandBoundaries[b];
                    int start = bound.StartBin;
                    int end = Math.Min(bound.EndBin, FftSize / 2 - 1);
                    int binCount = end - start + 1;

                    // Extract peak and average magnitude across bins (85% peak + 15% avg highlights distinct musical notes and melodies)
                    double peak = 0;
                    double sum = 0;
                    for (int k = start; k <= end; k++)
                    {
                        double mag = _fftBuffer[k].Magnitude;
                        if (mag > peak) peak = mag;
                        sum += mag;
                    }
                    double avg = sum / Math.Max(1, binCount);
                    double val = (peak * 0.85 + avg * 0.15);

                    // Normal scaling calibrated for lively, bouncy movement:
                    // val / 256 normalizes the FFT output, weight provides natural tonal balance
                    float weight = BandWeights[b];
                    double magVal = (val / 256.0) * weight * 2.2 * sens;

                    // Dynamic expansion: smooth 0.75 power curve gives wide vertical bounce
                    magVal = Math.Pow(magVal, 0.75);

                    float clamped = (float)Math.Clamp(magVal, 0.0, 1.0);
                    _targetLevels[b] = clamped;
                    if (clamped > maxBandVal) maxBandVal = clamped;
                }
            }

            // Real-time audio presence auto-detection
            if (maxBandVal > 0.008f)
            {
                Interlocked.Exchange(ref _lastAudioSignalTick, Environment.TickCount64);
            }
        }

        /// <summary>
        /// Reads the 20-band audio levels with snappy, bouncy visual ballistics:
        /// 15ms fast transient attack and 0.80 swift gravity release for high-energy bounce.
        /// </summary>
        public float[] GetCurrentLevelsWithDecay()
        {
            lock (_lock)
            {
                for (int i = 0; i < BandCount; i++)
                {
                    float target = _targetLevels[i];
                    if (target > _bandLevels[i])
                    {
                        // Explosive transient attack (85% instant jump on beat)
                        _bandLevels[i] = _bandLevels[i] * 0.15f + target * 0.85f;
                    }
                    else
                    {
                        // Snappy gravity falloff (drops quickly between beats to create rhythmic bounce)
                        _bandLevels[i] = _bandLevels[i] * 0.80f;
                    }
                }

                Array.Copy(_bandLevels, _outputLevels, BandCount);
                return _outputLevels;
            }
        }

        /// <summary>
        /// Downmixes the provided 20-band spectrum into 4 proportional acoustic groups for Compact View.
        /// Using an already-retrieved levels array prevents decaying twice in a single frame.
        /// Band 0: Bass & Kick (Bands 0..4, 50 Hz - 170 Hz)
        /// Band 1: Low-Mid (Bands 5..9, 220 Hz - 770 Hz)
        /// Band 2: Mid & High-Mid (Bands 10..14, 1.0 kHz - 3.5 kHz)
        /// Band 3: Treble & Air (Bands 15..19, 4.7 kHz - 16.0 kHz)
        /// </summary>
        public float[] GetCompactLevels(float[] levels)
        {
            float b0 = Math.Max(levels[0], Math.Max(levels[1], Math.Max(levels[2], Math.Max(levels[3], levels[4]))));
            float b1 = Math.Max(levels[5], Math.Max(levels[6], Math.Max(levels[7], Math.Max(levels[8], levels[9]))));
            float b2 = Math.Max(levels[10], Math.Max(levels[11], Math.Max(levels[12], Math.Max(levels[13], levels[14]))));
            float b3 = Math.Max(levels[15], Math.Max(levels[16], Math.Max(levels[17], Math.Max(levels[18], levels[19]))));

            _compactLevels[0] = Math.Clamp(b0 * 1.0f, 0f, 1f);
            _compactLevels[1] = Math.Clamp(b1 * 1.0f, 0f, 1f);
            _compactLevels[2] = Math.Clamp(b2 * 1.0f, 0f, 1f);
            _compactLevels[3] = Math.Clamp(b3 * 1.0f, 0f, 1f);

            return _compactLevels;
        }

        public float[] GetCompactLevelsWithDecay()
        {
            return GetCompactLevels(GetCurrentLevelsWithDecay());
        }

        public void ClearLevels()
        {
            lock (_lock)
            {
                Array.Clear(_bandLevels, 0, BandCount);
                Array.Clear(_targetLevels, 0, BandCount);
                Array.Clear(_compactLevels, 0, 4);
            }
        }

        private static void Fft(Complex[] buffer)
        {
            int n = buffer.Length;
            int j = 0;
            for (int i = 0; i < n - 1; i++)
            {
                if (i < j)
                {
                    Complex temp = buffer[i];
                    buffer[i] = buffer[j];
                    buffer[j] = temp;
                }
                int k = n / 2;
                while (k <= j)
                {
                    j -= k;
                    k /= 2;
                }
                j += k;
            }

            for (int len = 2; len <= n; len <<= 1)
            {
                double angle = -2 * Math.PI / len;
                Complex wlen = new Complex(Math.Cos(angle), Math.Sin(angle));
                for (int i = 0; i < n; i += len)
                {
                    Complex w = 1;
                    for (int k = 0; k < len / 2; k++)
                    {
                        Complex u = buffer[i + k];
                        Complex v = buffer[i + k + len / 2] * w;
                        buffer[i + k] = u + v;
                        buffer[i + k + len / 2] = u - v;
                        w *= wlen;
                    }
                }
            }
        }

        #region IMMNotificationClient Device Change Handlers

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && (role == Role.Multimedia || role == Role.Console))
            {
                // Active output device changed (e.g. plugged headphones or connected bluetooth)
                ScheduleReconnect(250);
            }
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active)
            {
                ScheduleReconnect(500);
            }
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }

        public void OnDeviceRemoved(string deviceId)
        {
            ScheduleReconnect(500);
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        #endregion

        public void Dispose()
        {
            _isDisposed = true;
            try
            {
                _reconnectTimer?.Dispose();
                _reconnectTimer = null;
            }
            catch { }

            try
            {
                if (_deviceEnumerator != null)
                {
                    _deviceEnumerator.UnregisterEndpointNotificationCallback(this);
                    _deviceEnumerator.Dispose();
                    _deviceEnumerator = null;
                }
            }
            catch { }

            lock (_captureSync)
            {
                StopInternal();
            }
        }
    }
}
