$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Net.Http

$engine = 'D:\Codex Work\BaiYunGe\engine\llama-server\llama-server.exe'
$model = 'D:\BYG\Qwen3-ASR-1.7B-Q8_0.gguf'
$mmproj = 'D:\BYG\mmproj-Qwen3-ASR-1.7B-Q8_0.gguf'
$audio = 'D:\Codex Work\BaiYunGe\testdata\zh_test.wav'
$port = 18027
$apiKey = 'baiyunge-smoke'
$stdout = 'D:\Codex Work\BaiYunGe\testdata\llama-server-smoke.out.log'
$stderr = 'D:\Codex Work\BaiYunGe\testdata\llama-server-smoke.err.log'

foreach ($path in @($engine, $model, $mmproj, $audio)) {
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Missing required file: $path"
    }
}

$arguments = @(
    '--model', $model,
    '--mmproj', $mmproj,
    '--host', '127.0.0.1',
    '--port', "$port",
    '--no-webui',
    '--api-key', $apiKey,
    '--gpu-layers', 'all',
    '--parallel', '1',
    '--ctx-size', '4096',
    '--temp', '0'
)

$process = Start-Process -FilePath $engine `
    -ArgumentList $arguments `
    -WorkingDirectory (Split-Path -Parent $engine) `
    -WindowStyle Hidden `
    -RedirectStandardOutput $stdout `
    -RedirectStandardError $stderr `
    -PassThru

try {
    $deadline = (Get-Date).AddMinutes(2)
    $ready = $false
    while ((Get-Date) -lt $deadline) {
        if ($process.HasExited) {
            throw "llama-server exited with code $($process.ExitCode)"
        }

        try {
            $health = Invoke-WebRequest `
                -Uri "http://127.0.0.1:$port/health" `
                -Headers @{ Authorization = "Bearer $apiKey" } `
                -UseBasicParsing `
                -TimeoutSec 2
            if ($health.StatusCode -eq 200) {
                $ready = $true
                break
            }
        }
        catch {
            Start-Sleep -Milliseconds 400
        }
    }

    if (-not $ready) {
        throw 'llama-server did not become healthy in time.'
    }

    $client = [System.Net.Http.HttpClient]::new()
    $client.Timeout = [TimeSpan]::FromMinutes(2)
    try {
        $request = [System.Net.Http.HttpRequestMessage]::new(
            [System.Net.Http.HttpMethod]::Post,
            "http://127.0.0.1:$port/v1/audio/transcriptions")
        $request.Headers.Authorization =
            [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $apiKey)

        $multipart = [System.Net.Http.MultipartFormDataContent]::new()
        $fileStream = [System.IO.FileStream]::new(
            $audio,
            [System.IO.FileMode]::Open,
            [System.IO.FileAccess]::Read,
            [System.IO.FileShare]::ReadWrite)
        try {
            $fileContent = [System.Net.Http.StreamContent]::new($fileStream)
            $fileContent.Headers.ContentType =
                [System.Net.Http.Headers.MediaTypeHeaderValue]::new('audio/wav')
            $multipart.Add($fileContent, 'file', 'zh_test.wav')
            $multipart.Add([System.Net.Http.StringContent]::new('json'), 'response_format')
            $multipart.Add([System.Net.Http.StringContent]::new('zh'), 'language')
            $request.Content = $multipart

            $response = $client.SendAsync($request).GetAwaiter().GetResult()
            $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
            if (-not $response.IsSuccessStatusCode) {
                throw "Transcription failed: $($response.StatusCode) $body"
            }

            Write-Output $body
        }
        finally {
            $fileStream.Dispose()
            $multipart.Dispose()
            $request.Dispose()
        }
    }
    finally {
        $client.Dispose()
    }
}
finally {
    if (-not $process.HasExited) {
        Stop-Process -Id $process.Id -Force
        $process.WaitForExit()
    }
    $process.Dispose()
}
