using System.Reflection;

namespace BaiYunGe.Core;

/// <summary>
/// 统一管理运行期所有路径，落实「不占 C 盘」约束：
/// 优先落在 D 盘用户目录，无可用 D 盘时回退到程序目录下的 .data。
/// </summary>
public sealed class AppPaths
{
    private readonly string _dataRoot;

    public AppPaths(string? overrideDataDir = null)
    {
        _dataRoot = ResolveDataRoot(overrideDataDir);
        Logs = Path.Combine(_dataRoot, "logs");
        Temp = Path.Combine(_dataRoot, "temp");
        ConfigFile = Path.Combine(_dataRoot, "config.json");
        DefaultModels = Path.Combine(_dataRoot, "Models");
    }

    /// <summary>日志目录。</summary>
    public string Logs { get; }

    /// <summary>临时音频目录（录音 WAV 等）。</summary>
    public string Temp { get; }

    /// <summary>配置文件完整路径。</summary>
    public string ConfigFile { get; }

    /// <summary>默认模型目录（用户可改）。</summary>
    public string DefaultModels { get; }

    /// <summary>程序目录（exe 所在）。</summary>
    public static string AppDirectory =>
        Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)
        ?? AppContext.BaseDirectory;

    /// <summary>内置 llama-server 引擎目录。</summary>
    public static string EngineDirectory => Path.Combine(AppDirectory, "engine", "llama-server");

    /// <summary>默认模型安装目录：软件安装路径下的 models 子目录。</summary>
    public static string DefaultModelDirectory => Path.Combine(AppDirectory, "models");

    /// <summary>生成一个唯一的临时 WAV 路径。</summary>
    public string CreateTempWavPath()
    {
        Directory.CreateDirectory(Temp);
        var stamp = DateTime.Now.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        return Path.Combine(Temp, $"capture-{stamp}-{Guid.NewGuid():N}.wav");
    }

    /// <summary>确保运行所需的子目录都存在。</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Temp);
    }

    private static string ResolveDataRoot(string? overrideDataDir)
    {
        if (!string.IsNullOrWhiteSpace(overrideDataDir))
        {
            return Path.GetFullPath(overrideDataDir);
        }

        // 优先 D 盘用户目录，避免写 C 盘。
        var userRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userRoot) && userRoot.Length >= 2 && userRoot[1] == ':')
        {
            var drive = char.ToUpperInvariant(userRoot[0]);
            if (drive != 'C')
            {
                return Path.Combine(userRoot, "BaiYunGe");
            }
        }

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.IsReady && drive.DriveType == DriveType.Fixed &&
                drive.Name.StartsWith("D:", StringComparison.OrdinalIgnoreCase))
            {
                return Path.Combine(drive.Name, "Users", Environment.UserName, "BaiYunGe");
            }
        }

        // 无可用 D 盘：回退 %LOCALAPPDATA%\BaiYunGe（标准用户数据目录，任何权限下都可写，
        // 避免安装到 Program Files 后写入失败）。最终兜底程序目录 .data。
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            return Path.Combine(localAppData, "BaiYunGe");
        }

        return Path.Combine(AppDirectory, ".data");
    }
}
