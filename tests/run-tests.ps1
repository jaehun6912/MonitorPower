$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$testOutput = Join-Path $env:TEMP ('MonitorPowerTests-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testOutput | Out-Null

# Antivirus (AhnLab V3 on the development PC) can hold, kill or delete a freshly compiled executable while it checks it,
# which shows up as random test failures. Start every new executable once with a harmless argument and wait until it
# answers as expected, so the real run starts only after that first check.
function Wait-Runnable([string]$exe, [string[]]$arguments, [int]$expected) {
    $deadline = (Get-Date).AddSeconds(60)
    while ($true) {
        if (-not (Test-Path -LiteralPath $exe)) { throw "Antivirus removed $exe. Allow the test folder or run the tests again." }
        try {
            $probe = Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru -Wait
            if ($probe.ExitCode -eq $expected) { return }
        } catch { }
        if ((Get-Date) -gt $deadline) { throw "$exe did not start within 60 seconds (antivirus scan?)." }
        Start-Sleep -Milliseconds 500
    }
}

& $compiler /nologo /codepage:65001 /target:exe "/out:$testOutput\FakeControlMyMonitor.exe" "$PSScriptRoot\FakeControlMyMonitor.cs"
if ($LASTEXITCODE -ne 0) { throw 'Fake build failed' }
Copy-Item -LiteralPath "$testOutput\FakeControlMyMonitor.exe" -Destination "$testOutput\ControlMyMonitor.exe"
& $compiler /nologo /codepage:65001 /target:exe /r:System.dll /r:System.Core.dll "/out:$testOutput\Tests.exe" "$PSScriptRoot\Tests.cs" "$PSScriptRoot\..\src\MonitorCore.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }
$uiSources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot '..\src') -Filter *.cs | Where-Object { $_.Name -ne 'AssemblyInfo.cs' } | ForEach-Object { $_.FullName }
& $compiler /nologo /codepage:65001 /target:exe /main:UiTests /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/win32icon:$PSScriptRoot\..\src\MonitorPower.ico" "/out:$testOutput\UiTests.exe" "$PSScriptRoot\UiTests.cs" $uiSources
if ($LASTEXITCODE -ne 0) { throw 'UI test build failed' }
# Run the UI tests under the same runtime switches as the shipped program (.NET Framework 4.8 target: UIA live regions, current RichEdit).
Copy-Item -LiteralPath "$PSScriptRoot\..\src\App.config" -Destination "$testOutput\UiTests.exe.config"

foreach ($mock in 'FakeControlMyMonitor.exe', 'ControlMyMonitor.exe') { Wait-Runnable "$testOutput\$mock" @('/GetValue', 'probe', 'D6') 1 }
Wait-Runnable "$testOutput\Tests.exe" @('--probe', "`"$testOutput\FakeControlMyMonitor.exe`"") 0
Wait-Runnable "$testOutput\UiTests.exe" @('--probe') 0

& "$testOutput\Tests.exe" "$testOutput\FakeControlMyMonitor.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
& "$testOutput\UiTests.exe" @args
if ($LASTEXITCODE -ne 0) { throw 'UI tests failed' }
