using System.Net.Http;
using System.Text.Json;

namespace BaiYunGe.Core;

/// <summary>一个已发布版本（用于版本回退列表）。</summary>
public sealed record ReleaseEntry(string Version, string Name, DateTime PublishedAt);

/// <summary>
/// 从 GitHub 拉取历史 release 列表，并按当前架构解析指定版本的安装包下载地址。
/// 用于「版本回退」功能。网络失败返回 null，绝不抛异常影响主流程。
/// </summary>
public sealed class ReleaseCatalog
{
    private const string ReleasesUrl = "https://api.github.com/repos/bilibilibaiyun/BaiYunGe/releases";
    private const string ReleaseByTagUrl = "https://api.github.com/repos/bilibilibaiyun/BaiYunGe/releases/tags/";

    private readonly HttpClient _httpClient;

    public ReleaseCatalog()
    {
        _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BaiYunGe-Updater");
    }

    /// <summary>拉取历史 release 列表（最多 30 个，按发布时间倒序）。</summary>
    public async Task<List<ReleaseEntry>?> GetReleasesAsync(CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(ReleasesUrl + "?per_page=30", ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var list = new List<ReleaseEntry>();
            foreach (var release in doc.RootElement.EnumerateArray())
            {
                var tag = release.GetProperty("tag_name").GetString() ?? string.Empty;
                var version = tag.TrimStart('v', 'V');
                var name = release.GetProperty("name").GetString() ?? version;
                var published = DateTime.TryParse(
                    release.GetProperty("published_at").GetString(),
                    out var parsed) ? parsed : DateTime.MinValue;
                list.Add(new ReleaseEntry(version, name, published));
            }

            return list;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>按架构解析指定版本的安装包下载地址（x64 找标准版、arm64 找含 _arm64 的）。</summary>
    public async Task<string?> GetDownloadUrlAsync(string version, CancellationToken ct = default)
    {
        try
        {
            using var response = await _httpClient.GetAsync(ReleaseByTagUrl + "v" + version, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var isArm64 = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture ==
                          System.Runtime.InteropServices.Architecture.Arm64;
            if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
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
                        return asset.GetProperty("browser_download_url").GetString();
                    }
                }

                // 兜底：无匹配架构时退回任意 .exe。
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? string.Empty;
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return asset.GetProperty("browser_download_url").GetString();
                    }
                }
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
