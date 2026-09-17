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
    private const string ApiUrl = "https://api.github.com/repos/bilibilibaiyun/BaiYunGe/releases";

    private readonly HttpClient _httpClient;

    public UpdateChecker()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BaiYunGe-Updater");
    }

    /// <summary>
    /// 检查 GitHub 最新「稳定版」release（标题含「稳定版」）：版本对比 + 更新日志 + 下载地址。
    /// 只检查稳定版，测试版不参与自动更新提示（选择版本功能不受此限制）。
    /// 网络失败/无稳定版时返回 null（或 HasUpdate=false），绝不抛异常影响主流程。
    /// </summary>
    public async Task<UpdateInfo?> CheckAsync(string currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(ApiUrl + "?per_page=30", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            // release 列表按发布时间倒序，取第一个「稳定版」（标题含「稳定版」）。
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var name = release.GetProperty("name").GetString() ?? string.Empty;
                if (!name.Contains("稳定版", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tagName = release.GetProperty("tag_name").GetString() ?? string.Empty;
                var latestVersion = tagName.TrimStart('v', 'V');
                var notes = release.GetProperty("body").GetString() ?? string.Empty;
                var downloadUrl = ResolveDownloadUrl(release);
                var hasUpdate = CompareVersions(latestVersion, currentVersion) > 0;
                return new UpdateInfo(hasUpdate, latestVersion, currentVersion, notes, downloadUrl);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按架构解析 release 的安装包下载地址（x64 找标准版、arm64 找含 _arm64 的）。</summary>
    private static string ResolveDownloadUrl(JsonElement release)
    {
        var isArm64 = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                      System.Runtime.InteropServices.Architecture.Arm64;
        if (release.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var isArm64Asset = name.Contains("_arm64", StringComparison.OrdinalIgnoreCase);
                if (isArm64 == isArm64Asset)
                {
                    return asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                }
            }

            // 兜底：无匹配架构的安装包时，退回任意 .exe。
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? string.Empty;
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    return asset.GetProperty("browser_download_url").GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
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
}
