$ErrorActionPreference = 'Stop'

$msi = '"D:\Codex Work\BaiYunGe\artifacts\BaiYunGe_1.0.1_x64.msi"'
$log = '"D:\Codex Work\BaiYunGe\artifacts\msi-install.log"'
$installFolder = '"D:\BaiYunGe-MSITest"'

$existingInstaller = Get-CimInstance Win32_Process -Filter "Name='msiexec.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine -notmatch '(?i)(^|\s)/V(\s|$)' }
if ($existingInstaller) {
    throw 'Another Windows Installer process is already running.'
}

$arguments = @(
    '/i',
    $msi,
    '/qn',
    '/norestart',
    '/l*v',
    $log,
    "INSTALLFOLDER=$installFolder"
)

$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru
Write-Output "EXIT=$($process.ExitCode)"

Get-ChildItem -LiteralPath 'D:\BaiYunGe-MSITest' -Force -ErrorAction SilentlyContinue |
    Select-Object Mode, Length, Name |
    Format-Table -AutoSize
