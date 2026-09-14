using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace BaiYunGe.Core.Inference;

public sealed record ModelDownloadProgress(
    string FileName,
    long ReceivedBytes,
    long TotalBytes,
    string Source,
    string? Error);

public sealed record ModelFileCheck(string FileName, bool IsValid, long Size);

public sealed record ModelDownloadSummary(bool IsComplete, IReadOnlyList<ModelFileCheck> Files);

/// <summary>
/// 模型下载：白名单镜像（hf-mirror → huggingface 官方）、断点续传、大小 + SHA-256
/// 校验、原子替换。每个源重试 3 次（应对大文件下载途中网络中断），详细日志。
/// 全程 ConfigureAwait(false) 避免阻塞 UI。
/// </summary>
public sealed class ModelDownloader : IDisposable
{
    private const int MaxAttemptsPerSource = 3;

    private static readonly IReadOnlyList<ModelFileManifest> Manifest = new[]
    {
        new ModelFileManifest(
            "Qwen3-ASR-1.7B-Q8_0.gguf",
            2165034944L,
            "58E22D0532D4EACAF034CFAC17A6FED159F37C41390C710186783BE439D1FC57",
            new[]
            {
                "https://hf-mirror.com/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/Qwen3-ASR-1.7B-Q8_0.gguf",
                "https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/Qwen3-ASR-1.7B-Q8_0.gguf"
            }),
        new ModelFileManifest(
            "mmproj-Qwen3-ASR-1.7B-Q8_0.gguf",
            355709344L,
            "46C1D533AF3F354CEB37CE855DBCEFF7DA7FA7CF1E6A523DF3B13440BD164C0D",
            new[]
            {
                "https://hf-mirror.com/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/mmproj-Qwen3-ASR-1.7B-Q8_0.gguf",
                "https://huggingface.co/ggml-org/Qwen3-ASR-1.7B-GGUF/resolve/main/mmproj-Qwen3-ASR-1.7B-Q8_0.gguf"
            })
    };

    private readonly HttpClient _httpClient = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly AppLogger? _logger;
    private bool _disposed;

    public ModelDownloader(AppLogger? logger = null)
    {
        _logger = logger;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("BaiYunGe/2.0");
    }

    public event EventHandler<ModelDownloadProgress>? ProgressChanged;

    public async Task<ModelDownloadSummary> DownloadAsync(
        string destinationDirectory,
        Func<Task>? beforeFileReplaceAsync = null,
        IProgress<ModelDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        Directory.CreateDirectory(destinationDirectory);

        foreach (var file in Manifest)
        {
            await DownloadFileAsync(file, destinationDirectory, beforeFileReplaceAsync, progress, cancellationToken)
                .ConfigureAwait(false);
        }

        return await VerifyAsync(destinationDirectory, cancellationToken).ConfigureAwait(false);
    }

