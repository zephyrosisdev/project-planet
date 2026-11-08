param(
    [string]$From='TestResults/Multiplayer/Live/a04287057cb0',
    [string]$ServerExe,
    [string]$ClientExe,
    [switch]$Graphics,
    [switch]$LargeSnapshots,
    [switch]$Shipyard,
    [switch]$Buildings
)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$ServerExe){$ServerExe=Join-Path $taskRoot 'Builds/WindowsServer/SyntheticFrontServer.exe'}
if($Graphics -and !$ClientExe){$ClientExe=Join-Path $taskRoot 'TestResults/Multiplayer/GraphicsProbeClient/SyntheticFrontGraphicsProbe.exe'}
$pair=& (Join-Path $PSScriptRoot 'NewProfilePair.ps1') -From $From|ConvertFrom-Json
if($pair.players -ne 2){throw 'Factory validation needs an original completed two-client diagnostic run'}
$fixture=$pair.before;$specPath=Join-Path $fixture 'factory-spec.json'
$owner=Get-Content -LiteralPath (Join-Path $fixture 'client-1.json') -Raw|ConvertFrom-Json
$observer=Get-Content -LiteralPath (Join-Path $fixture 'client-2.json') -Raw|ConvertFrom-Json
@{version=1;seeded=$false;largeSnapshots=[bool]$LargeSnapshots;shipyard=[bool]$Shipyard;buildings=[bool]$Buildings;configPath=(Join-Path $fixture 'server.json');owner=[long]$owner.actor;observer=[long]$observer.actor;initialPlates=100;initialGears=100;cargo=4;cargoActivity=.125}|ConvertTo-Json|Set-Content -LiteralPath $specPath -Encoding utf8
$summary=@{status='STARTING';fixture=$fixture;scope='Real authenticated factory transport commands on an offline-prepared diagnostic world. No admission bypass. Native/graphical fixture only, not GUI, progression or full-load acceptance.'}
function Quote-Argument([string]$value){if($value.Contains('"')){throw 'Invalid quote in argument'};'"'+$value+'"'}
function Run-Phase([string]$phase){
    $arguments=@{ServerExe=$ServerExe;ResumeFrom=$fixture;ClientCount=2;FactorySpec=$specPath;FactoryPhase=$phase}
    if($Graphics){$arguments.ClientExe=$ClientExe}else{$arguments.Headless=$true;if($ClientExe){$arguments.ClientExe=$ClientExe}}
    $lines=& (Join-Path $PSScriptRoot 'RunSessionProbe.ps1') @arguments
    $run=$null;foreach($line in $lines){Write-Host $line;if($line -match '^Session probe PASS; reports: (.+)$'){$run=$Matches[1]}}
    if(!$run){throw "Missing completed $phase report"};return $run
}
function Item-Count($inventory,[string]$item){[long]$count=0;foreach($stack in $inventory.items.ItemStack){if($stack -and $stack.item -eq $item){$count+=[long]$stack.count}};return $count}
function Check-World([bool]$recovered){
    [xml]$saved=Get-Content -LiteralPath (Join-Path $fixture 'industry.xml');$world=$saved.IndustrialWorld
    $actor=$world.actors.IndustrialActor|Where-Object {[long]$_.id -eq [long]$spec.owner}
    $target=$world.machines.IndustrialMachine|Where-Object {[long]$_.id -eq [long]$spec.target}
    $source=$world.machines.IndustrialMachine|Where-Object {[long]$_.id -eq [long]$spec.source}
    $consumer=$world.machines.IndustrialMachine|Where-Object {[long]$_.id -eq [long]$spec.consumer}
    $pump=$world.machines.IndustrialMachine|Where-Object {[long]$_.id -eq [long]$spec.pump}
    $belts=@($world.belts.ConveyorLane|Where-Object {$_})
    $spent=if($recovered){0}else{[long]$spec.beltCost}
    if((Item-Count $actor.inventory 'plate') -ne (100-$spent) -or (Item-Count $actor.inventory 'gear') -ne (100-$spent-$(if($spec.buildings -and !$recovered){1}else{0}))){throw 'Saved factory material conservation failed'}
    if((Item-Count $target.input 'iron-bar') -ne 4 -or (Item-Count $source.input 'iron-bar') -ne 0){throw 'Saved conveyor transport conservation failed'}
    [double]$activity=0;foreach($stack in $target.input.items.ItemStack){if($stack.item -eq 'iron-bar'){$activity+=[double]$stack.activity}}
    if([Math]::Abs($activity-[double]$spec.cargoActivity) -gt 1e-8){throw 'Saved cargo activity was not conserved'}
    if($recovered){if($belts.Count -ne 0 -or [long]$consumer.powerSource -ne 0 -or [long]$pump.waterTarget -ne 0){throw 'Recovered connections remained in the checkpoint'}}
    elseif($belts.Count -ne 1 -or [long]$consumer.powerSource -ne [long]$spec.generator -or [long]$pump.waterTarget -ne [long]$spec.waterTarget){throw 'Created factory connections did not persist'}
    if($spec.buildings){
        $extra=if($recovered){0}else{1};$parts=@();foreach($savedBody in $world.buildStates.ConstructionStorageRecord){if($savedBody){$storage=$savedBody.json|ConvertFrom-Json;foreach($site in $storage.playerBuildSites){if([long]$site.owner -eq [long]$spec.owner){$parts+=@($site.modules)}}}}
        if($parts.Count -ne (3*$extra) -or (Item-Count $actor.inventory 'stone') -ne (4-2*$extra) -or (Item-Count $actor.inventory 'iron-bar') -ne (12-6*$extra)){throw 'Saved building structure or material conservation failed'}
        if(!$recovered -and !($parts|Where-Object {$_.kind -eq 7 -and $_.open})){throw 'Open door did not persist'}
    }
    if($spec.shipyard){
        $ship=$world.ships.ShipAssembly|Where-Object {[long]$_.owner -eq [long]$spec.owner};$modules=@($ship.modules.ShipModuleRecord|Where-Object {$_});$wires=@($ship.wires.ShipPowerWire|Where-Object {$_});$extra=if($recovered){0}else{1}
        if($modules.Count -ne ([int]$spec.shipModules+$extra) -or $wires.Count -ne ([int]$spec.shipWires+$extra)){throw 'Saved shipyard layout/connectivity mismatch'}
        if((Item-Count $actor.inventory 'hull-panel') -ne (24-12*$extra) -or (Item-Count $actor.inventory 'electronics') -ne (16-8*$extra) -or (Item-Count $actor.inventory 'wire') -ne (100-[long]$spec.shipWireCost*$extra)){throw 'Saved shipyard material conservation failed'}
    }
}
try{
    $seed=Start-Process -FilePath $ServerExe -ArgumentList @('-batchmode','-nographics','-survival-factory-seed',(Quote-Argument $specPath),'-logFile',(Quote-Argument (Join-Path $fixture 'seed.log'))) -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru
    $timer=[Diagnostics.Stopwatch]::StartNew();while(!$seed.HasExited -and $timer.Elapsed.TotalSeconds -lt 120){Start-Sleep -Milliseconds 500}
    if(!$seed.HasExited){Stop-Process -InputObject $seed -Force;throw 'Offline factory setup timed out'}
    $spec=Get-Content -LiteralPath $specPath -Raw|ConvertFrom-Json;if($seed.ExitCode -ne 0 -or !$spec.seeded){throw "Offline factory setup failed: $($spec.error)"}
    Write-Host 'Offline fixture prepared; starting normal authenticated clients.'
    $summary.create=Run-Phase 'create';Check-World $false
    foreach($index in 1,2){Copy-Item -LiteralPath (Join-Path $summary.create "client-$index.json") -Destination (Join-Path $fixture "client-$index.json")}
    $summary.recover=Run-Phase 'recover';Check-World $true
    $summary.status='PASS';$summary.persistedConservation=$true
}catch{$summary.status='FAIL';$summary.error=$_.Exception.Message;throw}
finally{$summary|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $fixture 'factory-result.json') -Encoding utf8;Write-Host "Factory probe $($summary.status): $fixture"}
