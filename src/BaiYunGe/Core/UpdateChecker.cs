using System.Net.Http;
using System.Text.Json;

namespace BaiYunGe.Core;

public sealed record UpdateInfo(
    bool HasUpdate,
    string LatestVersion,
    string CurrentVersion,
    string ReleaseNotes,
    string DownloadUrl);

/// <summary>
/// 检查 GitHub 最新 release：版本对比 + 更新日志 + 安装包下载地址。
/// 网络失败/无新版本时返回 null（或 HasUpdate=false），绝不抛异常影响主流程。
/// </summary>
public sealed class UpdateChecker
{
    private const string ApiUrl = "https://api.github.com/repos/bilibilibaiyun/BaiYunGe/releases/latest";

    private readonly HttpClient _httpClient;

    public UpdateChecker()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BaiYunGe-Updater");
    }

    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(ApiUrl, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var tagName = root.GetProperty("tag_name").GetString() ?? string.Empty;
            var latestVersion = tagName.TrimStart('v', 'V');
            var notes = root.GetProperty("body").GetString() ?? string.Empty;

            // 下载地址：根据当前架构匹配对应的安装包（x64 只更新 x64，arm64 只更新 arm64）。
            var archSuffix = GetCurrentArchSuffix();
            var downloadUrl = string.Empty;
            if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
            {
                // 优先匹配当前架构的安装包（如 _x64_Setup.exe / _arm64_Setup.exe）。
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.EndsWith(archSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                        break;
                    }
                }

                // 兜底：无匹配架构的安装包时，退回任意 .exe。
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var name = asset.GetProperty("name").GetString() ?? string.Empty;
                        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                            break;
                        }
                    }
                }
            }

            var hasUpdate = CompareVersions(latestVersion, currentVersion) > 0;
            return new UpdateInfo(hasUpdate, latestVersion, currentVersion, notes, downloadUrl);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按点分版本号逐段比较，a>b 返回正、a&lt;b 返回负、相等返回 0。</summary>
    private static int CompareVersions(string a, string b)
    {
        var pa = a.Split('.');
        var pb = b.Split('.');
        var len = Math.Max(pa.Length, pb.Length);
        for (var i = 0; i < len; i++)
        {
            var na = i < pa.Length && int.TryParse(pa[i], out var va) ? va : 0;
            var nb = i < pb.Length && int.TryParse(pb[i], out var vb) ? vb : 0;
            if (na != nb)
            {
                return na.CompareTo(nb);
            }
        }

        return 0;
    }

    /// <summary>返回当前进程架构对应的安装包文件后缀（用于从 release 资产中匹配正确架构）。</summary>
    private static string GetCurrentArchSuffix()
    {
        return System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
               System.Runtime.InteropServices.Architecture.Arm64
            ? "_arm64_Setup.exe"
            : "_x64_Setup.exe";
    }
}
