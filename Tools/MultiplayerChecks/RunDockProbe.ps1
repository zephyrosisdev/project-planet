param([string]$From='TestResults/Multiplayer/Live/a04287057cb0',[string]$ServerExe,[string]$ClientExe,[switch]$Graphics,[ValidateRange(0,500)][int]$OneWayDelayMs=0,[ValidateRange(0,250)][int]$JitterMs=0,[ValidateRange(0,15)][int]$LossPercent=0)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$ServerExe){$ServerExe=Join-Path $taskRoot 'Builds/WindowsServer/SyntheticFrontServer.exe'}
if($Graphics -and !$ClientExe){$ClientExe=Join-Path $taskRoot 'TestResults/Multiplayer/GraphicsProbeClient/SyntheticFrontGraphicsProbe.exe'}
foreach($exe in @($ServerExe,$ClientExe)|Where-Object {$_}){$marker=Join-Path (Split-Path -Parent $exe) 'dock-probe.version';if(!(Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw).Trim() -ne '1'){throw 'Rebuild the dedicated/graphics fixture with the station docking probe first'}}
$pair=& (Join-Path $PSScriptRoot 'NewProfilePair.ps1') -From $From|ConvertFrom-Json
if($pair.players -ne 2){throw 'Dock validation requires a completed two-client diagnostic source'}
$fixture=$pair.before;$owner=Get-Content -LiteralPath (Join-Path $fixture 'client-1.json') -Raw|ConvertFrom-Json;$passenger=Get-Content -LiteralPath (Join-Path $fixture 'client-2.json') -Raw|ConvertFrom-Json
$specPath=Join-Path $fixture 'dock-spec.json'
@{version=1;seeded=$false;owner=[long]$owner.actor;passenger=[long]$passenger.actor;configPath=(Join-Path $fixture 'server.json')}|ConvertTo-Json|Set-Content -LiteralPath $specPath -Encoding utf8
$summary=@{status='STARTING';fixture=$fixture;scope='Two authenticated production Relay clients exercising orbital station mooring, passenger authority rejection, undock, predicted thrust approach, berth reacquisition and ship-local reconnect. Offline-prepared station and character positions; no physical UI/voice, capacity or rendered frame-time acceptance.'}
try{
    $seed=Start-Process -FilePath $ServerExe -ArgumentList @('-batchmode','-nographics','-survival-dock-seed',('"'+$specPath+'"'),'-logFile',('"'+(Join-Path $fixture 'seed.log')+'"')) -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew();while(!$seed.HasExited -and $timer.Elapsed.TotalSeconds -lt 120){Start-Sleep -Milliseconds 500}
    if(!$seed.HasExited){Stop-Process -InputObject $seed -Force;throw 'Dock fixture seeding timed out'}
    $spec=Get-Content -LiteralPath $specPath -Raw|ConvertFrom-Json;if($seed.ExitCode -ne 0 -or !$spec.seeded){throw "Dock seed failed: $($spec.error)"}
    $arguments=@{ServerExe=$ServerExe;ResumeFrom=$fixture;ClientCount=2;DockSpec=$specPath;OneWayDelayMs=$OneWayDelayMs;JitterMs=$JitterMs;LossPercent=$LossPercent}
    if($Graphics){$arguments.ClientExe=$ClientExe}else{$arguments.Headless=$true;if($ClientExe){$arguments.ClientExe=$ClientExe}}
    $lines=& (Join-Path $PSScriptRoot 'RunSessionProbe.ps1') @arguments
    $result=($lines|Where-Object {$_ -match '^Session probe PASS; reports: '}|Select-Object -Last 1) -replace '^Session probe PASS; reports: ',''
    if(!$result){throw 'Dock clients did not return a passing report'};$summary.report=$result
    [xml]$saved=Get-Content -LiteralPath (Join-Path $fixture 'industry.xml');$stations=@($saved.IndustrialWorld.multiplayer.stations.SurvivalStationState);$ships=@($saved.IndustrialWorld.multiplayer.ships.SurvivalShipState)
    if([int]$saved.IndustrialWorld.multiplayer.contentVersion -ne 2){throw 'Station-aware checkpoint did not retain the save-content version guard'}
    $station=$stations|Where-Object {[long]$_.entity -eq [long]$spec.station};$ship=$ships|Where-Object {[long]$_.entity -eq [long]$spec.ship}
    if(!$station -or !$ship -or [long]$station.dockedShip -ne [long]$ship.entity -or [long]$ship.dockedStation -ne [long]$station.entity){throw 'Checkpoint lost the station berth reservation or moored ship'}
    $summary.status='PASS';$summary.persistedMooring=$true
}catch{$summary.status='FAIL';$summary.error=$_.Exception.Message;throw}
finally{$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $fixture 'dock-result.json') -Encoding utf8;Write-Host "Dock probe $($summary.status): $fixture"}
