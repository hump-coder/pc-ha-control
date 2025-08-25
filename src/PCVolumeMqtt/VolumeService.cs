using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Diagnostics;

namespace PCVolumeMqtt;

public record DeviceInfo(string Id, string Name);

public record VolumeChangedEventArgs(float Volume);

public class VolumeService : IDisposable
{
    private MMDeviceEnumerator _enumerator;
    private MMDevice _device;
    private readonly AudioEndpointVolumeNotificationDelegate _callback;
    private readonly IMMNotificationClient _notificationClient;

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public VolumeService()
    {
        Trace.WriteLine("Initializing VolumeService");
        _enumerator = new MMDeviceEnumerator();
        _callback = data =>
        {
            var vol = data.MasterVolume * 100f;
            Trace.WriteLine($"OnVolumeNotification {vol}");
            VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(vol));
        };
        _notificationClient = new NotificationClient(RefreshDevice);
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        _device.AudioEndpointVolume.OnVolumeNotification += _callback;
        Trace.WriteLine($"Initial volume {GetVolume()}");
    }

    public DeviceInfo GetDevice() => new(_device.ID, _device.FriendlyName);

    public float GetVolume() => _device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;

    public void SetVolume(float volume)
    {
        var v = Math.Clamp(volume, 0f, 100f) / 100f;
        Trace.WriteLine($"SetVolume {volume} (clamped {v * 100f})");
        _device.AudioEndpointVolume.MasterVolumeLevelScalar = v;
    }

    public void Dispose()
    {
        Trace.WriteLine("Disposing VolumeService");
        _device.AudioEndpointVolume.OnVolumeNotification -= _callback;
        _device.Dispose();
        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _enumerator.Dispose();
    }

    private void RefreshDevice(string _)
    {
        // Re-create the device enumerator and default device just like we do
        // at startup. This ensures volume notifications continue to flow
        // after the output device changes.

        Trace.WriteLine("Refreshing audio device");
        _device.AudioEndpointVolume.OnVolumeNotification -= _callback;
        _device.Dispose();
        _enumerator.UnregisterEndpointNotificationCallback(_notificationClient);
        _enumerator.Dispose();

        _enumerator = new MMDeviceEnumerator();
        _enumerator.RegisterEndpointNotificationCallback(_notificationClient);
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        _device.AudioEndpointVolume.OnVolumeNotification += _callback;
        var volume = GetVolume();
        Trace.WriteLine($"Refreshed device, volume {volume}");
        VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(volume));
    }

    private class NotificationClient : IMMNotificationClient
    {
        private readonly Action<string> _onDefaultDeviceChanged;

        public NotificationClient(Action<string> onDefaultDeviceChanged)
        {
            _onDefaultDeviceChanged = onDefaultDeviceChanged;
        }

        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render &&
                (role == Role.Console || role == Role.Multimedia))
            {
                _onDefaultDeviceChanged(defaultDeviceId);
            }
        }

        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }
}

