$ErrorActionPreference = 'Stop'

$msi = '"D:\Codex Work\BaiYunGe\artifacts\BaiYunGe_1.0.1_x64.msi"'
$log = '"D:\Codex Work\BaiYunGe\artifacts\msi-uninstall.log"'

Get-Process -Name BaiYunGe -ErrorAction SilentlyContinue | Stop-Process -Force

$arguments = @(
    '/x',
    $msi,
    '/qn',
    '/norestart',
    '/l*v',
    $log
)

$process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $arguments -Wait -PassThru
Write-Output "EXIT=$($process.ExitCode)"
Write-Output "INSTALL_DIR_EXISTS=$(Test-Path -LiteralPath 'D:\BaiYunGe-MSITest')"
