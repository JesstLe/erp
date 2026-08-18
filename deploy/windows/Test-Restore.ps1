[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$EncryptedBackup,
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9_]+_restore_[A-Za-z0-9_]+$')][string]$TargetDatabase,
    [Parameter(Mandatory)][string]$AgeIdentityFile,
    [string]$AgeExecutable = 'age',
    [string]$PgRestoreExecutable = 'pg_restore',
    [string]$PsqlExecutable = 'psql',
    [string]$CreatedbExecutable = 'createdb',
    [string]$DropdbExecutable = 'dropdb',
    [switch]$DropAfterValidation
)

. (Join-Path $PSScriptRoot 'Common.ps1')
$backupPath = [System.IO.Path]::GetFullPath($EncryptedBackup)
if (-not (Test-Path -LiteralPath $backupPath -PathType Leaf)) { throw '加密备份不存在' }
if (-not (Test-Path -LiteralPath $AgeIdentityFile -PathType Leaf)) { throw 'age 身份文件不存在' }
foreach ($name in @('ERP_RESTORE_HOST', 'ERP_RESTORE_PORT', 'ERP_RESTORE_ADMIN_USER', 'ERP_RESTORE_ADMIN_PASSWORD')) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "缺少环境变量 $name" }
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "erp-restore-$([Guid]::NewGuid().ToString('N'))"
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
$plainArchive = Join-Path $temporaryRoot 'backup.zip'
$env:PGPASSWORD = $env:ERP_RESTORE_ADMIN_PASSWORD
$created = $false
try {
    $existing = & $PsqlExecutable --host=$env:ERP_RESTORE_HOST --port=$env:ERP_RESTORE_PORT `
        --username=$env:ERP_RESTORE_ADMIN_USER --dbname=postgres --tuples-only --no-align `
        --command="SELECT 1 FROM pg_database WHERE datname = '$TargetDatabase'"
    if ($LASTEXITCODE -ne 0) { throw '无法检查恢复目标数据库' }
    if (($existing | Out-String).Trim() -eq '1') { throw '恢复目标数据库已存在，拒绝覆盖' }

    & $AgeExecutable --decrypt --identity $AgeIdentityFile --output $plainArchive $backupPath
    if ($LASTEXITCODE -ne 0) { throw 'age 解密失败' }
    Expand-Archive -LiteralPath $plainArchive -DestinationPath $temporaryRoot
    $manifest = Get-Content -LiteralPath (Join-Path $temporaryRoot 'backup-manifest.json') -Raw | ConvertFrom-Json
    if ([int]$manifest.formatVersion -ne 1) { throw '不支持的备份格式' }
    $listedPaths = @($manifest.files | ForEach-Object { ([string]$_.path).Replace('\\', '/') })
    if ($listedPaths.Count -ne ($listedPaths | Sort-Object -Unique).Count) { throw '备份清单包含重复路径' }
    if ('database.dump' -notin $listedPaths) { throw '备份清单缺少 database.dump' }
    $safeTemporaryRoot = Resolve-SafeDirectory $temporaryRoot
    foreach ($file in $manifest.files) {
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $safeTemporaryRoot ([string]$file.path)))
        if (-not $candidate.StartsWith("$safeTemporaryRoot$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "备份清单包含越界路径: $($file.path)"
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) { throw "备份内容缺失: $($file.path)" }
        $actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne ([string]$file.sha256).ToLowerInvariant()) { throw "备份内容校验失败: $($file.path)" }
    }

    & $CreatedbExecutable --host=$env:ERP_RESTORE_HOST --port=$env:ERP_RESTORE_PORT `
        --username=$env:ERP_RESTORE_ADMIN_USER $TargetDatabase
    if ($LASTEXITCODE -ne 0) { throw '创建隔离恢复数据库失败' }
    $created = $true
    & $PgRestoreExecutable --host=$env:ERP_RESTORE_HOST --port=$env:ERP_RESTORE_PORT `
        --username=$env:ERP_RESTORE_ADMIN_USER --dbname=$TargetDatabase --no-owner --no-privileges `
        --exit-on-error (Join-Path $temporaryRoot 'database.dump')
    if ($LASTEXITCODE -ne 0) { throw 'pg_restore 失败' }

    $verificationSql = @"
SELECT CASE WHEN to_regclass('public.service_orders') IS NOT NULL
 AND to_regclass('public.member_account_ledgers') IS NOT NULL
 AND to_regclass('public.inventory_movements') IS NOT NULL
 AND to_regclass('public.price_override_approvals') IS NOT NULL
 AND NOT EXISTS (SELECT 1 FROM member_accounts WHERE balance_units < 0)
 THEN 'RESTORE_OK' ELSE 'RESTORE_INVALID' END;
"@
    $result = & $PsqlExecutable --host=$env:ERP_RESTORE_HOST --port=$env:ERP_RESTORE_PORT `
        --username=$env:ERP_RESTORE_ADMIN_USER --dbname=$TargetDatabase --tuples-only --no-align `
        --set=ON_ERROR_STOP=1 --command=$verificationSql
    if ($LASTEXITCODE -ne 0 -or ($result | Out-String).Trim() -ne 'RESTORE_OK') { throw '恢复后业务约束校验失败' }
    Write-Output "RESTORE_OK:$TargetDatabase"
}
finally {
    if ($DropAfterValidation -and $created) {
        & $DropdbExecutable --host=$env:ERP_RESTORE_HOST --port=$env:ERP_RESTORE_PORT `
            --username=$env:ERP_RESTORE_ADMIN_USER $TargetDatabase
    }
    Remove-Item Env:PGPASSWORD -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $temporaryRoot) { Remove-Item -LiteralPath $temporaryRoot -Recurse -Force }
}
