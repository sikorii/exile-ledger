param(
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "ExileLedger.csproj"

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath
$version = @($projectXml.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ })[0]
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Could not read <Version> from $projectPath"
}

$publishDir = Join-Path $repoRoot "publish\ExileLedger-singlefile"
$artifactsDir = Join-Path $repoRoot "artifacts"
$packageWorkDir = Join-Path $artifactsDir "package-work"
$stageDir = Join-Path $packageWorkDir "Exile Ledger"
$zipPath = Join-Path $artifactsDir "ExileLedger-v$version-$RuntimeIdentifier.zip"

function Assert-UnderRepo {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $repoFullPath = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $fullPath.StartsWith($repoFullPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to modify path outside repository: $fullPath"
    }
}

function Remove-PathIfExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    Assert-UnderRepo -Path $Path
    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }
}

function Copy-ReleaseItem {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $sourcePath = Join-Path $publishDir $RelativePath
    if (-not (Test-Path -LiteralPath $sourcePath)) {
        throw "Expected publish item missing: $RelativePath"
    }

    $destinationPath = Join-Path $stageDir $RelativePath
    $destinationParent = Split-Path -Parent $destinationPath
    if (-not [string]::IsNullOrWhiteSpace($destinationParent)) {
        New-Item -ItemType Directory -Force -Path $destinationParent | Out-Null
    }

    if ((Get-Item -LiteralPath $sourcePath).PSIsContainer) {
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Recurse -Force
    } else {
        Copy-Item -LiteralPath $sourcePath -Destination $destinationPath -Force
    }
}

function Test-BannedReleasePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RelativePath
    )

    $normalized = $RelativePath -replace "\\", "/"
    return $normalized -match "(^|/)secrets\.json$" -or
        $normalized -match "(^|/)latest-stash" -or
        $normalized -match "(^|/).*-count-overrides\.json$" -or
        $normalized -match "\.pdb$" -or
        $normalized -match "(^|/)(config|debug|cache|logs?|images|training)(/|$)" -or
        $normalized -match "(price|icon)-cache" -or
        $normalized -match "(^|/)hotkeys\.json$" -or
        $normalized -match "(^|/)(settings|runtimeconfig|deps)\.json$"
}

Write-Host "Packaging Exile Ledger $version for $RuntimeIdentifier..."

New-Item -ItemType Directory -Force -Path $artifactsDir | Out-Null
Remove-PathIfExists -Path $publishDir
Remove-PathIfExists -Path $packageWorkDir
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

dotnet publish $projectPath `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $publishDir

New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

$releaseItems = @(
    "ExileLedger.exe",
    "Tesseract.dll",
    "assets",
    "Data",
    "x64"
)

foreach ($item in $releaseItems) {
    Copy-ReleaseItem -RelativePath $item
}

$readmeText = @"
Exile Ledger $version

1. Extract the zip first.
2. Open the extracted Exile Ledger folder.
3. Run ExileLedger.exe.

Do not run the app directly from inside the zip.
This early friends-alpha build is not code-signed, so Windows SmartScreen may appear.
"@

Set-Content -LiteralPath (Join-Path $stageDir "README.txt") -Value $readmeText -Encoding ASCII

$bannedFiles = @(Get-ChildItem -LiteralPath $stageDir -Recurse -File |
    Where-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($stageDir, $_.FullName)
        Test-BannedReleasePath -RelativePath $relativePath
    })

if ($bannedFiles.Count -gt 0) {
    Write-Error "Banned release contents found:`n$($bannedFiles.FullName -join [Environment]::NewLine)"
}

Compress-Archive -LiteralPath $stageDir -DestinationPath $zipPath -CompressionLevel Optimal

$rootItemCount = (Get-ChildItem -LiteralPath $stageDir -Force).Count
$totalFileCount = (Get-ChildItem -LiteralPath $stageDir -Recurse -File -Force).Count
$sha256 = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Package summary"
Write-Host "Publish output: $publishDir"
Write-Host "Staged folder:  $stageDir"
Write-Host "Zip:            $zipPath"
Write-Host "Root items:     $rootItemCount"
Write-Host "Total files:    $totalFileCount"
Write-Host "SHA256:         $sha256"
