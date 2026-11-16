param([string]$ConfigPath=(Join-Path $PSScriptRoot 'server.json'),[switch]$PrepareOnly)
$ErrorActionPreference='Stop'
$source=(Resolve-Path -LiteralPath $ConfigPath).Path
$config=Get-Content -LiteralPath $source -Raw|ConvertFrom-Json
$config|Add-Member -NotePropertyName allowTestTimeControls -NotePropertyValue $true -Force
$config|Add-Member -NotePropertyName testTimeScale -NotePropertyValue 1 -Force
$target=Join-Path (Split-Path -Parent $source) 'server.test-clock.json'
if($target.Equals($source,[StringComparison]::OrdinalIgnoreCase)){throw 'Choose the original server.json, not the generated test-clock configuration'}
$config|ConvertTo-Json -Depth 16|Set-Content -LiteralPath $target -Encoding utf8
if($PrepareOnly){Write-Output $target;return}
$server=Join-Path $PSScriptRoot 'SyntheticFrontServer.exe'
if(!(Test-Path -LiteralPath $server)){throw 'Place this script beside the dedicated server executable'}
Write-Host 'Test clock enabled at 1x. Connected testers can use numpad + / - (up to 8x).'
& $server -batchmode -nographics -server-config $target -logFile -
exit $LASTEXITCODE
