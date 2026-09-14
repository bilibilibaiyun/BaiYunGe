namespace BaiYunGe.Core.Audio;

public sealed record AudioDeviceInfo(
    string Id,
    string Name,
    int ChannelCount,
    int SampleRate,
    bool IsDefault);
