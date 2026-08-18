Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-SafeDirectory {
    param([Parameter(Mandatory)][string]$Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    $root = [System.IO.Path]::GetPathRoot($full)
    if ([string]::IsNullOrWhiteSpace($full) -or $full -eq $root -or $full.Length -lt 8) {
        throw "拒绝使用过宽或无效目录: $full"
    }
    return $full.TrimEnd([System.IO.Path]::DirectorySeparatorChar)
}

function Get-ReleaseManifest {
    param([Parameter(Mandatory)][string]$ReleaseRoot)
    $manifestPath = Join-Path $ReleaseRoot 'release-manifest.json'
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "发布包缺少 release-manifest.json"
    }
    return Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
}

function Test-ReleasePayload {
    param(
        [Parameter(Mandatory)][string]$ReleaseRoot,
        [Parameter(Mandatory)]$Manifest
    )
    $safeRoot = Resolve-SafeDirectory $ReleaseRoot
    $listedPaths = @($Manifest.files | ForEach-Object { ([string]$_.path).Replace('\\', '/') })
    if ($listedPaths.Count -ne ($listedPaths | Sort-Object -Unique).Count) {
        throw '发布清单包含重复路径'
    }
    foreach ($file in $Manifest.files) {
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $safeRoot ([string]$file.path)))
        if (-not $candidate.StartsWith("$safeRoot$([System.IO.Path]::DirectorySeparatorChar)", [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "发布清单包含越界路径: $($file.path)"
        }
        if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
            throw "发布文件不存在: $($file.path)"
        }
        $actual = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($actual -ne ([string]$file.sha256).ToLowerInvariant()) {
            throw "发布文件校验失败: $($file.path)"
        }
    }
    $actualPaths = @(Get-ChildItem -LiteralPath $safeRoot -Recurse -File | ForEach-Object {
        [System.IO.Path]::GetRelativePath($safeRoot, $_.FullName).Replace('\\', '/')
    } | Where-Object { $_ -ne 'release-manifest.json' })
    $unexpected = @($actualPaths | Where-Object { $_ -notin $listedPaths })
    $missing = @($listedPaths | Where-Object { $_ -notin $actualPaths })
    if ($unexpected.Count -gt 0 -or $missing.Count -gt 0) {
        throw "发布清单与实际文件不一致；多余=$($unexpected -join ',')；缺少=$($missing -join ',')"
    }
}

function Invoke-HealthGate {
    param(
        [Parameter(Mandatory)][uri]$Uri,
        [Parameter(Mandatory)][string]$ExpectedSchemaVersion,
        [int]$Attempts = 20,
        [int]$DelaySeconds = 3
    )
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $result = Invoke-RestMethod -Uri $Uri -Method Get -TimeoutSec 10
            if ($result.status -eq 'ready' -and [string]$result.schemaVersion -eq $ExpectedSchemaVersion) {
                return
            }
        }
        catch {
            if ($attempt -eq $Attempts) { throw }
        }
        if ($attempt -lt $Attempts) { Start-Sleep -Seconds $DelaySeconds }
    }
    throw "健康检查未通过: $Uri"
}

function Assert-FreeSpace {
    param([Parameter(Mandatory)][string]$Path, [int]$MinimumFreeGb = 10)
    $root = [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($Path))
    $drive = Get-CimInstance Win32_LogicalDisk -Filter "DeviceID='$($root.TrimEnd('\'))'"
    if ($null -eq $drive -or ($drive.FreeSpace / 1GB) -lt $MinimumFreeGb) {
        throw "磁盘剩余空间不足 $MinimumFreeGb GB，禁止发布"
    }
}

function Write-AtomicTextFile {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][string]$Content)
    $directory = Split-Path -Parent $Path
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    $temporary = "$Path.new"
    [System.IO.File]::WriteAllText($temporary, $Content, [System.Text.UTF8Encoding]::new($false))
    if (Test-Path -LiteralPath $Path) {
        [System.IO.File]::Replace($temporary, $Path, "$Path.previous", $true)
    }
    else {
        [System.IO.File]::Move($temporary, $Path)
    }
}
