param([string]$Tag = 'v1.1.1', [string]$OutputDirectory = 'artifacts/rc')
$ErrorActionPreference = 'Stop'
if ($Tag -notmatch '^v\d+\.\d+\.\d+$') { throw 'Use a release tag such as v1.1.1.' }
$projectRoot = Split-Path $PSScriptRoot -Parent
[xml]$project = Get-Content -LiteralPath (Join-Path $projectRoot 'src/FolderWatch.App/FolderWatch.App.csproj')
$version = ($project.Project.PropertyGroup | Where-Object Version).Version
if ($Tag.Substring(1) -ne $version) { throw "Tag $Tag does not match application version $version." }
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot $OutputDirectory))
New-Item -ItemType Directory -Force -Path $output | Out-Null
$work = Join-Path $output ('package-' + [guid]::NewGuid().ToString('N'))
$publish = Join-Path $work 'publish'
$stage = Join-Path $work 'stage'
New-Item -ItemType Directory -Force -Path $stage | Out-Null
try {
    dotnet publish (Join-Path $projectRoot 'src/FolderWatch.App/FolderWatch.App.csproj') -c Release -r win-x64 --self-contained true -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Portable publish failed.' }
    Copy-Item -LiteralPath (Join-Path $publish 'FolderWatch.exe') -Destination $stage
    foreach ($name in @('README.md', 'LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot $name) -Destination $stage }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/release-notes-v1.1.1.md') -Destination (Join-Path $stage 'RELEASE-NOTES.md')
    $zip = Join-Path $output "FolderWatch-$Tag-win-x64.zip"
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText("$zip.sha256", "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.UTF8Encoding]::new($false))
    Write-Output "ZIP: $zip"
    Write-Output "SHA-256: $hash"
} finally {
    # Only the uniquely-created packaging workspace may be removed.
    $resolvedWork = [IO.Path]::GetFullPath($work)
    if (!$resolvedWork.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe packaging cleanup path.' }
    if (Test-Path -LiteralPath $resolvedWork) { Remove-Item -LiteralPath $resolvedWork -Recurse -Force }
}
