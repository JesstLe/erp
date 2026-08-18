[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$ReleasePackage,
    [Parameter(Mandatory)][ValidatePattern('^[0-9a-fA-F]{64}$')][string]$ExpectedPackageSha256,
    [Parameter(Mandatory)][string]$InstallRoot,
    [Parameter(Mandatory)][string]$ProxyRoot,
    [Parameter(Mandatory)][string]$BackupStagingDirectory,
    [Parameter(Mandatory)][string]$BackupAgeRecipient,
    [string]$BlueSiteName = 'ERP-Blue',
    [string]$GreenSiteName = 'ERP-Green',
    [int]$BluePort = 5101,
    [int]$GreenPort = 5102,
    [uri]$PublicReadyUrl = 'https://localhost/health/ready',
    [string]$FlywayExecutable = 'flyway',
    [string]$AgeExecutable = 'age',
    [string]$PgDumpExecutable = 'pg_dump',
    [string]$AttachmentDirectory,
    [string]$DataProtectionKeyDirectory
)

. (Join-Path $PSScriptRoot 'Common.ps1')
Import-Module WebAdministration -ErrorAction Stop
$packagePath = [System.IO.Path]::GetFullPath($ReleasePackage)
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) { throw '发布包不存在' }
$actualPackageSha256 = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualPackageSha256 -ne $ExpectedPackageSha256.ToLowerInvariant()) { throw '发布包 SHA-256 与受信签收值不一致' }
$safeInstallRoot = Resolve-SafeDirectory $InstallRoot
$safeProxyRoot = Resolve-SafeDirectory $ProxyRoot
[System.IO.Directory]::CreateDirectory($safeInstallRoot) | Out-Null
[System.IO.Directory]::CreateDirectory($safeProxyRoot) | Out-Null
Assert-FreeSpace -Path $safeInstallRoot -MinimumFreeGb 10

$inspectionRoot = Join-Path $safeInstallRoot "inspection-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($inspectionRoot) | Out-Null
$proxyPath = Join-Path $safeProxyRoot 'web.config'
$proxyExisted = Test-Path -LiteralPath $proxyPath -PathType Leaf
$previousProxyContent = if ($proxyExisted) { Get-Content -LiteralPath $proxyPath -Raw } else { $null }
$proxySwitched = $false
try {
    Expand-Archive -LiteralPath $packagePath -DestinationPath $inspectionRoot
    $manifest = Get-ReleaseManifest -ReleaseRoot $inspectionRoot
    Test-ReleasePayload -ReleaseRoot $inspectionRoot -Manifest $manifest
    if ([int]$manifest.formatVersion -ne 1) { throw '不支持的发布包格式' }
    $version = [string]$manifest.version
    if ($version -notmatch '^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$') { throw '发布清单版本号无效' }
    if ([string]$manifest.gitCommit -notmatch '^[0-9a-fA-F]{40}$') { throw '发布清单 Git 提交标识无效' }
    if ([string]$manifest.schema.min -notmatch '^[0-9]+$' -or [string]$manifest.schema.max -notmatch '^[0-9]+$' `
        -or [Int64]([string]$manifest.schema.min) -gt [Int64]([string]$manifest.schema.max)) {
        throw '发布清单 schema 范围无效'
    }
    $releaseDirectory = Join-Path $safeInstallRoot 'releases'
    [System.IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null
    $releaseRoot = Join-Path $releaseDirectory $version
    if (Test-Path -LiteralPath $releaseRoot) { throw "版本目录已存在，禁止覆盖: $version" }

    & (Join-Path $PSScriptRoot 'Backup-Erp.ps1') -StagingDirectory $BackupStagingDirectory `
        -AgeRecipient $BackupAgeRecipient -PgDumpExecutable $PgDumpExecutable -AgeExecutable $AgeExecutable `
        -AttachmentDirectory $AttachmentDirectory -DataProtectionKeyDirectory $DataProtectionKeyDirectory
    if ($LASTEXITCODE -ne 0) { throw '发布前备份失败' }

    & (Join-Path $PSScriptRoot 'Invoke-DatabaseMigration.ps1') `
        -MigrationDirectory (Join-Path $inspectionRoot 'db/migrations') -FlywayExecutable $FlywayExecutable `
        -LogDirectory (Join-Path $safeInstallRoot 'logs/deploy')
    if ($LASTEXITCODE -ne 0) { throw '数据库迁移失败' }

    [System.IO.Directory]::Move($inspectionRoot, $releaseRoot)
    $inspectionRoot = $null
    $statePath = Join-Path $safeInstallRoot 'active-slot.json'
    $activeSlot = 'Blue'
    if (Test-Path -LiteralPath $statePath) {
        $activeSlot = [string](Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json).slot
    }
    if ($activeSlot -notin @('Blue', 'Green')) { throw 'active-slot.json 内容无效' }
    $targetSlot = if ($activeSlot -eq 'Blue') { 'Green' } else { 'Blue' }
    $targetSite = if ($targetSlot -eq 'Blue') { $BlueSiteName } else { $GreenSiteName }
    $targetPort = if ($targetSlot -eq 'Blue') { $BluePort } else { $GreenPort }
    if (-not (Test-Path "IIS:\Sites\$targetSite")) { throw "IIS 站点不存在: $targetSite" }

    Stop-Website -Name $targetSite -ErrorAction SilentlyContinue
    Set-ItemProperty "IIS:\Sites\$targetSite" -Name physicalPath -Value (Join-Path $releaseRoot 'app')
    Start-Website -Name $targetSite
    Invoke-HealthGate -Uri "http://127.0.0.1:$targetPort/health/ready" `
        -ExpectedSchemaVersion ([string]$manifest.schema.max)

    $template = Get-Content -LiteralPath (Join-Path $releaseRoot 'deploy/windows/proxy-web.config.template') -Raw
    $proxyConfig = $template.Replace('__TARGET_PORT__', [string]$targetPort)
    Write-AtomicTextFile -Path $proxyPath -Content $proxyConfig
    $proxySwitched = $true
    Invoke-HealthGate -Uri $PublicReadyUrl -ExpectedSchemaVersion ([string]$manifest.schema.max)

    [ordered]@{
        slot = $targetSlot
        site = $targetSite
        port = $targetPort
        version = $version
        gitCommit = [string]$manifest.gitCommit
        schemaVersion = [string]$manifest.schema.max
        activatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        previousSlot = $activeSlot
    } | ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding utf8NoBOM
    $proxySwitched = $false
    Write-Output "DEPLOYED:$version:$targetSlot"
}
catch {
    if ($proxySwitched) {
        if ($proxyExisted) {
            Write-AtomicTextFile -Path $proxyPath -Content $previousProxyContent
        }
        elseif (Test-Path -LiteralPath $proxyPath -PathType Leaf) {
            Remove-Item -LiteralPath $proxyPath -Force
        }
    }
    throw
}
finally {
    if ($null -ne $inspectionRoot -and (Test-Path -LiteralPath $inspectionRoot)) {
        Remove-Item -LiteralPath $inspectionRoot -Recurse -Force
    }
}
