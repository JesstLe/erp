[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('Blue', 'Green')][string]$TargetSlot,
    [Parameter(Mandatory)][string]$InstallRoot,
    [Parameter(Mandatory)][string]$ProxyRoot,
    [string]$BlueSiteName = 'ERP-Blue',
    [string]$GreenSiteName = 'ERP-Green',
    [int]$BluePort = 5101,
    [int]$GreenPort = 5102,
    [uri]$PublicReadyUrl = 'https://localhost/health/ready'
)

. (Join-Path $PSScriptRoot 'Common.ps1')
Import-Module WebAdministration -ErrorAction Stop
$safeInstallRoot = Resolve-SafeDirectory $InstallRoot
$safeProxyRoot = Resolve-SafeDirectory $ProxyRoot
$statePath = Join-Path $safeInstallRoot 'active-slot.json'
if (-not (Test-Path -LiteralPath $statePath)) { throw '缺少当前活动版本记录' }
$current = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
if ([string]$current.slot -eq $TargetSlot) { throw '目标槽位已经是当前活动槽位' }
$currentSchemaVersion = [string]$current.schemaVersion
if ($currentSchemaVersion -notmatch '^[0-9]+$') { throw '当前活动版本记录缺少有效 schemaVersion' }
$targetSite = if ($TargetSlot -eq 'Blue') { $BlueSiteName } else { $GreenSiteName }
$targetPort = if ($TargetSlot -eq 'Blue') { $BluePort } else { $GreenPort }
$physicalPath = [System.IO.Path]::GetFullPath([string](Get-ItemProperty "IIS:\Sites\$targetSite" -Name physicalPath).physicalPath)
$releaseRoot = Resolve-SafeDirectory (Split-Path -Parent $physicalPath)
$releaseDirectory = [System.IO.Path]::GetFullPath((Join-Path $safeInstallRoot 'releases'))
if (-not $releaseRoot.StartsWith("$releaseDirectory$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::OrdinalIgnoreCase) `
    -or [System.IO.Path]::GetFileName($physicalPath) -ne 'app') {
    throw '目标 IIS 站点未指向受控 releases 版本目录'
}
$manifest = Get-ReleaseManifest -ReleaseRoot $releaseRoot
if ([System.IO.Path]::GetFileName($releaseRoot) -ne [string]$manifest.version) { throw '目标目录与发布清单版本不一致' }
$schemaNumber = [Int64]$currentSchemaVersion
if ($schemaNumber -lt [Int64]([string]$manifest.schema.min) `
    -or $schemaNumber -gt [Int64]([string]$manifest.schema.max)) {
    throw "目标应用不兼容当前数据库 schema $currentSchemaVersion，禁止回退"
}

Start-Website -Name $targetSite
Invoke-HealthGate -Uri "http://127.0.0.1:$targetPort/health/ready" -ExpectedSchemaVersion $currentSchemaVersion
$template = Get-Content -LiteralPath (Join-Path $releaseRoot 'deploy/windows/proxy-web.config.template') -Raw
$proxyPath = Join-Path $safeProxyRoot 'web.config'
$proxyExisted = Test-Path -LiteralPath $proxyPath -PathType Leaf
$previousProxyContent = if ($proxyExisted) { Get-Content -LiteralPath $proxyPath -Raw } else { $null }
try {
    Write-AtomicTextFile -Path $proxyPath -Content $template.Replace('__TARGET_PORT__', [string]$targetPort)
    Invoke-HealthGate -Uri $PublicReadyUrl -ExpectedSchemaVersion $currentSchemaVersion
}
catch {
    if ($proxyExisted) {
        Write-AtomicTextFile -Path $proxyPath -Content $previousProxyContent
    }
    elseif (Test-Path -LiteralPath $proxyPath -PathType Leaf) {
        Remove-Item -LiteralPath $proxyPath -Force
    }
    throw
}

[ordered]@{
    slot = $TargetSlot
    site = $targetSite
    port = $targetPort
    version = [string]$manifest.version
    gitCommit = [string]$manifest.gitCommit
    schemaVersion = $currentSchemaVersion
    activatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    previousSlot = [string]$current.slot
    rollback = $true
} | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
Write-Output "ROLLED_BACK:$($manifest.version):$TargetSlot"
