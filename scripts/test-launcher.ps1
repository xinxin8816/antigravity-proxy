$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
$area = Join-Path ([IO.Path]::GetTempPath()) ('antigravity-launcher-test-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $area | Out-Null
$previousCapture = $env:LAUNCHER_TEST_CAPTURE
try {
    $source = Join-Path $root 'tools\launcher\Launcher.cs'
    & $compiler /nologo /target:exe /main:LauncherTests /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/out:$area\tests.exe" $source (Join-Path $root 'tests\test_launcher.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
    & "$area\tests.exe" $area
    if ($LASTEXITCODE -ne 0) { throw 'Core regression failed' }
    & $compiler /nologo /target:winexe /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/out:$area\package\AntigravityProxyLauncher.exe" $source
    if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
    & $compiler /nologo /target:winexe "/out:$area\app\Antigravity.exe" (Join-Path $root 'tests\launcher_mock.cs')
    if ($LASTEXITCODE -ne 0) { throw 'Mock compilation failed' }
    $env:LAUNCHER_TEST_CAPTURE = Join-Path $area 'arguments.txt'
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = "$area\package\AntigravityProxyLauncher.exe"
    $info.UseShellExecute = $false
    $info.WorkingDirectory = $area
    $info.Arguments = '--quiet --target "' + "$area\app\Antigravity.exe" + '" -- "space path" "" "quote\"inside" "trailing\\"'
    $launcher = [Diagnostics.Process]::Start($info)
    if (-not $launcher.WaitForExit(5000) -or $launcher.ExitCode -ne 0) { throw 'Launcher failed or stayed resident' }
    for ($i=0; $i -lt 30 -and -not (Test-Path $env:LAUNCHER_TEST_CAPTURE); $i++) { Start-Sleep -Milliseconds 100 }
    $actual = @(Get-Content -LiteralPath $env:LAUNCHER_TEST_CAPTURE)
    $expected = @($area, 'space path', '', 'quote"inside', 'trailing\')
    if (($actual | ConvertTo-Json -Compress) -ne ($expected | ConvertTo-Json -Compress)) { throw 'Argument/working directory forwarding failed' }
    $mock = @(Get-Process Antigravity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq "$area\app\Antigravity.exe" })
    if ($mock.Count -eq 0) { throw 'Mock must still be running after launcher exits' }
    # Exercise real process inspection through the executable, not only the injected predicate.
    Remove-Item -LiteralPath "$area\app\config.json"
    $info.Arguments = '--quiet --repair-only --target "' + "$area\app\Antigravity.exe" + '"'
    $guard = [Diagnostics.Process]::Start($info)
    if (-not $guard.WaitForExit(2000) -or $guard.ExitCode -ne 1) { throw 'Running target was not refused' }
    if (Test-Path -LiteralPath "$area\app\config.json") { throw 'Running target was modified' }
    Write-Host 'Launcher integration: arguments, working directory and immediate exit passed.'
    # Only wait for our mock process, not any real installation.
    Get-Process Antigravity -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq "$area\app\Antigravity.exe" } | Wait-Process -Timeout 10
    $repair = [Diagnostics.Process]::Start($info)
    if (-not $repair.WaitForExit(5000) -or $repair.ExitCode -ne 0) { throw 'Repair-only failed after application exit' }
    if (-not (Test-Path -LiteralPath "$area\app\config.json")) { throw 'Config not restored' }
    $info.Arguments = '--quiet --target "' + "$area\absent\Antigravity.exe" + '"'
    $invalid = [Diagnostics.Process]::Start($info)
    if (-not $invalid.WaitForExit(5000) -or $invalid.ExitCode -ne 1) { throw 'Missing target exit code incorrect' }
    Write-Host 'Launcher integration: real running-process refusal, repair-only and missing target passed.'
} finally {
    $env:LAUNCHER_TEST_CAPTURE = $previousCapture
    $resolved = [IO.Path]::GetFullPath($area)
    $prefix = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\antigravity-launcher-test-'
    if ($resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}
