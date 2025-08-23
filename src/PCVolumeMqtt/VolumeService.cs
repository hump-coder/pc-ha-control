using NAudio.CoreAudioApi;

namespace PCVolumeMqtt;

public class VolumeService : IDisposable
{
    private readonly MMDevice _device;

    public event EventHandler<float>? VolumeChanged;

    public VolumeService()
    {
        var enumerator = new MMDeviceEnumerator();
        _device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _device.AudioEndpointVolume.OnVolumeNotification += data =>
        {
            VolumeChanged?.Invoke(this, data.MasterVolume * 100f);
        };
    }

    public float GetVolume() => _device.AudioEndpointVolume.MasterVolumeLevelScalar * 100f;

    public void SetVolume(float volume)
    {
        var v = Math.Clamp(volume, 0f, 100f) / 100f;
        _device.AudioEndpointVolume.MasterVolumeLevelScalar = v;
    }

    public void Dispose() => _device.Dispose();
}