    public Task<ModelDownloadSummary> QuickVerifyAsync(string directory, CancellationToken cancellationToken = default)
    {
        var checks = Manifest.Select(file =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, file.Name);
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            return new ModelFileCheck(file.Name, File.Exists(path) && size == file.ExpectedSize, size);
        }).ToArray();

        return Task.FromResult(new ModelDownloadSummary(checks.All(c => c.IsValid), checks));
    }

    public async Task<ModelDownloadSummary> VerifyAsync(string directory, CancellationToken cancellationToken = default)
    {
        var checks = new List<ModelFileCheck>();
        foreach (var file in Manifest)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = Path.Combine(directory, file.Name);
            var size = File.Exists(path) ? new FileInfo(path).Length : 0;
            var valid = File.Exists(path) &&
                        size == file.ExpectedSize &&
                        await HashMatchesAsync(path, file.ExpectedSha256, cancellationToken).ConfigureAwait(false);
            checks.Add(new ModelFileCheck(file.Name, valid, size));
        }

        return new ModelDownloadSummary(checks.All(c => c.IsValid), checks);
    }

    private async Task DownloadFileAsync(
        ModelFileManifest file,
        string directory,
        Func<Task>? beforeFileReplaceAsync,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var finalPath = Path.Combine(directory, file.Name);
        var partialPath = finalPath + ".part";

        // 已完整安装：跳过。
        if (File.Exists(finalPath) &&
            new FileInfo(finalPath).Length == file.ExpectedSize &&
            await HashMatchesAsync(finalPath, file.ExpectedSha256, cancellationToken).ConfigureAwait(false))
        {
            Report(file.Name, file.ExpectedSize, file.ExpectedSize, "installed", progress);
            return;
        }

        // 已有完整 .part：直接原子替换。
        if (!File.Exists(finalPath) &&
            File.Exists(partialPath) &&
            new FileInfo(partialPath).Length == file.ExpectedSize &&
            await HashMatchesAsync(partialPath, file.ExpectedSha256, cancellationToken).ConfigureAwait(false))
        {
            await ReplaceReadyFileAsync(partialPath, finalPath, file, beforeFileReplaceAsync, progress, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        foreach (var source in file.Sources)
        {
            for (var attempt = 1; attempt <= MaxAttemptsPerSource; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var readyForMove = false;
                try
                {
                    await DownloadOnceAsync(file, source, partialPath, finalPath, beforeFileReplaceAsync, progress, cancellationToken)
                        .ConfigureAwait(false);
                    readyForMove = true;
                    return;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception) when (!readyForMove)
                {
                    _logger?.Warn(
                        $"Download {file.Name} from {source} failed (attempt {attempt}/{MaxAttemptsPerSource}): {exception.Message}");
                    progress?.Report(new ModelDownloadProgress(
                        file.Name,
                        File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0,
                        file.ExpectedSize,
                        source,
                        exception.Message));

                    if (attempt < MaxAttemptsPerSource)
                    {
                        await Task.Delay(1000 * attempt, cancellationToken).ConfigureAwait(false);
                    }
                }
            }
        }

        throw new InvalidOperationException($"All download sources failed for {file.Name}.");
    }

    private async Task DownloadOnceAsync(
        ModelFileManifest file,
        string source,
        string partialPath,
        string finalPath,
        Func<Task>? beforeFileReplaceAsync,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        var existingLength = File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0L;
        if (existingLength >= file.ExpectedSize)
        {
            File.Delete(partialPath);
            existingLength = 0;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, source);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, file.ExpectedSize - 1);
        }

        using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);

        if (existingLength > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
        {
            File.Delete(partialPath);
            throw new HttpRequestException($"Range not satisfiable (source may have changed): {source}");
        }

        if (existingLength > 0 && response.StatusCode == HttpStatusCode.OK)
        {
            // 服务器忽略 Range，返回完整文件：从头重下。
            File.Delete(partialPath);
            existingLength = 0;
        }

        if (existingLength == 0 && response.StatusCode != HttpStatusCode.OK)
        {
            throw new HttpRequestException($"Download source returned {(int)response.StatusCode}: {source}");
        }

        if (existingLength > 0 && response.StatusCode != HttpStatusCode.PartialContent)
        {
            throw new HttpRequestException($"Resume source returned {(int)response.StatusCode}: {source}");
        }

        await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
        await using (var target = new FileStream(
                         partialPath,
                         FileMode.Append,
                         FileAccess.Write,
                         FileShare.Read,
                         1024 * 1024,
                         useAsync: true))
        {
            var buffer = new byte[1024 * 1024];
            var total = existingLength;
            int read;
            while ((read = await sourceStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                total += read;
                Report(file.Name, total, file.ExpectedSize, source, progress);
            }

            await target.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (new FileInfo(partialPath).Length != file.ExpectedSize)
        {
            throw new InvalidDataException($"Downloaded size mismatch for {file.Name}.");
        }

        if (!await HashMatchesAsync(partialPath, file.ExpectedSha256, cancellationToken).ConfigureAwait(false))
        {
            File.Delete(partialPath);
            throw new InvalidDataException($"SHA-256 mismatch for {file.Name}.");
        }

        await ReplaceReadyFileAsync(partialPath, finalPath, file, beforeFileReplaceAsync, progress, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task ReplaceReadyFileAsync(
        string partialPath,
        string finalPath,
        ModelFileManifest file,
        Func<Task>? beforeFileReplaceAsync,
        IProgress<ModelDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        try
        {
            if (beforeFileReplaceAsync is not null)
            {
                await beforeFileReplaceAsync().ConfigureAwait(false);
            }

            File.Move(partialPath, finalPath, true);
        }
        catch (IOException)
        {
            if (new FileInfo(partialPath).Length != file.ExpectedSize)
            {
                throw;
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
            if (beforeFileReplaceAsync is not null)
            {
                await beforeFileReplaceAsync().ConfigureAwait(false);
            }

            File.Move(partialPath, finalPath, true);
        }

        progress?.Report(new ModelDownloadProgress(file.Name, file.ExpectedSize, file.ExpectedSize, "completed", null));
    }

    private static async Task<bool> HashMatchesAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    private void Report(string fileName, long received, long total, string source, IProgress<ModelDownloadProgress>? progress)
    {
        progress?.Report(new ModelDownloadProgress(fileName, received, total, source, null));
        ProgressChanged?.Invoke(this, new ModelDownloadProgress(fileName, received, total, source, null));
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _httpClient.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(ModelDownloader));
        }
    }

    private sealed record ModelFileManifest(
        string Name,
        long ExpectedSize,
        string ExpectedSha256,
        IReadOnlyList<string> Sources);
}
