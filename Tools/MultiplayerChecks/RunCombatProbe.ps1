param([string]$From='TestResults/Multiplayer/Live/678545bb00ac',[string]$ServerExe,[string]$ClientExe,[switch]$Graphics)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$ServerExe){$ServerExe=Join-Path $taskRoot 'Builds/WindowsServer/SyntheticFrontServer.exe'}
$pair=& (Join-Path $PSScriptRoot 'NewProfilePair.ps1') -From $From|ConvertFrom-Json
if($pair.players -ne 2){throw 'Combat validation requires two registered diagnostic characters'}
$fixture=$pair.before;$first=Get-Content -LiteralPath (Join-Path $fixture 'client-1.json') -Raw|ConvertFrom-Json;$second=Get-Content -LiteralPath (Join-Path $fixture 'client-2.json') -Raw|ConvertFrom-Json;$specPath=Join-Path $fixture 'combat-spec.json'
@{version=1;seeded=$false;configPath=(Join-Path $fixture 'server.json');shooter=[long]$first.actor;victim=[long]$second.actor}|ConvertTo-Json|Set-Content -LiteralPath $specPath -Encoding utf8
$summary=@{status='STARTING';fixture=$fixture;scope='Authenticated production Relay combat, four authoritative shots, replay rejection, death container, owned respawn, inventory recovery and reconnect. Resources and a flat contact fixture prepared offline. No GUI, audible voice or gameplay capacity acceptance.'}
function Count-Item($actor,[string]$item){[long]$count=0;foreach($stack in $actor.inventory.items.ItemStack){if($stack.item -eq $item){$count+=[long]$stack.count}};return $count}
try{
    $seed=Start-Process -FilePath $ServerExe -ArgumentList @('-batchmode','-nographics','-survival-combat-seed',('"'+$specPath+'"'),'-logFile',('"'+(Join-Path $fixture 'seed.log')+'"')) -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew();while(!$seed.HasExited -and $timer.Elapsed.TotalSeconds -lt 120){Start-Sleep -Milliseconds 500}
    if(!$seed.HasExited){Stop-Process -InputObject $seed -Force;throw 'Combat fixture setup timed out'}
    $spec=Get-Content -LiteralPath $specPath -Raw|ConvertFrom-Json;if($seed.ExitCode -ne 0 -or !$spec.seeded){throw "Combat setup failed: $($spec.error)"}
    $arguments=@{ServerExe=$ServerExe;ResumeFrom=$fixture;ClientCount=2;CombatSpec=$specPath}
    if($Graphics){if($ClientExe){$arguments.ClientExe=$ClientExe}}else{$arguments.Headless=$true;if($ClientExe){$arguments.ClientExe=$ClientExe}}
    $lines=& (Join-Path $PSScriptRoot 'RunSessionProbe.ps1') @arguments
    $report=($lines|Where-Object {$_ -match '^Session probe PASS; reports: '}|Select-Object -Last 1) -replace '^Session probe PASS; reports: ',''
    if(!$report){throw 'No passing combat report'};$summary.report=$report
    foreach($index in 1,2){$r=Get-Content -LiteralPath (Join-Path $report "client-$index.json") -Raw|ConvertFrom-Json;if(!$r.combatRequired -or !$r.combatKilled -or !$r.combatReplay -or !$r.combatReconnect){throw 'Combat/replay/reconnect assertions missing'};if([long]$r.actor -eq [long]$spec.shooter){if($r.combatShots -ne 4){throw 'Shooter did not complete exactly four accepted shots'}}elseif([long]$r.actor -eq [long]$spec.victim){if(!$r.combatRevived -or !$r.combatRecovered){throw 'Victim did not revive and recover its dropped items'}}else{throw 'Unexpected combat identity'}}
    [xml]$saved=Get-Content -LiteralPath (Join-Path $fixture 'industry.xml');$shooter=$saved.IndustrialWorld.actors.IndustrialActor|Where-Object {[long]$_.id -eq [long]$spec.shooter};$victim=$saved.IndustrialWorld.actors.IndustrialActor|Where-Object {[long]$_.id -eq [long]$spec.victim}
    if((Count-Item $shooter 'ammo-9mm') -ne 2 -or (Count-Item $victim 'copper-bar') -ne 3 -or [double]$victim.health -le 0){throw 'Saved ammunition, revived character or recovered inventory is inconsistent'}
    $drops=@($saved.IndustrialWorld.droppedItems.DroppedInventory|Where-Object {$_});if($drops.Count -gt 1 -or ($drops.Count -eq 1 -and @($drops[0].inventory.items.ItemStack|Where-Object {$_}).Count -ne 0)){throw 'Death/recovery duplicated a container or left recovered contents behind'}
    $summary.status='PASS';$summary.persistedConservation=$true
}catch{$summary.status='FAIL';$summary.error=$_.Exception.Message;throw}
finally{$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $fixture 'combat-result.json') -Encoding utf8;Write-Host "Combat probe $($summary.status): $fixture"}
