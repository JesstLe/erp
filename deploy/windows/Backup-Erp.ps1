[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$StagingDirectory,
    [Parameter(Mandatory)][string]$AgeRecipient,
    [string]$PgDumpExecutable = 'pg_dump',
    [string]$AgeExecutable = 'age',
    [string]$AttachmentDirectory,
    [string]$DataProtectionKeyDirectory
)

. (Join-Path $PSScriptRoot 'Common.ps1')
$stagingRoot = Resolve-SafeDirectory $StagingDirectory
[System.IO.Directory]::CreateDirectory($stagingRoot) | Out-Null
Assert-FreeSpace -Path $stagingRoot -MinimumFreeGb 10
foreach ($name in @('ERP_BACKUP_HOST', 'ERP_BACKUP_PORT', 'ERP_BACKUP_DATABASE', 'ERP_BACKUP_USER', 'ERP_BACKUP_PASSWORD')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "缺少环境变量 $name" }
}
if ($AgeRecipient -notmatch '^age1[0-9a-z]+$') { throw 'age 接收方公钥格式无效' }

$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
$temporaryRoot = Join-Path $stagingRoot "backup-work-$stamp-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$plainArchive = Join-Path $stagingRoot "erp-backup-$stamp.zip"
$encryptedArchive = "$plainArchive.age"
$env:PGPASSWORD = $env:ERP_BACKUP_PASSWORD
try {
    $dumpPath = Join-Path $temporaryRoot 'database.dump'
    & $PgDumpExecutable --host=$env:ERP_BACKUP_HOST --port=$env:ERP_BACKUP_PORT `
        --username=$env:ERP_BACKUP_USER --dbname=$env:ERP_BACKUP_DATABASE --format=custom `
        --no-owner --no-privileges --file=$dumpPath
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $dumpPath)) { throw 'pg_dump 失败' }

    if (-not [string]::IsNullOrWhiteSpace($AttachmentDirectory)) {
        $source = Resolve-SafeDirectory $AttachmentDirectory
        Copy-Item -LiteralPath $source -Destination (Join-Path $temporaryRoot 'attachments') -Recurse
    }
    if (-not [string]::IsNullOrWhiteSpace($DataProtectionKeyDirectory)) {
        $source = Resolve-SafeDirectory $DataProtectionKeyDirectory
        Copy-Item -LiteralPath $source -Destination (Join-Path $temporaryRoot 'data-protection-keys') -Recurse
    }

    $payloadFiles = Get-ChildItem -LiteralPath $temporaryRoot -Recurse -File | ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($temporaryRoot, $_.FullName).Replace('\', '/')
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    [ordered]@{
        formatVersion = 1
        createdAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        database = $env:ERP_BACKUP_DATABASE
        files = @($payloadFiles)
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $temporaryRoot 'backup-manifest.json') -Encoding utf8NoBOM

    Compress-Archive -Path (Join-Path $temporaryRoot '*') -DestinationPath $plainArchive -CompressionLevel Optimal
    & $AgeExecutable --recipient $AgeRecipient --output $encryptedArchive $plainArchive
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $encryptedArchive)) { throw 'age 加密失败' }
    Remove-Item -LiteralPath $plainArchive -Force
    $hash = (Get-FileHash -LiteralPath $encryptedArchive -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $([System.IO.Path]::GetFileName($encryptedArchive))" | Set-Content -LiteralPath "$encryptedArchive.sha256" -Encoding ascii
    Write-Output $encryptedArchive
}
finally {
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $plainArchive) { Remove-Item -LiteralPath $plainArchive -Force }
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
