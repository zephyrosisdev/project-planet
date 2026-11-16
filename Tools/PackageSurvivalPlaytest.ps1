param([Parameter(Mandatory=$true)][string]$ClientSource,[Parameter(Mandatory=$true)][string]$ServerSource,[switch]$Zip,[switch]$IncludeTestClock,[switch]$IncludeWeatherTrace,[ValidateRange(0,1000)][int]$WorldScale=0)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$clientSourcePath=(Resolve-Path -LiteralPath $ClientSource).Path
$serverSourcePath=(Resolve-Path -LiteralPath $ServerSource).Path
if(Test-Path -LiteralPath (Join-Path $clientSourcePath 'graphics-probe-only')){throw 'Package the normal client, not a graphics diagnostic fixture'}
$targets=@(@{name='Client';source=$clientSourcePath;exe='SyntheticFront.exe'},@{name='Server';source=$serverSourcePath;exe='SyntheticFrontServer.exe'})
foreach($target in $targets){foreach($required in @($target.exe,'UnityPlayer.dll',([IO.Path]::GetFileNameWithoutExtension($target.exe)+'_Data/Managed/Assembly-CSharp.dll'))){if(!(Test-Path -LiteralPath (Join-Path $target.source $required))){throw "Incomplete $($target.name) build: $required"}}}
$clientAssembly=Join-Path $clientSourcePath 'SyntheticFront_Data/Managed/Assembly-CSharp.dll'
if([Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($clientAssembly)).Contains('SurvivalSessionProbe')){throw 'The client contains opt-in session diagnostics; use the normal client build'}
if($IncludeWeatherTrace -and ![Text.Encoding]::UTF8.GetString([IO.File]::ReadAllBytes($clientAssembly)).Contains('WeatherHistory')){throw 'Weather recording requires a WeatherTraceClient build'}
$output=Join-Path $taskRoot ('Builds/Playtest-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss'))
if(Test-Path -LiteralPath $output){throw 'Playtest output already exists'}
[IO.Directory]::CreateDirectory($output)|Out-Null
$protocol=[regex]::Match((Get-Content -LiteralPath (Join-Path $taskRoot 'Assets/MassiveArmy/Runtime/Industrial/IndustrialMultiplayer.cs') -Raw),'const int Protocol=(\d+)');if(!$protocol.Success){throw 'Could not identify the source protocol'}
$content=[regex]::Match((Get-Content -LiteralPath (Join-Path $taskRoot 'Assets/MassiveArmy/Runtime/Industrial/IndustrialMultiplayer.cs') -Raw),'const string Content="([^"]+)"');if(!$content.Success){throw 'Could not identify the source content version'}
$manifest=@{protocol=[int]$protocol.Groups[1].Value;content=$content.Groups[1].Value;createdUtc=[DateTime]::UtcNow.ToString('o');intendedUse='Two-player development playtest';capacity='100-player and full gameplay capacity are unvalidated';performance='Survival 60 FPS acceptance is unvalidated; RTS/army benchmarks are retired';artifacts=@()}
foreach($target in $targets){
    $destination=Join-Path $output $target.name;[IO.Directory]::CreateDirectory($destination)|Out-Null
    $dataName=[IO.Path]::GetFileNameWithoutExtension($target.exe)+'_Data'
    $runtimeDlls=@(Get-ChildItem -LiteralPath $target.source -Filter '*.dll' -File|ForEach-Object {$_.Name})
    foreach($name in (@($target.exe,'UnityCrashHandler64.exe','MonoBleedingEdge','D3D12','survival-only','world-preset.example.json',$dataName)+$runtimeDlls)){$source=Join-Path $target.source $name;if(Test-Path -LiteralPath $source){Copy-Item -LiteralPath $source -Destination $destination -Recurse}}
    Copy-Item -LiteralPath (Join-Path $taskRoot 'Documentation/MULTIPLAYER_PLAYTEST.md') -Destination (Join-Path $destination 'README.md')
    Copy-Item -LiteralPath (Join-Path $taskRoot 'Documentation/SHARED_WORLD_PRESETS.md') -Destination $destination
    if($target.name -eq 'Client' -and $IncludeWeatherTrace){Copy-Item -LiteralPath (Join-Path $taskRoot 'Tools/MultiplayerChecks/RunWeatherTrace.bat') -Destination (Join-Path $destination 'RunWeatherTrace.bat')}
    if($target.name -eq 'Client'){Copy-Item -LiteralPath (Join-Path $taskRoot 'Tools/MultiplayerChecks/RunSinglePlayer-LargeWorld.bat') -Destination $destination;if($WorldScale -gt 0){$launcher=(Get-Content -LiteralPath (Join-Path $taskRoot 'Tools/MultiplayerChecks/RunSinglePlayer-LargeWorld.bat') -Raw).Replace('large-test-100',"large-test-$WorldScale").Replace('-planet-size-multiplier 100',"-planet-size-multiplier $WorldScale");Set-Content -LiteralPath (Join-Path $destination 'RunSinglePlayer.bat') -Value $launcher -Encoding ascii}}
    if($target.name -eq 'Server'){
        $config=Get-Content -LiteralPath (Join-Path $taskRoot 'Server/server.example.json') -Raw|ConvertFrom-Json
        $config.maxPlayers=2;$config.displayName='Friends Survival';$config.worldId='friends-survival';$config.instanceId='friends-1';$config.savePath=''
        if($WorldScale -gt 0){$config.systemSizeMultiplier=$WorldScale;$config.worldId="friends-survival-$($WorldScale)x";$config.instanceId="friends-$($WorldScale)x";$config.displayName="Friends Survival $($WorldScale)x"}
        $config|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $destination 'server.json') -Encoding utf8
        Copy-Item -LiteralPath (Join-Path $taskRoot 'Tools/MultiplayerChecks/RunServerLargeWorld.ps1'),(Join-Path $taskRoot 'Tools/MultiplayerChecks/RunServer-LargeWorld.bat') -Destination $destination
        @'
@echo off
setlocal
set "SERVER_CONFIG=%~1"
if not defined SERVER_CONFIG set "SERVER_CONFIG=%~dp0server.json"
"%~dp0SyntheticFrontServer.exe" -batchmode -nographics -server-config "%SERVER_CONFIG%" -logFile -
exit /b %errorlevel%
'@|Set-Content -LiteralPath (Join-Path $destination 'RunServer.bat') -Encoding ascii
        if($IncludeTestClock){
            Copy-Item -LiteralPath (Join-Path $taskRoot 'Tools/MultiplayerChecks/RunServerTestClock.ps1') -Destination (Join-Path $destination 'RunServerTestClock.ps1')
            @'
@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0RunServerTestClock.ps1" %*
exit /b %errorlevel%
'@|Set-Content -LiteralPath (Join-Path $destination 'RunServer-TestClock.bat') -Encoding ascii
        }
    }
    $assembly=Join-Path $destination ($dataName+'/Managed/Assembly-CSharp.dll')
    $manifest.artifacts+=@{name=$target.name;executable=$target.exe;assemblySha256=(Get-FileHash -LiteralPath $assembly -Algorithm SHA256).Hash;executableSha256=(Get-FileHash -LiteralPath (Join-Path $destination $target.exe) -Algorithm SHA256).Hash}
}
$manifest|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $output 'build-manifest.json') -Encoding utf8
foreach($target in $targets){Copy-Item -LiteralPath (Join-Path $output 'build-manifest.json') -Destination (Join-Path $output ($target.name+'/build-manifest.json'))}
if($Zip){
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    foreach($target in $targets){
        $archive=Join-Path $output ($target.name+'.zip')
        [IO.Compression.ZipFile]::CreateFromDirectory((Join-Path $output $target.name),$archive,[IO.Compression.CompressionLevel]::Optimal,$true)
        $hash=(Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash
        "$hash  $($target.name).zip"|Set-Content -LiteralPath ($archive+'.sha256') -Encoding ascii
    }
}
Write-Output $output
