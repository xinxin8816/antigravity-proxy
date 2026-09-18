[CmdletBinding()]
param([string]$OutputDir = (Join-Path (Split-Path $PSScriptRoot -Parent) 'output'))
$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework C# compiler not found.' }
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'tools\launcher\Launcher.cs'
$dll = Join-Path $OutputDir 'ide\version.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw 'Build the IDE DLL first.' }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll "/out:$OutputDir\AntigravityProxyLauncher.exe" $source
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }
(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash | Set-Content -LiteralPath (Join-Path $OutputDir 'version.dll.sha256') -Encoding ASCII
Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'docs\launcher.md') -Destination (Join-Path $OutputDir 'launcher.md') -Force
