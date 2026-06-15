<#
.SYNOPSIS
	 Build and packaging script for Mesen Orion on Windows.

.DESCRIPTION
	 Compiles the solution in the specified configuration (Debug, Release, or both),
	 generates the distributable single-file executable, and optionally packages
	 everything into a release-ready ZIP archive.

.PARAMETER Configuration
	 Build configuration: Debug, Release, or All.
	 Default: Release

.PARAMETER PlatformToolset
	 MSVC toolset to use (e.g. v143, v145).
	 Default: v145

.PARAMETER Package
	 When specified, generates the distributable ZIP after building Release.

.PARAMETER ZipName
	 Output ZIP file name (no path).
	 Default: MesenOrion-win-x64.zip

.EXAMPLE
	 .\build-windows.ps1
	 Builds in Release.

.EXAMPLE
	 .\build-windows.ps1 -Configuration All
	 Builds both Debug and Release.

.EXAMPLE
	 .\build-windows.ps1 -Package
	 Builds Release and generates the distributable ZIP.

.EXAMPLE
	 .\build-windows.ps1 -Configuration Debug
	 Builds Debug only.
#>

[CmdletBinding()]
param(
	 [ValidateSet("Debug", "Release", "All")]
	 [string]$Configuration = "Release",

	 [string]$PlatformToolset = "v145",

	 [switch]$Package,

	 [string]$ZipName = "MesenOrion-win-x64.zip"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# ---------------------------------------------------------------------------
# Helpers
# ---------------------------------------------------------------------------

function Write-Header([string]$msg) {
	 Write-Host ""
	 Write-Host "========================================" -ForegroundColor Cyan
	 Write-Host "  $msg" -ForegroundColor Cyan
	 Write-Host "========================================" -ForegroundColor Cyan
}

function Write-Ok([string]$msg)   { Write-Host "  [OK] $msg" -ForegroundColor Green }
function Write-Info([string]$msg) { Write-Host "  [..] $msg" -ForegroundColor Gray }
function Write-Fail([string]$msg) { Write-Host "  [!!] $msg" -ForegroundColor Red }

# ---------------------------------------------------------------------------
# Paths
# ---------------------------------------------------------------------------

$SolutionDir = $PSScriptRoot
$SolutionFile = Join-Path $SolutionDir "Mesen.sln"
$ShadersDir   = Join-Path $SolutionDir "Shaders"
$ReadmeSrc    = Join-Path $SolutionDir "README.md"
$ZipPath      = Join-Path $SolutionDir $ZipName

# ---------------------------------------------------------------------------
# Prerequisites check
# ---------------------------------------------------------------------------

Write-Header "Checking prerequisites"

# MSBuild
$msbuild = Get-Command msbuild -ErrorAction SilentlyContinue
if (-not $msbuild) {
	 Write-Fail "MSBuild not found in PATH."
	 Write-Fail "Run this script from a 'Developer PowerShell for VS' prompt, or add MSBuild to PATH."
	 exit 1
}
Write-Ok "MSBuild: $($msbuild.Source)"

# dotnet
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if (-not $dotnet) {
	 Write-Fail "dotnet SDK not found in PATH."
	 exit 1
}
Write-Ok "dotnet: $(dotnet --version)"

if (-not (Test-Path $SolutionFile)) {
	 Write-Fail "Mesen.sln not found at: $SolutionDir"
	 exit 1
}
Write-Ok "Solution: $SolutionFile"

# ---------------------------------------------------------------------------
# Build function
# ---------------------------------------------------------------------------

function Invoke-Build([string]$cfg) {
	 Write-Header "Building: $cfg"

	 $args = @(
		  $SolutionFile,
		  "/p:Configuration=$cfg",
		  "/p:Platform=x64",
		  "/p:PlatformToolset=$PlatformToolset",
		  "/m",
		  "/v:minimal"
	 )

	 Write-Info "msbuild $($args -join ' ')"
	 & msbuild @args

	 if ($LASTEXITCODE -ne 0) {
		  Write-Fail "Build failed for $cfg (exit code $LASTEXITCODE)"
		  exit $LASTEXITCODE
	 }

	 $outputDll = Join-Path $SolutionDir "bin\win-x64\$cfg\Mesen.dll"
	 if (Test-Path $outputDll) {
		  $size = [math]::Round((Get-Item $outputDll).Length / 1MB, 1)
		  Write-Ok "bin\win-x64\$cfg\Mesen.dll ($size MB)"
	 }
	 Write-Ok "$cfg build completed"
}

# ---------------------------------------------------------------------------
# Publish and package function
# ---------------------------------------------------------------------------

function Invoke-Package {
	 Write-Header "Generating distributable executable (dotnet publish)"

	 $uiDir = Join-Path $SolutionDir "UI"

	 Push-Location $uiDir
	 try {
		  dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --no-self-contained
		  if ($LASTEXITCODE -ne 0) {
				Write-Fail "dotnet publish failed (exit code $LASTEXITCODE)"
				exit $LASTEXITCODE
		  }
	 } finally {
		  Pop-Location
	 }

	 # dotnet publish may land in C:\bin or inside the project tree depending on
	 # how SolutionDir is resolved; check both candidates.
	 $publishCandidates = @(
		  (Join-Path $SolutionDir "bin\win-x64\Release\win-x64\publish"),
		  "C:\bin\win-x64\Release\win-x64\publish"
	 )
	 $publishDir = $publishCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1

	 if (-not $publishDir) {
		  Write-Fail "Publish directory not found. Searched paths:"
		  $publishCandidates | ForEach-Object { Write-Fail "  $_" }
		  exit 1
	 }
	 Write-Ok "Publish directory: $publishDir"

	 # Copy MesenCore.dll and Dependencies.zip from the Release build output
	 $releaseDir = Join-Path $SolutionDir "bin\win-x64\Release"
	 foreach ($file in @("MesenCore.dll", "Dependencies.zip")) {
		  $src = Join-Path $releaseDir $file
		  $dst = Join-Path $publishDir $file
		  if (Test-Path $src) {
				Copy-Item $src $dst -Force
				Write-Ok "Copied: $file"
		  } else {
				Write-Fail "Not found in Release output: $file"
				exit 1
		  }
	 }

	 # Copy README.md
	 if (Test-Path $ReadmeSrc) {
		  Copy-Item $ReadmeSrc (Join-Path $publishDir "README.md") -Force
		  Write-Ok "Copied: README.md"
	 }

	 # Copy the Shaders folder
	 $shadersDst = Join-Path $publishDir "Shaders"
	 if (Test-Path $ShadersDir) {
		  if (Test-Path $shadersDst) { Remove-Item $shadersDst -Recurse -Force }
		  Copy-Item $ShadersDir $shadersDst -Recurse
		  $count = (Get-ChildItem $shadersDst -Recurse -File).Count
		  Write-Ok "Shaders copied: $count files"
	 } else {
		  Write-Info "Shaders folder not found, skipping."
	 }

	 # Generate ZIP
	 Write-Header "Creating ZIP: $ZipName"

	 if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
	 Compress-Archive -Path "$publishDir\*" -DestinationPath $ZipPath

	 $zipSize = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
	 Write-Ok "ZIP created: $ZipPath ($zipSize MB)"

	 # Print archive contents summary
	 Add-Type -AssemblyName System.IO.Compression.FileSystem
	 $zip = [System.IO.Compression.ZipFile]::OpenRead($ZipPath)
	 $rootFiles   = @($zip.Entries | Where-Object { $_.FullName -notmatch "[/\\]" })
	 $shaderFiles = @($zip.Entries | Where-Object { $_.FullName -like "Shaders*" })
	 $zip.Dispose()

	 Write-Host ""
	 Write-Host "  ZIP contents:" -ForegroundColor Cyan
	 $rootFiles | ForEach-Object {
		  $kb = [math]::Round($_.Length / 1KB, 0)
		  Write-Host ("    {0,-30} {1,8} KB" -f $_.Name, $kb)
	 }
	 Write-Host ("    Shaders/  ({0} files)" -f $shaderFiles.Count)
}

# ---------------------------------------------------------------------------
# Main execution
# ---------------------------------------------------------------------------

$configs = if ($Configuration -eq "All") { @("Debug", "Release") } else { @($Configuration) }

foreach ($cfg in $configs) {
	 Invoke-Build $cfg
}

if ($Package) {
	 if ($Configuration -eq "Debug") {
		  Write-Fail "-Package requires Release or All configuration."
		  exit 1
	 }
	 Invoke-Package
}

Write-Header "All done"
Write-Ok "Configurations built: $($configs -join ', ')"
if ($Package) { Write-Ok "Package: $ZipPath" }
Write-Host ""
