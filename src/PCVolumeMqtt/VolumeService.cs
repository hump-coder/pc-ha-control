using NAudio.CoreAudioApi;

namespace PCVolumeMqtt;

public record DeviceInfo(string Id, string Name);

public record VolumeChangedEventArgs(float Volume);

public class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private readonly MMDevice _device;

    public event EventHandler<VolumeChangedEventArgs>? VolumeChanged;

    public VolumeService()
    {
        _enumerator = new MMDeviceEnumerator();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _device.AudioEndpointVolume.OnVolumeNotification += data =>
        {
            VolumeChanged?.Invoke(this, new VolumeChangedEventArgs(data.MasterVolume * 100f));
        };
    }

    public DeviceInfo GetDevice() => new(_device.ID, _device.FriendlyName);

    public float GetVolume() => _device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;

    public void SetVolume(float volume)
    {
        var v = Math.Clamp(volume, 0f, 100f) / 100f;
        _device.AudioEndpointVolume.MasterVolumeLevelScalar = v;
    }

    public void Dispose()
    {
        _device.Dispose();
        _enumerator.Dispose();
    }
}

