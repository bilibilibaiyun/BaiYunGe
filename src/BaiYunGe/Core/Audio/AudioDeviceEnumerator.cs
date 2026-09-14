using NAudio.CoreAudioApi;

namespace BaiYunGe.Core.Audio;

/// <summary>枚举 WASAPI 输入设备。</summary>
public sealed class AudioDeviceEnumerator : IDisposable
{
    private MMDeviceEnumerator? _enumerator;

    public IReadOnlyList<AudioDeviceInfo> GetCaptureDevices()
    {
        try
        {
            _enumerator ??= new MMDeviceEnumerator();
            var defaultId = GetDefaultDeviceId();
            return _enumerator
                .EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                .Select(device => ToInfo(device, defaultId))
                .ToList();
        }
        catch
        {
            return Array.Empty<AudioDeviceInfo>();
        }
    }

    /// <summary>按 Id 解析设备；未匹配时返回默认输入设备。</summary>
    public MMDevice Resolve(string? deviceId)
    {
        _enumerator ??= new MMDeviceEnumerator();

        if (!string.IsNullOrWhiteSpace(deviceId))
        {
            foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (string.Equals(device.ID, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return device;
                }
            }
        }

        return _enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
    }

    private string GetDefaultDeviceId()
    {
        try
        {
            return _enumerator?.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications).ID ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static AudioDeviceInfo ToInfo(MMDevice device, string defaultId)
    {
        return new AudioDeviceInfo(
            device.ID,
            device.FriendlyName,
            device.AudioClient.MixFormat?.Channels ?? 1,
            device.AudioClient.MixFormat?.SampleRate ?? 48000,
            string.Equals(device.ID, defaultId, StringComparison.OrdinalIgnoreCase));
    }

    public void Dispose()
    {
        _enumerator?.Dispose();
        _enumerator = null;
    }
}
