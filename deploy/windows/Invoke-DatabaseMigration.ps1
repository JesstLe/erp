[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MigrationDirectory,
    [string]$FlywayExecutable = 'flyway',
    [string]$LogDirectory
)

. (Join-Path $PSScriptRoot 'Common.ps1')
$migrationRoot = Resolve-SafeDirectory $MigrationDirectory
if (-not (Test-Path -LiteralPath $migrationRoot -PathType Container)) { throw '迁移目录不存在' }
$required = @('ERP_FLYWAY_URL', 'ERP_MIGRATOR_USER', 'ERP_MIGRATOR_PASSWORD')
foreach ($name in $required) {
    if ([string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($name))) { throw "缺少环境变量 $name" }
}
$env:FLYWAY_URL = $env:ERP_FLYWAY_URL
$env:FLYWAY_USER = $env:ERP_MIGRATOR_USER
$env:FLYWAY_PASSWORD = $env:ERP_MIGRATOR_PASSWORD
try {
    $arguments = @(
        "-locations=filesystem:$($migrationRoot.Replace('\', '/'))",
        '-baselineOnMigrate=false', '-cleanDisabled=true', '-validateMigrationNaming=true', '-connectRetries=3'
    )
    & $FlywayExecutable @arguments validate
    if ($LASTEXITCODE -ne 0) { throw 'Flyway validate 失败，禁止迁移' }
    & $FlywayExecutable @arguments migrate
    if ($LASTEXITCODE -ne 0) { throw 'Flyway migrate 失败' }
    & $FlywayExecutable @arguments validate
    if ($LASTEXITCODE -ne 0) { throw '迁移后 Flyway validate 失败' }
    if (-not [string]::IsNullOrWhiteSpace($LogDirectory)) {
        $safeLog = Resolve-SafeDirectory $LogDirectory
        [System.IO.Directory]::CreateDirectory($safeLog) | Out-Null
        & $FlywayExecutable @arguments -outputType=json info | Set-Content -LiteralPath (Join-Path $safeLog "flyway-info-$([DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')).json") -Encoding utf8NoBOM
        if ($LASTEXITCODE -ne 0) { throw 'Flyway info 失败' }
    }
}
finally {
    Remove-Item Env:FLYWAY_PASSWORD -ErrorAction SilentlyContinue
    Remove-Item Env:FLYWAY_USER -ErrorAction SilentlyContinue
    Remove-Item Env:FLYWAY_URL -ErrorAction SilentlyContinue
}
