param([string]$From='TestResults/Multiplayer/Live/a04287057cb0',[string]$ServerExe,[string]$ClientExe,[switch]$Graphics,[ValidateRange(0,500)][int]$OneWayDelayMs=0,[ValidateRange(0,250)][int]$JitterMs=0,[ValidateRange(0,15)][int]$LossPercent=0)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$ServerExe){$ServerExe=Join-Path $taskRoot 'Builds/WindowsServer/SyntheticFrontServer.exe'}
if($Graphics -and !$ClientExe){$ClientExe=Join-Path $taskRoot 'TestResults/Multiplayer/GraphicsProbeClient/SyntheticFrontGraphicsProbe.exe'}
$pair=& (Join-Path $PSScriptRoot 'NewProfilePair.ps1') -From $From|ConvertFrom-Json
if($pair.players -ne 2){throw 'Interior validation requires a completed two-client diagnostic source'}
$fixture=$pair.before;$owner=Get-Content -LiteralPath (Join-Path $fixture 'client-1.json') -Raw|ConvertFrom-Json;$passenger=Get-Content -LiteralPath (Join-Path $fixture 'client-2.json') -Raw|ConvertFrom-Json
$specPath=Join-Path $fixture 'interior-spec.json'
@{version=1;seeded=$false;owner=[long]$owner.actor;passenger=[long]$passenger.actor;configPath=(Join-Path $fixture 'server.json')}|ConvertTo-Json|Set-Content -LiteralPath $specPath -Encoding utf8
$summary=@{status='STARTING';fixture=$fixture;scope='Real authenticated production transport with two registered characters, ship-local walking, hatch collision, owner pilot, moving parent and reconnect; offline-prepared invitations only. No physical UI/voice, capacity or rendered frame-time acceptance.'}
try{
    $seed=Start-Process -FilePath $ServerExe -ArgumentList @('-batchmode','-nographics','-survival-interior-seed',('"'+$specPath+'"'),'-logFile',('"'+(Join-Path $fixture 'seed.log')+'"')) -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew();while(!$seed.HasExited -and $timer.Elapsed.TotalSeconds -lt 120){Start-Sleep -Milliseconds 500}
    if(!$seed.HasExited){Stop-Process -InputObject $seed -Force;throw 'Interior fixture seeding timed out'}
    $spec=Get-Content -LiteralPath $specPath -Raw|ConvertFrom-Json;if($seed.ExitCode -ne 0 -or !$spec.seeded){throw "Interior seed failed: $($spec.error)"}
    $arguments=@{ServerExe=$ServerExe;ResumeFrom=$fixture;ClientCount=2;InteriorSpec=$specPath;OneWayDelayMs=$OneWayDelayMs;JitterMs=$JitterMs;LossPercent=$LossPercent}
    if($Graphics){$arguments.ClientExe=$ClientExe}else{$arguments.Headless=$true;if($ClientExe){$arguments.ClientExe=$ClientExe}}
    $lines=& (Join-Path $PSScriptRoot 'RunSessionProbe.ps1') @arguments
    $result=($lines|Where-Object {$_ -match '^Session probe PASS; reports: '}|Select-Object -Last 1) -replace '^Session probe PASS; reports: ',''
    if(!$result){throw 'Interior clients did not return a passing report'};$summary.report=$result
    [xml]$saved=Get-Content -LiteralPath (Join-Path $fixture 'industry.xml');$players=@($saved.IndustrialWorld.multiplayer.players.SurvivalPlayerRecord)
    foreach($id in @([long]$spec.owner,[long]$spec.passenger)){$player=$players|Where-Object {[long]$_.actor -eq $id};if(!$player -or $player.interiorWalking -ne 'true' -or [long]$player.attachedShip -ne [long]$spec.ship -or [long]$player.controlledShip -ne 0 -or [long]$player.interiorPose.frame -ne [long]$spec.ship){throw 'Final checkpoint lost walking attachment, pilot release or parent coordinates'}}
    $summary.status='PASS';$summary.persistedAttachments=$true
}catch{$summary.status='FAIL';$summary.error=$_.Exception.Message;throw}
finally{$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $fixture 'interior-result.json') -Encoding utf8;Write-Host "Interior probe $($summary.status): $fixture"}
