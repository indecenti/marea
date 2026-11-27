# Publishes Marea for Windows x64: self-contained app + mareactl (no .NET install needed),
# then creates the portable ZIP and SHA-256 checksums of the files to distribute.
# Usage: powershell -File build\publish.ps1 [-Version 0.1.0] [-NoPackage | -PackageOnly]
# The release workflow runs it twice: -NoPackage, then code signing, then -PackageOnly.
param([string]$Version = "0.2.1", [switch]$NoPackage, [switch]$PackageOnly)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "dist\Marea"
$dist = Join-Path $root "dist"
if (-not $PackageOnly) {
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    New-Item -ItemType Directory -Force $out | Out-Null

    # Self-contained but NOT single-file: app and mareactl share one copy of the .NET runtime.
    $common = @("-c", "Release", "-r", "win-x64", "--self-contained", "true",
        "-p:DebugType=none", "-p:Version=$Version", "-p:SatelliteResourceLanguages=en%3Bit%3Bes%3Bfr%3Bde", "-o", $out)
    dotnet publish (Join-Path $root "src\Marea.App\Marea.App.csproj") @common
    if ($LASTEXITCODE -ne 0) { throw "app publish failed" }
    # mareactl also builds for Linux (net10.0): publish the Windows target here.
    dotnet publish (Join-Path $root "src\Marea.Cli\Marea.Cli.csproj") -f net10.0-windows10.0.19041.0 @common
    if ($LASTEXITCODE -ne 0) { throw "mareactl publish failed" }

    Copy-Item (Join-Path $root "README.md"), (Join-Path $root "SECURITY.md"), (Join-Path $root "LICENSE"), (Join-Path $root "THIRD-PARTY-NOTICES.md") $out
}
if ($NoPackage) { Write-Host "Published (not packaged): $out"; return }
$zip = Join-Path $dist "Marea-$Version-win-x64-portable.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$out\*" -DestinationPath $zip
# Checksum files use LF line endings so `sha256sum -c` works on Linux and macOS too.
function Write-Sums($files, $path) {
    $lines = $files | Get-FileHash -Algorithm SHA256 | ForEach-Object { "{0}  {1}" -f $_.Hash.ToLower(), (Split-Path $_.Path -Leaf) }
    [IO.File]::WriteAllText($path, (($lines -join "`n") + "`n"))
}
Write-Sums (Get-ChildItem $out -File -Filter *.exe) (Join-Path $out "SHA256SUMS.txt")
Write-Sums (Get-Item $zip) "$zip.sha256"
Write-Host "Done: $out and $zip"
