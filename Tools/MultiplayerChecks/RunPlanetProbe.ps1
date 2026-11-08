param([string]$From='TestResults/Multiplayer/Live/a04287057cb0',[string]$ServerExe,[string]$ClientExe,[switch]$Graphics)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$ServerExe){$ServerExe=Join-Path $taskRoot 'Builds/WindowsServer/SyntheticFrontServer.exe'}
$pair=& (Join-Path $PSScriptRoot 'NewProfilePair.ps1') -From $From|ConvertFrom-Json
if($pair.players -ne 2){throw 'Planet validation requires a completed two-character diagnostic source'}
$fixture=$pair.before;$owner=Get-Content -LiteralPath (Join-Path $fixture 'client-1.json') -Raw|ConvertFrom-Json;$guest=Get-Content -LiteralPath (Join-Path $fixture 'client-2.json') -Raw|ConvertFrom-Json;$specPath=Join-Path $fixture 'planet-spec.json'
@{version=1;seeded=$false;actor=[long]$guest.actor;configPath=(Join-Path $fixture 'server.json')}|ConvertTo-Json|Set-Content -LiteralPath $specPath -Encoding utf8
$summary=@{status='STARTING';fixture=$fixture;scope='Two authenticated production Relay clients independently walking and reconnecting on different canonical planets; offline fixture placement only. No full travel, rendered performance or gameplay capacity acceptance.'}
try{
    $seed=Start-Process -FilePath $ServerExe -ArgumentList @('-batchmode','-nographics','-survival-planet-seed',('"'+$specPath+'"'),'-logFile',('"'+(Join-Path $fixture 'seed.log')+'"')) -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew();while(!$seed.HasExited -and $timer.Elapsed.TotalSeconds -lt 120){Start-Sleep -Milliseconds 500}
    if(!$seed.HasExited){Stop-Process -InputObject $seed -Force;throw 'Planet fixture setup timed out'}
    $spec=Get-Content -LiteralPath $specPath -Raw|ConvertFrom-Json;if($seed.ExitCode -ne 0 -or !$spec.seeded){throw "Planet setup failed: $($spec.error)"}
    $arguments=@{ServerExe=$ServerExe;ResumeFrom=$fixture;ClientCount=2;SeparatedRegions=$true;HoldSeconds=10}
    if($Graphics){if($ClientExe){$arguments.ClientExe=$ClientExe}}else{$arguments.Headless=$true;if($ClientExe){$arguments.ClientExe=$ClientExe}}
    $lines=& (Join-Path $PSScriptRoot 'RunSessionProbe.ps1') @arguments
    $report=($lines|Where-Object {$_ -match '^Session probe PASS; reports: '}|Select-Object -Last 1) -replace '^Session probe PASS; reports: ',''
    if(!$report){throw 'No passing planet report'};$summary.report=$report
    [xml]$saved=Get-Content -LiteralPath (Join-Path $fixture 'industry.xml');$bodies=@($saved.IndustrialWorld.actors.IndustrialActor|Where-Object {[long]$_.id -in @([long]$guest.actor,[long]$owner.actor)}|ForEach-Object {[int]$_.pose.body}|Select-Object -Unique)
    if($bodies.Count -ne 2 -or [int]$spec.body -notin $bodies){throw 'Checkpoint lost independent canonical planetary identities'}
    $summary.status='PASS';$summary.persistedRegions=$true
}catch{$summary.status='FAIL';$summary.error=$_.Exception.Message;throw}
finally{$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $fixture 'planet-result.json') -Encoding utf8;Write-Host "Planet probe $($summary.status): $fixture"}
