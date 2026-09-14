$ErrorActionPreference = 'Stop'

$projectRoot = 'D:\Codex Work\BaiYunGe'
$dotnetRoot = 'D:\Codex Work\.tools\dotnet'
$wix = 'D:\Codex Work\.tools\wix\wix.exe'
$artifactsDir = Join-Path $projectRoot 'artifacts'
$publishDir = Join-Path $artifactsDir 'publish'
$bootstrapperPublishDir = Join-Path $artifactsDir 'bootstrapper-publish'
$msiZh = Join-Path $artifactsDir 'BaiYunGe_1.0.2_zh-CN_x64.msi'
$msiEn = Join-Path $artifactsDir 'BaiYunGe_1.0.2_en-US_x64.msi'
$bundle = Join-Path $artifactsDir 'BaiYunGe_1.0.2_x64.exe'
$balExtension = Join-Path $projectRoot '.wix\extensions\WixToolset.Bal.wixext\5.0.2\wixext5\WixToolset.BootstrapperApplications.wixext.dll'

$env:DOTNET_ROOT = $dotnetRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-cli-home'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:PATH = "$dotnetRoot;$env:PATH"

function Remove-ArtifactDirectory {
    param(
        [Parameter(Mandatory)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $resolved = [System.IO.Path]::GetFullPath($Path)
    $artifactsRoot = [System.IO.Path]::GetFullPath($artifactsDir)
    if (-not $resolved.StartsWith(
        $artifactsRoot,
        [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove unexpected artifact directory: $resolved"
    }

    Remove-Item -LiteralPath $resolved -Recurse -Force
}

Get-Process -Name BaiYunGe -ErrorAction SilentlyContinue |
    Stop-Process -Force

Remove-ArtifactDirectory -Path $publishDir
Remove-ArtifactDirectory -Path $bootstrapperPublishDir

& "$dotnetRoot\dotnet.exe" test (Join-Path $projectRoot 'BaiYunGe.sln') `
    -c Release `
    --logger 'console;verbosity=minimal'
if ($LASTEXITCODE -ne 0) {
    throw "dotnet test failed with exit code $LASTEXITCODE."
}

& "$dotnetRoot\dotnet.exe" publish (Join-Path $projectRoot 'src\BaiYunGe.App\BaiYunGe.App.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "BaiYunGe application publish failed with exit code $LASTEXITCODE."
}

& "$dotnetRoot\dotnet.exe" publish (Join-Path $projectRoot 'installer\BaiYunGe.Bootstrapper\BaiYunGe.Bootstrapper.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -o $bootstrapperPublishDir
if ($LASTEXITCODE -ne 0) {
    throw "Bootstrapper publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $publishDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'THIRD_PARTY_NOTICES.md') -Destination $publishDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publishDir -Force

foreach ($file in @($msiZh, $msiEn, $bundle)) {
    if (Test-Path -LiteralPath $file) {
        Remove-Item -LiteralPath $file -Force
    }
}

& $wix extension add 'WixToolset.UI.wixext/5.0.2'
if ($LASTEXITCODE -ne 0) {
    throw "WiX UI extension installation failed with exit code $LASTEXITCODE."
}

& $wix extension add 'WixToolset.Bal.wixext/5.0.2'
if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1) {
    throw "WiX Bal extension installation failed with exit code $LASTEXITCODE."
}

& $wix build (Join-Path $projectRoot 'installer\baiyunge.wxs') `
    -o $msiZh `
    -arch x64 `
    -culture zh-CN `
    -loc (Join-Path $projectRoot 'installer\localization\zh-CN.wxl') `
    -ext WixToolset.UI.wixext
if ($LASTEXITCODE -ne 0) {
    throw "Chinese MSI build failed with exit code $LASTEXITCODE."
}

& $wix build (Join-Path $projectRoot 'installer\baiyunge.wxs') `
    -o $msiEn `
    -arch x64 `
    -culture en-US `
    -loc (Join-Path $projectRoot 'installer\localization\en-US.wxl') `
    -ext WixToolset.UI.wixext
if ($LASTEXITCODE -ne 0) {
    throw "English MSI build failed with exit code $LASTEXITCODE."
}

& $wix msi validate $msiZh
if ($LASTEXITCODE -ne 0) {
    throw "Chinese MSI validation failed with exit code $LASTEXITCODE."
}

& $wix msi validate $msiEn
if ($LASTEXITCODE -ne 0) {
    throw "English MSI validation failed with exit code $LASTEXITCODE."
}

& $wix build (Join-Path $projectRoot 'installer\baiyunge-bundle.wxs') `
    -o $bundle `
    -arch x64 `
    -ext $balExtension
if ($LASTEXITCODE -ne 0) {
    throw "Burn bundle build failed with exit code $LASTEXITCODE."
}

$bundleFile = Get-Item -LiteralPath $bundle
$bundleHash = Get-FileHash -LiteralPath $bundle -Algorithm SHA256
$msiZhHash = Get-FileHash -LiteralPath $msiZh -Algorithm SHA256
$msiEnHash = Get-FileHash -LiteralPath $msiEn -Algorithm SHA256

[pscustomobject]@{
    Bundle = $bundleFile.FullName
    BundleBytes = $bundleFile.Length
    BundleSHA256 = $bundleHash.Hash
    MsiZh = $msiZh
    MsiZhSHA256 = $msiZhHash.Hash
    MsiEn = $msiEn
    MsiEnSHA256 = $msiEnHash.Hash
} | Format-List
