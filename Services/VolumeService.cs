using System;
using NAudio.CoreAudioApi;

namespace DynamicIsland.Services
{
    public class VolumeService
    {
        private MMDevice? _defaultDevice;

        public VolumeService()
        {
            try
            {
                var enumerator = new MMDeviceEnumerator();
                _defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"VolumeService init error: {ex.Message}");
            }
        }

        public float Volume
        {
            get
            {
                try
                {
                    return _defaultDevice?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0.5f;
                }
                catch
                {
                    return 0.5f;
                }
            }
            set
            {
                try
                {
                    if (_defaultDevice != null)
                    {
                        float val = Math.Clamp(value, 0f, 1f);
                        _defaultDevice.AudioEndpointVolume.MasterVolumeLevelScalar = val;
                    }
                }
                catch { }
            }
        }

        public bool IsMuted
        {
            get
            {
                try { return _defaultDevice?.AudioEndpointVolume.Mute ?? false; }
                catch { return false; }
            }
            set
            {
                try { if (_defaultDevice != null) _defaultDevice.AudioEndpointVolume.Mute = value; }
                catch { }
            }
        }

        public void ChangeVolume(float delta)
        {
            Volume += delta;
        }

        public void ToggleMute()
        {
            IsMuted = !IsMuted;
        }
    }
}
