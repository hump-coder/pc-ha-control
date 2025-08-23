using NAudio.CoreAudioApi;

namespace PCVolumeMqtt;

public record DeviceInfo(string Id, string Name);

public record VolumeChangedEventArgs(string DeviceId, float Volume);

public class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly Dictionary<string, MMDevice> _devices = new();

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public VolumeService()
    {
        _enumerator = new MMDeviceEnumerator();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            _devices[device.ID] = device;
            device.AudioEndpointVolume.OnVolumeNotification += data =>
            {
                VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(device.ID, data.MasterVolume * 100f));
            };
        }
    }

    public IEnumerable<DeviceInfo> GetDevices() => _devices.Values.Select(d => new DeviceInfo(d.ID, d.FriendlyName));

    public float GetVolume(string id) => _devices[id].AudioEndpointVolume.MasterVolumeLevelScalar * 100f;

    public void SetVolume(string id, float volume)
    {
        if (!_devices.TryGetValue(id, out var device))
        {
            return;
        }

        var v = Math.Clamp(volume, 0f, 100f) / 100f;
        device.AudioEndpointVolume.MasterVolumeLevelScalar = v;
    }

    public void Dispose()
    {
        foreach (var device in _devices.Values)
        {
            device.Dispose();
        }
        _enumerator.Dispose();
    }
}
