[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+(?:-[0-9A-Za-z.-]+)?$')][string]$Version,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$SchemaMin = '202608180021',
    [string]$SchemaMax = '202608180021',
    [string]$GitCommit
)

. (Join-Path $PSScriptRoot 'Common.ps1')
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$outputRoot = Resolve-SafeDirectory $OutputDirectory
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
if ([string]::IsNullOrWhiteSpace($GitCommit)) {
    $GitCommit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
}
if ($LASTEXITCODE -ne 0 -or $GitCommit -notmatch '^[0-9a-fA-F]{40}$') { throw '无法取得有效 Git 提交标识' }

$stagingRoot = Join-Path $outputRoot "staging-$Version"
if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
[System.IO.Directory]::CreateDirectory($stagingRoot) | Out-Null

try {
    $apiRoot = Join-Path $stagingRoot 'app'
    & dotnet publish (Join-Path $repositoryRoot 'apps/api/Erp.Api/Erp.Api.csproj') -c Release -r win-x64 `
        --self-contained false --no-restore -p:Version=$Version `
        -p:InformationalVersion="$Version+$GitCommit" -o $apiRoot
    if ($LASTEXITCODE -ne 0) { throw 'API 发布失败' }

    Push-Location (Join-Path $repositoryRoot 'apps/web')
    try {
        & npm ci
        if ($LASTEXITCODE -ne 0) { throw '前端依赖安装失败' }
        & npm run build
        if ($LASTEXITCODE -ne 0) { throw '前端构建失败' }
    }
    finally { Pop-Location }

    $webRoot = Join-Path $apiRoot 'wwwroot'
    [System.IO.Directory]::CreateDirectory($webRoot) | Out-Null
    Copy-Item -Path (Join-Path $repositoryRoot 'apps/web/dist/*') -Destination $webRoot -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'db') -Destination (Join-Path $stagingRoot 'db') -Recurse
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'deploy/windows') -Destination (Join-Path $stagingRoot 'deploy/windows') -Recurse

    $files = Get-ChildItem -LiteralPath $stagingRoot -Recurse -File | Sort-Object FullName | ForEach-Object {
        [ordered]@{
            path = [System.IO.Path]::GetRelativePath($stagingRoot, $_.FullName).Replace('\', '/')
            size = $_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    }
    $manifest = [ordered]@{
        formatVersion = 1
        version = $Version
        gitCommit = $GitCommit.ToLowerInvariant()
        builtAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        runtime = 'win-x64-framework-dependent'
        schema = [ordered]@{ min = $SchemaMin; max = $SchemaMax }
        files = @($files)
    }
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $stagingRoot 'release-manifest.json') -Encoding utf8NoBOM

    $packagePath = Join-Path $outputRoot "erp-$Version-win-x64.zip"
    if (Test-Path -LiteralPath $packagePath) { Remove-Item -LiteralPath $packagePath -Force }
    Compress-Archive -Path (Join-Path $stagingRoot '*') -DestinationPath $packagePath -CompressionLevel Optimal
    $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$packageHash  $([System.IO.Path]::GetFileName($packagePath))" | Set-Content -LiteralPath "$packagePath.sha256" -Encoding ascii
    Write-Output $packagePath
}
finally {
    if (Test-Path -LiteralPath $stagingRoot) { Remove-Item -LiteralPath $stagingRoot -Recurse -Force }
}
