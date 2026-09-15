using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace BaiYunGe.Core.Inference;

/// <summary>
/// llama-server 子进程管理：动态端口、Vulkan GPU 优先 + CPU 回退、健康检查、
/// 看门狗重启、Job Object 终止、HTTP multipart 转写。
/// 只监听 127.0.0.1，不开放局域网。
/// </summary>
public sealed class LlamaServerManager : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(2);

    /// <summary>转写多少次后主动重启 llama-server，释放长期运行累积的显存/内存碎片，避免识别渐慢。</summary>
    private const int RestartAfterTranscripts = 50;

    /// <summary>启动失败诊断用的最近输出环形缓冲区容量（stdout/stderr 合并缓存）。</summary>
    private const int MaxRecentOutputLines = 50;

    private readonly AppLogger _logger;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _startupLock = new(1, 1);
    private readonly ChildProcessJob? _job;

    private readonly object _outputLock = new();
    private readonly Queue<string> _recentOutput = new();

    private Process? _process;
    private HttpClient? _httpClient;
    private Uri? _baseUri;
    private string _apiKey = string.Empty;
    private bool _usingGpu;
    private bool _gpuFallbackLocked;
    private bool _disposed;

    private string _loadedModelDirectory = string.Empty;
    private string _loadedDevice = string.Empty;
    private int _transcriptionCount;

    public LlamaServerManager(AppLogger logger)
    {
        _logger = logger;
        try
        {
            _job = new ChildProcessJob();
        }
        catch (Exception exception)
        {
            _logger.Warn($"Child process job creation failed: {exception.Message}");
        }
    }

    public event EventHandler<string>? ServerLog;

    public bool IsHealthy { get; private set; }

    public bool IsUsingGpu => _usingGpu;

    public async Task EnsureStartedAsync(
        string modelDirectory,
        string inferenceDevice,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        await _startupLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (_sync)
            {
                if (_process is { HasExited: false } &&
                    IsHealthy &&
                    _httpClient is not null &&
                    string.Equals(_loadedModelDirectory, modelDirectory, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(_loadedDevice, inferenceDevice, StringComparison.OrdinalIgnoreCase) &&
                    _transcriptionCount < RestartAfterTranscripts)
                {
                    return;
                }
            }

            await StopCoreAsync().ConfigureAwait(false);

            var models = ResolveModelFiles(modelDirectory);
            var useGpu = inferenceDevice.Equals("gpu", StringComparison.OrdinalIgnoreCase) ||
                         (inferenceDevice.Equals("auto", StringComparison.OrdinalIgnoreCase) && !_gpuFallbackLocked);
            _loadedModelDirectory = modelDirectory;
            _loadedDevice = inferenceDevice;

            try
            {
                await StartCoreAsync(models.MainModel, models.Mmproj, useGpu, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                throw;
            }
            catch when (useGpu)
            {
                _logger.Warn("GPU llama-server start failed; retrying with CPU.");
                _gpuFallbackLocked = true;
                await StopCoreAsync().ConfigureAwait(false);
                await StartCoreAsync(models.MainModel, models.Mmproj, false, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _startupLock.Release();
        }
    }

    public async Task<string> TranscribeAsync(
        string wavPath,
        string language,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var client = GetHttpClient();
        var baseUri = _baseUri ?? throw new InvalidOperationException("llama-server is not initialized.");
        var stopwatch = Stopwatch.StartNew();

        using var fileStream = new FileStream(
            wavPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        using var content = new MultipartFormDataContent();
        var fileContent = new StreamContent(fileStream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        content.Add(fileContent, "file", Path.GetFileName(wavPath));
        content.Add(new StringContent("json"), "response_format");

        if (!string.IsNullOrWhiteSpace(language))
        {
            content.Add(new StringContent(language), "language");
        }

        if (!string.IsNullOrWhiteSpace(prompt))
        {
            content.Add(new StringContent(prompt), "prompt");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(baseUri, "/v1/audio/transcriptions"))
        {
            Content = content
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseContentRead, timeout.Token)
            .ConfigureAwait(false);

        var responseText = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"llama-server returned {(int)response.StatusCode}: {Truncate(responseText, 2000)}");
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.TryGetProperty("text", out var textElement))
            {
                var text = textElement.GetString() ?? string.Empty;
                _transcriptionCount++;
                stopwatch.Stop();
                _logger.Debug($"Transcription #{_transcriptionCount} took {stopwatch.ElapsedMilliseconds} ms.");
                _logger.Debug($"llama-server transcript: {Truncate(text, 2000)}");
                return text;
            }
        }
        catch (JsonException exception)
        {
            _logger.Warn($"llama-server response was not JSON: {exception.Message}");
        }

        _transcriptionCount++;
        stopwatch.Stop();
        _logger.Debug($"Transcription #{_transcriptionCount} took {stopwatch.ElapsedMilliseconds} ms.");
        _logger.Debug($"llama-server transcript (raw): {Truncate(responseText, 2000)}");
        return responseText;
    }

    public async Task StopAsync()
    {
        await _startupLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _startupLock.Release();
        }
    }

    private async Task StartCoreAsync(
        string mainModel,
        string mmproj,
        bool useGpu,
        CancellationToken cancellationToken)
    {
        var engineDirectory = AppPaths.EngineDirectory;
        var executable = Path.Combine(engineDirectory, "llama-server.exe");
        if (!File.Exists(executable))
        {
            throw new FileNotFoundException($"llama-server.exe was not found at {executable}", executable);
        }

        var port = ReserveLoopbackPort();
        _apiKey = Guid.NewGuid().ToString("N");
        _usingGpu = useGpu;
        _baseUri = new Uri($"http://127.0.0.1:{port}");

        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = engineDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.ArgumentList.Add("--model");
        startInfo.ArgumentList.Add(mainModel);
        startInfo.ArgumentList.Add("--mmproj");
        startInfo.ArgumentList.Add(mmproj);
        startInfo.ArgumentList.Add("--host");
        startInfo.ArgumentList.Add("127.0.0.1");
        startInfo.ArgumentList.Add("--port");
        startInfo.ArgumentList.Add(port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--no-webui");
        startInfo.ArgumentList.Add("--api-key");
        startInfo.ArgumentList.Add(_apiKey);
        startInfo.ArgumentList.Add("--gpu-layers");
        startInfo.ArgumentList.Add(useGpu ? "all" : "0");
        startInfo.ArgumentList.Add("--parallel");
        startInfo.ArgumentList.Add("1");
        startInfo.ArgumentList.Add("--ctx-size");
        startInfo.ArgumentList.Add("4096");
        startInfo.ArgumentList.Add("--temp");
        startInfo.ArgumentList.Add("0");
        startInfo.ArgumentList.Add("--threads");
        startInfo.ArgumentList.Add(Math.Max(1, Environment.ProcessorCount).ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--threads-batch");
        startInfo.ArgumentList.Add(Math.Max(1, Environment.ProcessorCount).ToString(System.Globalization.CultureInfo.InvariantCulture));

        // GPU 模式下显式选择最强设备（优先 NVIDIA/AMD 独显，回退第一个可用设备），
        // 避免多 GPU 机器上 llama.cpp 默认选中核显导致推理变慢。
        if (useGpu)
        {
            var device = DetectBestGpuDevice(executable);
            if (!string.IsNullOrEmpty(device))
            {
                startInfo.ArgumentList.Add("--device");
                startInfo.ArgumentList.Add(device);
                _logger.Info($"Selected GPU device: {device}");
            }
        }

        var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.OutputDataReceived += (_, e) => OnServerOutput(e.Data);
        process.ErrorDataReceived += (_, e) => OnServerOutput(e.Data);
        process.Exited += (_, _) =>
        {
            IsHealthy = false;
        };

        _logger.Info($"Starting llama-server GPU={useGpu} port={port} model={Path.GetFileName(mainModel)}");
        if (!process.Start())
        {
            throw new InvalidOperationException("llama-server process could not be started.");
        }

        lock (_outputLock)
        {
            _recentOutput.Clear();
        }

        lock (_sync)
        {
            _process = process;
            _httpClient?.Dispose();
            _httpClient = new HttpClient
            {
                BaseAddress = _baseUri,
                Timeout = Timeout.InfiniteTimeSpan
            };
        }

        try
        {
            _job?.AddProcess(process);
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to assign llama-server to job: {exception.Message}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await WaitForHealthAsync(process, cancellationToken).ConfigureAwait(false);
            // 新进程就绪，重置转写计数（用于定期重启避免长期运行性能退化）。
            _transcriptionCount = 0;
        }
        catch
        {
            await StopCoreAsync().ConfigureAwait(false);
            throw;
        }
    }

    private void OnServerOutput(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        ServerLog?.Invoke(this, line);
        _logger.Debug($"[llama-server] {line}");

        lock (_outputLock)
        {
            _recentOutput.Enqueue(line);
            while (_recentOutput.Count > MaxRecentOutputLines)
            {
                _recentOutput.Dequeue();
            }
        }
    }

    /// <summary>返回当前 llama-server 进程最近的 stdout/stderr 输出（供启动失败诊断）。</summary>
    private string GetRecentOutput()
    {
        lock (_outputLock)
        {
            return string.Join(Environment.NewLine, _recentOutput);
        }
    }

    private async Task WaitForHealthAsync(Process process, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(StartupTimeout);

        while (!cancellationToken.IsCancellationRequested)
        {
            if (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("llama-server did not become healthy within the startup timeout.");
            }

            if (process.HasExited)
            {
                var detail = GetRecentOutput();
                var suffix = string.IsNullOrWhiteSpace(detail)
                    ? string.Empty
                    : $"{Environment.NewLine}llama-server 最近输出：{Environment.NewLine}{detail}";
                throw new InvalidOperationException($"llama-server exited during startup with code {process.ExitCode}.{suffix}");
            }

            try
            {
                var client = GetHttpClient();
                using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(_baseUri!, "/health"));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
                using var response = await client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token)
                    .ConfigureAwait(false);

                if (response.IsSuccessStatusCode)
                {
                    IsHealthy = true;
                    _logger.Info($"llama-server ready (port {_baseUri!.Port}, GPU={_usingGpu}).");
                    return;
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("llama-server did not become healthy within the startup timeout.");
            }
            catch when (!timeout.IsCancellationRequested)
            {
                // 模型仍在加载。
            }

            try
            {
                await Task.Delay(400, timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                throw new TimeoutException("llama-server did not become healthy within the startup timeout.");
            }
        }

        throw new TimeoutException("llama-server did not become healthy within the startup timeout.");
    }

    private async Task StopCoreAsync()
    {
        Process? process;
        lock (_sync)
        {
            process = _process;
            _process = null;
            IsHealthy = false;
        }

        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            _logger.Warn($"Failed to stop llama-server: {exception.Message}");
        }
        finally
        {
            process.Dispose();
        }
    }

    private HttpClient GetHttpClient()
    {
        lock (_sync)
        {
            return _httpClient ?? throw new InvalidOperationException("llama-server is not initialized.");
        }
    }

    private static int ReserveLoopbackPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    /// <summary>
    /// 通过 llama-server --list-devices 探测 GPU 设备，优先独立显卡（NVIDIA/AMD），
    /// 找不到独显时回退第一个可用设备；探测失败返回 null（由 llama.cpp 默认选择）。
    /// 结果缓存，避免每次启动都重复探测。
    /// </summary>
    private static string? _cachedGpuDevice;
    private static bool _gpuDeviceProbed;

    private string? DetectBestGpuDevice(string executable)
    {
        if (_gpuDeviceProbed)
        {
            return _cachedGpuDevice;
        }

        _gpuDeviceProbed = true;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                Arguments = "--list-devices",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                // stderr 不重定向：避免写满管道导致子进程卡死；探测失败回退默认设备即可。
                RedirectStandardError = false,
                StandardOutputEncoding = System.Text.Encoding.UTF8
            };
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                _logger.Warn("GPU device probe: failed to start llama-server --list-devices.");
                return null;
            }

            // 带超时读取 stdout：5 秒内未结束即 Kill 并回退默认设备，绝不永久挂起。
            string output;
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
            {
                try
                {
                    output = process.StandardOutput.ReadToEndAsync(cts.Token).GetAwaiter().GetResult();
                }
                catch (Exception exception)
                {
                    _logger.Warn($"GPU device probe read failed or timed out: {exception.Message}");
                    TryKill(process);
                    return null;
                }
            }

            if (!process.WaitForExit(1000))
            {
                TryKill(process);
            }

            string? discrete = null;
            string? first = null;
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.Trim();
                // 行格式如 "Vulkan0: Intel(R) UHD Graphics (8075 MiB, 7415 MiB free)"
                var separator = line.IndexOf(':');
                if (separator <= 0 || !line.Contains("MiB", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var id = line[..separator].Trim();
                var name = line[(separator + 1)..].Trim();
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                first ??= id;
                var isDiscrete = name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("Radeon", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("AMD", StringComparison.OrdinalIgnoreCase) ||
                                 name.Contains("ARC", StringComparison.OrdinalIgnoreCase);
                if (isDiscrete)
                {
                    discrete ??= id;
                }
            }

            _cachedGpuDevice = discrete ?? first;
            _logger.Info($"GPU device probe result: {_cachedGpuDevice ?? "(none)"}");
            return _cachedGpuDevice;
        }
        catch (Exception exception)
        {
            _logger.Warn($"GPU device probe failed: {exception.Message}");
            return null;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // 忽略终止失败。
        }
    }

    private static (string MainModel, string Mmproj) ResolveModelFiles(string modelDirectory)
    {
        if (!Directory.Exists(modelDirectory))
        {
            throw new DirectoryNotFoundException($"Model directory was not found: {modelDirectory}");
        }

        var files = Directory.GetFiles(modelDirectory, "*.gguf", SearchOption.TopDirectoryOnly);
        var mmproj = files
            .Where(path => Path.GetFileName(path).StartsWith("mmproj", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(FileSize)
            .FirstOrDefault();
        var main = files
            .Where(path => !Path.GetFileName(path).StartsWith("mmproj", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(FileSize)
            .FirstOrDefault();

        if (main is null || mmproj is null)
        {
            throw new FileNotFoundException("Qwen3-ASR main model or mmproj file was not found.");
        }

        if (FileSize(main) < 100L * 1024 * 1024)
        {
            throw new InvalidDataException($"ASR main model is unexpectedly small: {main}");
        }

        if (FileSize(mmproj) < 10L * 1024 * 1024)
        {
            throw new InvalidDataException($"ASR mmproj model is unexpectedly small: {mmproj}");
        }

        return (main, mmproj);
    }

    private static long FileSize(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch
        {
            return 0;
        }
    }

    private static string Truncate(string value, int length) => value.Length <= length ? value : value[..length];

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        // 先同步等待在途 EnsureStartedAsync 完成：避免其 finally Release 时锁已被 Dispose
        // （抛 ObjectDisposedException），也避免其 catch-when(useGpu) 回退再拉起无 Job 保护的进程。
        _startupLock.Wait();
        try
        {
            StopCoreAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _startupLock.Release();
            _startupLock.Dispose();
        }

        _job?.Dispose();
        _httpClient?.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(LlamaServerManager));
        }
    }
}
