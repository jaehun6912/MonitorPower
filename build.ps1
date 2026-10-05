<#
  .\build.ps1            # portable\ 에 실행 파일을 만듭니다(.NET Framework 4.8 기본 컴파일러, 별도 SDK 불필요).
  .\build.ps1 -Package   # artifacts\ 에 휴대용 ZIP과 설치 파일을 만듭니다(Inno Setup 6 필요). portable\ 은 건드리지 않습니다.
#>
param([switch]$Package)

$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }

# Program files only: the exe, its runtime config, README and LICENSE.
function Build-To([string]$destination) {
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    $sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter *.cs | ForEach-Object { $_.FullName }
    & $compiler /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 "/win32icon:$PSScriptRoot\src\MonitorPower.ico" /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/out:$destination\MonitorPower.exe" $sources
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    Copy-Item -LiteralPath "$PSScriptRoot\src\App.config" -Destination "$destination\MonitorPower.exe.config" -Force
    Copy-Item -LiteralPath "$PSScriptRoot\README.md", "$PSScriptRoot\LICENSE" -Destination $destination -Force
    Write-Output "Built: $destination\MonitorPower.exe"
}

if (-not $Package) { Build-To (Join-Path $PSScriptRoot 'portable'); return }

# The package is compiled into its own clean folder, so it never picks up the ControlMyMonitor files
# you may keep in portable\ (not ours to redistribute) and works while that copy is running.
$artifacts = Join-Path $PSScriptRoot 'artifacts'
$stage = Join-Path $artifacts 'package'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
Build-To $stage
$version = (Get-Item -LiteralPath "$stage\MonitorPower.exe").VersionInfo.ProductVersion
if ($version -notmatch '^\d+\.\d+\.\d+') { throw "Unexpected product version: $version" }
$version = $Matches[0]

$zip = Join-Path $artifacts "MonitorPower-$version-portable.zip"
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip

$iscc = $null
foreach ($candidate in @("$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe", "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe")) {
    if (Test-Path -LiteralPath $candidate) { $iscc = $candidate; break }
}
if (-not $iscc) { throw 'Inno Setup 6 not found. Install it with: winget install --id JRSoftware.InnoSetup' }
& $iscc /Q "/DAppVersion=$version" "/DPackageDir=$stage" (Join-Path $PSScriptRoot 'installer\MonitorPower.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
$setup = Join-Path $artifacts "MonitorPower-setup-$version.exe"

foreach ($file in $setup, $zip) {
    $item = Get-Item -LiteralPath $file
    Write-Output ("{0}  {1:N0} bytes  SHA256 {2}" -f $item.Name, $item.Length, (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash)
}
