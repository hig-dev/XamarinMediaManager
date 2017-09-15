using System;
using Android.Support.V4.Media;
using Plugin.MediaManager.Abstractions;
using Plugin.MediaManager.Abstractions.EventArguments;

namespace Plugin.MediaManager
{
    public class VolumeManagerImplementation : IVolumeManager
    {
        private bool _mute;
        private float _maxVolume;
        private float _currentVolume;

        public event VolumeChangedEventHandler VolumeChanged;

        public float CurrentVolume
        {
            get => _currentVolume;
            set
            {
                if (Math.Abs(_currentVolume - value) > 0.001)
                {
                    _currentVolume = value;
                    SetVolumeDelegate?.Invoke(MaxVolume, CurrentVolume, Mute);
                    VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(CurrentVolume, Mute));
                }
            }
        }

        public float MaxVolume
        {
            get => _maxVolume;
            set
            {
                if (Math.Abs(_maxVolume - value) > 0.001)
                {
                    _maxVolume = value;
                    SetVolumeDelegate?.Invoke(MaxVolume, CurrentVolume, Mute);
                    VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(CurrentVolume, Mute));
                }

            }
        }

        public bool Mute
        {
            get => _mute;
            set
            {
                if (_mute != value)
                {
                    _mute = value;
                    SetVolumeDelegate?.Invoke(MaxVolume, CurrentVolume, Mute);
                    VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(CurrentVolume, Mute));
                }
            }
        }

        public Action<float, float, bool> SetVolumeDelegate { get; set; }

        public VolumeManagerImplementation(int maxVolume, int volume)
        {
            CurrentVolume = volume;
            MaxVolume = maxVolume;
        }
    }
}
