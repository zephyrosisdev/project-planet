param(
    [string]$ClientExe,
    [string]$ServerExe,
    [switch]$ServerOnly,
    [switch]$Headless,
    [string]$ResumeFrom,
    [switch]$Chat,
    [switch]$SeparatedRegions,
    [switch]$ListingRecovery,
    [ValidateRange(0,30)][int]$JoinCodeDelaySeconds=0,
    [ValidateRange(2,100)][int]$ClientCount=2,
    [ValidateRange(0,90)][int]$HoldSeconds=0,
    [ValidateSet('4','5','6','7','8','9','10','11')][string]$ProbeVersion='11',
    [ValidateRange(0,500)][int]$OneWayDelayMs=0,
    [ValidateRange(0,250)][int]$JitterMs=0,
    [ValidateRange(0,15)][int]$LossPercent=0,
    [string]$InteriorSpec,
    [string]$CombatSpec,
    [string]$FactorySpec,
    [ValidateSet('create','recover')][string]$FactoryPhase='create'
)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$extraWait=if($InteriorSpec){300}elseif($FactorySpec -and ((Get-Content -LiteralPath $FactorySpec -Raw|ConvertFrom-Json).shipyard -or (Get-Content -LiteralPath $FactorySpec -Raw|ConvertFrom-Json).buildings)){600}else{0}
if($InteriorSpec -and ($FactorySpec -or $Chat -or $ListingRecovery -or $ServerOnly -or $ClientCount -ne 2 -or !$ResumeFrom -or $ProbeVersion -ne '11')){throw 'Interior probe needs a current isolated two-client resume fixture'}
if($FactorySpec -and ($Chat -or $ListingRecovery -or $JoinCodeDelaySeconds -gt 0 -or $ServerOnly -or $ClientCount -ne 2 -or !$ResumeFrom -or $ProbeVersion -ne '11')){throw 'Factory probe requires two matching current clients and an isolated resume fixture'}
if($ListingRecovery){if($ServerOnly -or $ResumeFrom -or $ProbeVersion -ne '11'){throw 'Listing recovery requires a new current-build disposable session with clients'};$HoldSeconds=[Math]::Max(40,$HoldSeconds)}
if($JoinCodeDelaySeconds -gt 0 -and ($ResumeFrom -or $ProbeVersion -ne '11')){throw 'Join-code delay requires a new current-build diagnostic session'}
if($Chat -and ($Headless -or $ServerOnly)){throw 'Chat validation requires two graphics-enabled clients'}
if(!$ServerExe){$ServerExe=Join-Path $taskRoot 'Builds/WindowsServer/SyntheticFrontServer.exe'}
if(!$ClientExe){$ClientExe=if($Headless){$ServerExe}else{Join-Path $taskRoot 'TestResults/Multiplayer/GraphicsProbeClient/SyntheticFrontGraphicsProbe.exe'}}
$required=if($ServerOnly){@($ServerExe)}else{@($ClientExe,$ServerExe)}
foreach($exe in $required){
    if(!(Test-Path -LiteralPath $exe)){throw "Missing executable: $exe. Build the current client and server first."}
    $marker=Join-Path (Split-Path -Parent $exe) 'session-probe.version'
    if(!(Test-Path -LiteralPath $marker) -or (Get-Content -LiteralPath $marker -Raw).Trim() -ne $ProbeVersion){throw "Build without the requested opt-in session probe: $exe. Rebuild the dedicated server and GraphicsProbeClient fixture. Normal client builds intentionally exclude diagnostics."}
}
$runId=[guid]::NewGuid().ToString('N').Substring(0,12)
$runRoot=Join-Path $taskRoot "TestResults/Multiplayer/Live/$runId"
[IO.Directory]::CreateDirectory($runRoot)|Out-Null
$profiles=@(1..$ClientCount|ForEach-Object {"probe_${runId}_$_"});$expectedActors=@();$previousPoses=@{}
if($ResumeFrom){
    $resumeRoot=(Resolve-Path -LiteralPath $ResumeFrom).Path
    $allowed=[IO.Path]::GetFullPath((Join-Path $taskRoot 'TestResults/Multiplayer/Live'))+[IO.Path]::DirectorySeparatorChar
    if(!$resumeRoot.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Resume is limited to this workspace diagnostic Live directory'}
    $config=Get-Content -LiteralPath (Join-Path $resumeRoot 'server.json') -Raw|ConvertFrom-Json
    $expectedSave=[IO.Path]::GetFullPath((Join-Path $resumeRoot 'industry.xml'))
    if(![IO.Path]::GetFullPath($config.savePath).Equals($expectedSave,[StringComparison]::OrdinalIgnoreCase)){throw 'Resume config must reference its own diagnostic industry.xml'}
    [xml]$saved=Get-Content -LiteralPath $expectedSave
    foreach($actor in $saved.IndustrialWorld.actors.IndustrialActor){$previousPoses[[long]$actor.id]=$actor.pose.CloneNode($true)}
    foreach($index in 1..$ClientCount){
        $prior=Get-Content -LiteralPath (Join-Path $resumeRoot "client-$index.json") -Raw|ConvertFrom-Json
        if($prior.status -ne 'PASS' -or $prior.profile -notmatch '^probe_[a-f0-9]{12}_[0-9]{1,3}$'){throw 'Resume requires successful diagnostic client profiles'}
        $profiles[$index-1]=$prior.profile;$expectedActors+=[long]$prior.actor
    }
}else{
    $config=Get-Content -LiteralPath (Join-Path $taskRoot 'Server/server.example.json') -Raw|ConvertFrom-Json
    $config.worldId="probe-$runId";$config.instanceId="probe-$runId";$config.savePath=Join-Path $runRoot 'industry.xml'
}
$config.displayName="Session validation $runId";$config.maxPlayers=$ClientCount
$configPath=Join-Path $runRoot 'server.json';$config|ConvertTo-Json|Set-Content -LiteralPath $configPath -Encoding utf8
$serverReport=Join-Path $runRoot 'server-report.json';$processes=[Collections.Generic.List[Diagnostics.Process]]::new();$clientReports=@();$failure=$null
$memoryRecords=@{}
function Sample-ProcessMemory {
    for($processIndex=0;$processIndex -lt $processes.Count;$processIndex++){
        $sampleProcess=$processes[$processIndex]
        try{
            if($sampleProcess.HasExited){continue};$sampleProcess.Refresh()
            if(!$memoryRecords.ContainsKey($sampleProcess.Id)){$memoryRecords[$sampleProcess.Id]=@{role=$(if($processIndex -eq 0){'server'}else{"client-$processIndex"});samples=0;peakWorkingSetBytes=[long]0;peakObservedPrivateBytes=[long]0}}
            $memoryRow=$memoryRecords[$sampleProcess.Id];$memoryRow.samples++
            $memoryRow.peakWorkingSetBytes=[Math]::Max($memoryRow.peakWorkingSetBytes,$sampleProcess.PeakWorkingSet64)
            $memoryRow.peakObservedPrivateBytes=[Math]::Max($memoryRow.peakObservedPrivateBytes,$sampleProcess.PrivateMemorySize64)
        }catch{if(!$sampleProcess.HasExited){Write-Warning "Memory sample unavailable for process $($sampleProcess.Id)"}}
    }
}
function Quote-Argument([string]$value){if($value.Contains('"')){throw 'Double quotes are not allowed in probe argument paths'};'"'+$value+'"'}
try {
    $serverArgs=@('-batchmode','-nographics','-server-config',(Quote-Argument $configPath),'-survival-probe-report',(Quote-Argument $serverReport),'-logFile',(Quote-Argument (Join-Path $runRoot 'server.log')))
    if($OneWayDelayMs -or $JitterMs -or $LossPercent){$serverArgs+=@('-survival-probe-delay',"$OneWayDelayMs",'-survival-probe-jitter',"$JitterMs",'-survival-probe-loss',"$LossPercent")}
    if($ListingRecovery){$serverArgs+=@('-survival-probe-listing-recovery',"$ClientCount")}
    if($JoinCodeDelaySeconds -gt 0){$serverArgs+=@('-survival-probe-join-code-delay',"$JoinCodeDelaySeconds")}
    if($extraWait -gt 0){$serverArgs+=@('-survival-probe-extra-wait',"$extraWait")}
    $server=Start-Process -FilePath $ServerExe -ArgumentList $serverArgs -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru;$processes.Add($server)
    $timer=[Diagnostics.Stopwatch]::StartNew();$ready=$null
    while($timer.Elapsed.TotalSeconds -lt 120 -and !$server.HasExited){
        Sample-ProcessMemory
        if(Test-Path -LiteralPath $serverReport){$ready=Get-Content -LiteralPath $serverReport -Raw|ConvertFrom-Json;if($ready.status -eq 'READY'){break}}
        Start-Sleep -Milliseconds 500
    }
    if(!$ready -or $ready.status -ne 'READY'){throw 'Dedicated server did not advertise readiness; inspect server.log and server-report.json'}
    Write-Output 'Authenticated server ready.'
    if(!$ServerOnly){
    Write-Output $(if($Headless){"Launching $ClientCount headless production transport/prediction clients."}else{"Launching $ClientCount graphics-enabled standalone clients."})
    foreach($index in 1..$ClientCount){
        $report=Join-Path $runRoot "client-$index.json";$clientReports+=$report
        $clientArgs=@('-batchmode','-screen-fullscreen','0','-screen-width','960','-screen-height','540','-survival-probe-report',(Quote-Argument $report),'-survival-probe-profile',$profiles[$index-1],'-survival-probe-code',$ready.relayCode,'-logFile',(Quote-Argument (Join-Path $runRoot "client-$index.log")))
        if($OneWayDelayMs -or $JitterMs -or $LossPercent){$clientArgs+=@('-survival-probe-delay',"$OneWayDelayMs",'-survival-probe-jitter',"$JitterMs",'-survival-probe-loss',"$LossPercent")}
        if($Headless){$clientArgs+=@('-nographics','-survival-probe-headless')}
        if($Chat){$clientArgs+='-survival-probe-chat'}
        if($SeparatedRegions){$clientArgs+='-survival-probe-separated'}
        if($HoldSeconds -gt 0){$clientArgs+=@('-survival-probe-hold-seconds',"$HoldSeconds")}
        if($CombatSpec){$clientArgs+=@('-survival-probe-combat',(Quote-Argument ([IO.Path]::GetFullPath($CombatSpec))))}
        if($InteriorSpec){$clientArgs+=@('-survival-probe-interior',(Quote-Argument ([IO.Path]::GetFullPath($InteriorSpec))))}
        if($FactorySpec){$clientArgs+=@('-survival-probe-factory',(Quote-Argument ([IO.Path]::GetFullPath($FactorySpec))),'-survival-probe-factory-phase',$FactoryPhase,'-survival-probe-factory-state',(Quote-Argument $runRoot))}
        $client=Start-Process -FilePath $ClientExe -ArgumentList $clientArgs -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru;$processes.Add($client)
    }
    $timer.Restart()
    while($timer.Elapsed.TotalSeconds -lt (185+$HoldSeconds+$extraWait)){
        Sample-ProcessMemory
        foreach($reportPath in $clientReports){if(Test-Path -LiteralPath $reportPath){$early=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json;if($early.status -eq 'FAIL'){throw "Client failed: $($early.error)"}}}
        $running=@($processes|Select-Object -Skip 1|Where-Object {!$_.HasExited});if($running.Count -eq 0){break};if($server.HasExited){throw 'Server exited while clients were running'};Start-Sleep -Milliseconds 500
    }
    foreach($report in $clientReports){if(!(Test-Path -LiteralPath $report)){throw "Client did not write $report"};$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;if($result.status -ne 'PASS'){throw "Client failed: $($result.error)"};if($HoldSeconds -gt 0 -and $result.holdSeconds -ne $HoldSeconds){throw 'Client did not execute the required occupied hold interval'}}
    if(Select-String -LiteralPath (Join-Path $runRoot 'server.log') -SimpleMatch 'send queue full' -Quiet){throw 'Server transport send queue overflowed during this run'}
    if($OneWayDelayMs -or $JitterMs -or $LossPercent){foreach($reportPath in $clientReports){$r=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json;if(!$r.transportSimulationActive -or $r.simulatedDelayMs -ne $OneWayDelayMs -or $r.simulatedJitterMs -ne $JitterMs -or $r.simulatedLossPercent -ne $LossPercent){throw 'Requested UTP network conditions were not active'}}}
    if($Chat){foreach($report in $clientReports){$result=Get-Content -LiteralPath $report -Raw|ConvertFrom-Json;if(!$result.chatRequired -or !$result.peerTextBefore -or !$result.ownTextAcknowledgedBefore -or !$result.peerTextAfter -or !$result.ownTextAcknowledgedAfter -or !$result.voiceBefore -or !$result.voiceAfter -or !$result.mutedBefore -or !$result.mutedAfter){throw 'Client did not complete the required muted voice-channel and bidirectional text checks'}}}
    if($InteriorSpec){
        $spec=Get-Content -LiteralPath $InteriorSpec -Raw|ConvertFrom-Json
        foreach($reportPath in $clientReports){$r=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json;if(!$r.interiorRequired -or !$r.interiorBoarded -or !$r.interiorWalked -or !$r.interiorCanonical -or !$r.interiorParentMoved){throw 'Interior walking/moving-parent/canonical assertions failed'};if([long]$r.actor -eq [long]$spec.owner){if(!$r.interiorHatchBlocked -or !$r.interiorPilot -or !$r.interiorReconnect){throw 'Owner hatch/pilot/reconnect assertions failed'}}elseif(!$r.interiorPilotDenied){throw 'Passenger pilot denial was not checked'}}
    }
    if($FactorySpec){
        $spec=Get-Content -LiteralPath $FactorySpec -Raw|ConvertFrom-Json
        foreach($reportPath in $clientReports){
            $r=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json
            if(!$r.factoryRequired -or $r.factoryPhase -ne $FactoryPhase -or !$r.factoryReplication -or $r.matchingPredictionSamples -lt 10){throw 'Missing real transport factory assertions'}
            if(!$Headless -and (!$r.factoryRendered -or ($FactoryPhase -eq 'recover' -and !$r.factoryRenderCleared))){throw 'Graphical factory geometry creation/removal did not pass'}
            if($spec.buildings){
                if([long]$r.actor -eq [long]$spec.owner){if(!$r.buildingConserved -or $r.buildingAccepted -ne $(if($FactoryPhase -eq 'create'){4}else{3}) -or ($FactoryPhase -eq 'create' -and (!$r.buildingCreated -or !$r.buildingReconnect)) -or ($FactoryPhase -eq 'recover' -and (!$r.buildingRestored -or !$r.buildingRecovered))){throw 'Building ownership/payment/replay/reconnect/restart/recovery assertions failed'}}
                else{if(!$r.buildingPrivacy -or ($FactoryPhase -eq 'create' -and (!$r.buildingCreated -or $r.buildingRejections -ne 2)) -or ($FactoryPhase -eq 'recover' -and (!$r.buildingRestored -or !$r.buildingRecovered))){throw 'Foreign building privacy/ownership assertions failed'}}
                if(!$Headless -and (($FactoryPhase -eq 'create' -and !$r.buildingRendered) -or ($FactoryPhase -eq 'recover' -and (!$r.buildingRendered -or !$r.buildingRenderCleared)))){throw 'Graphical building creation/removal did not pass'}
            }
            if([long]$r.actor -eq [long]$spec.owner){
                if($spec.shipyard -and ($r.shipyardAccepted -ne 2 -or !$r.shipyardConserved -or ($FactoryPhase -eq 'create' -and !$r.shipyardCreated) -or ($FactoryPhase -eq 'recover' -and (!$r.shipyardRestored -or !$r.shipyardRecovered)))){throw 'Real owner shipyard payment/restart/recovery assertions failed'}
                if($spec.largeSnapshots -and ($r.snapshotFragments -lt 2 -or $r.largeSnapshots -lt 1 -or $r.partialSnapshots -lt 2 -or $r.largestPrivateRespawnCount -ne 128 -or $r.maximumSnapshotBytes -gt 16384)){throw 'Bounded large private-state delivery assertions did not pass'}
                if($r.factoryAccepted -ne 3 -or !$r.factoryConserved){throw 'Owner factory commands/conservation did not pass'}
                if($FactoryPhase -eq 'create' -and (!$r.factoryReplaySafe -or !$r.factoryReconnect)){throw 'Factory creation replay/reconnect checks did not pass'}
                if($FactoryPhase -eq 'recover' -and (!$r.factoryRestartRestored -or !$r.factoryRemoved)){throw 'Factory restart/recovery checks did not pass'}
            }elseif([long]$r.actor -eq [long]$spec.observer){
                if($spec.shipyard -and (!$r.shipyardPrivacy -or ($FactoryPhase -eq 'create' -and ($r.shipyardRejections -ne 2 -or !$r.shipyardCreated)) -or ($FactoryPhase -eq 'recover' -and (!$r.shipyardRestored -or !$r.shipyardRecovered)))){throw 'Real observer shipyard privacy/ownership/recovery assertions failed'}
                if(!$r.factoryPrivacy){throw 'Foreign endpoint privacy was not checked'}
                if($FactoryPhase -eq 'create' -and ($r.factoryOwnershipRejections -ne 2 -or !$r.factoryReconnect)){throw 'Foreign ownership/reconnect assertions did not pass'}
                if($FactoryPhase -eq 'recover' -and (!$r.factoryRestartRestored -or !$r.factoryRemoved)){throw 'Observer did not see restored links and their removal'}
            }else{throw 'Unexpected factory identity'}
        }
    }
    if($SeparatedRegions){$bodies=@();foreach($reportPath in $clientReports){$r=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json;if(!$r.separatedRegions -or $r.remotePlayerSeen){throw 'Separated-region interest isolation was not verified'};$bodies+=[int]$r.body};if(@($bodies|Select-Object -Unique).Count -ne $ClientCount){throw 'Expected independently occupied planetary regions'}}
    if($ResumeFrom){for($i=0;$i -lt $clientReports.Count;$i++){$result=Get-Content -LiteralPath $clientReports[$i] -Raw|ConvertFrom-Json;if([long]$result.actor -ne $expectedActors[$i]){throw 'Restart changed a persistent character identity'}}}
    }
} catch {$failure=$_.Exception.Message}
finally {
    [IO.File]::WriteAllText($serverReport+'.stop','Stop this disposable validation session')
    $timer=[Diagnostics.Stopwatch]::StartNew();while($timer.Elapsed.TotalSeconds -lt 30 -and @($processes|Where-Object {!$_.HasExited}).Count -gt 0){Sample-ProcessMemory;Start-Sleep -Milliseconds 500}
    foreach($process in $processes){if(!$process.HasExited){Stop-Process -InputObject $process -Force;if(!$failure){$failure='A probe process required termination after its cleanup timeout'}}elseif($process.ExitCode -ne 0 -and !$failure){$failure="Probe process exited with code $($process.ExitCode)"}}
    if($ListingRecovery -and !$failure){$discovery=Get-Content -LiteralPath $serverReport -Raw|ConvertFrom-Json;if($discovery.status -ne 'PASS' -or !$discovery.listingRestored -or !$discovery.listingConnectionsPreserved -or !$discovery.listingMetadataMatches){$failure='Dedicated listing recovery assertions did not pass'}}
    if($JoinCodeDelaySeconds -gt 0 -and !$failure){$delayed=Get-Content -LiteralPath $serverReport -Raw|ConvertFrom-Json;if($delayed.status -ne 'PASS' -or !$delayed.joinCodeDelayChecked -or $delayed.joinCodeDelaySeconds -lt $JoinCodeDelaySeconds -or $delayed.joinCodeDelayTicks -le 0){$failure='Dedicated bound-transport/join-code-delay assertions did not pass'}}
    if($ResumeFrom -and !$ServerOnly -and !$failure){
        [xml]$after=Get-Content -LiteralPath $config.savePath
        foreach($reportPath in $clientReports){
            $result=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json;$before=$previousPoses[[long]$result.actor];$actor=$after.IndustrialWorld.actors.IndustrialActor|Where-Object {[long]$_.id -eq [long]$result.actor}
            if(!$before -or !$actor -or $before.body -ne $actor.pose.body -or $before.frame -ne $actor.pose.frame){$failure='Restart lost the canonical character frame';break}
            $dx=[double]$actor.pose.x-[double]$before.x;$dy=[double]$actor.pose.y-[double]$before.y;$dz=[double]$actor.pose.z-[double]$before.z
            if([Math]::Abs([Math]::Sqrt($dx*$dx+$dy*$dy+$dz*$dz)-$result.movementMetres) -gt 3){$failure='Restart did not preserve character position before the new walking segment';break}
        }
    }
    $status=if($failure){'FAIL'}else{'PASS'}
    $scope=if($ServerOnly){'Dedicated service startup/cleanup only. No clients or gameplay validation.'}elseif($Headless){'Two headless authenticated production transport/prediction clients only. No rendering, UI, Vivox, full survival or capacity acceptance.'}else{'Two authenticated standalone clients: movement, remote visibility, prediction samples and reconnect only. Not Vivox, full survival, capacity, frame-time or army acceptance.'}
    if(!$Headless -and !$ServerOnly -and (Test-Path -LiteralPath (Join-Path (Split-Path -Parent $ClientExe) 'graphics-probe-only'))){$scope='Two graphics-enabled survival fixtures with offline army kernels omitted. Not release, UI/Vivox, full survival, capacity, rendered frame-time or army acceptance.'}
    if($HoldSeconds -gt 0){$scope+=" Includes $HoldSeconds seconds of stationary occupied simulation per client before reconnect; not full survival load."}
    $scope=$scope.Replace('Two ',"$ClientCount ").Replace('two ',"$ClientCount ")
    if($FactorySpec){$scope+=" Includes real factory $FactoryPhase commands and observer privacy/ownership checks, using offline prepared resources and local diagnostic coordination files. GUI, progression and factory load remain unverified."}
    if($ListingRecovery){$scope+=' Includes deletion/recreation of this disposable server Lobby, unchanged Relay code, metadata and continuous connection checks. Relay transport outages/reallocation are not covered.'}
    if($JoinCodeDelaySeconds -gt 0){$scope+=" Includes $JoinCodeDelaySeconds seconds before join-code retrieval while server transport/clock run; this delays a diagnostic phase and does not induce a provider outage."}
    if($Chat){$scope=$scope.Replace('UI/Vivox','interactive UI').Replace('Not Vivox,','Not audible voice,');$scope+=' Includes real Vivox text exchange and positional channel membership/mute checks before and after reconnect; microphone capture, playback and interactive keyboard behavior are unverified.'}
    @{oneWayDelayMs=$OneWayDelayMs;jitterMs=$JitterMs;lossPercent=$LossPercent;status=$status;clientCount=$(if($ServerOnly){0}else{$ClientCount});holdSeconds=$HoldSeconds;factoryPhase=$(if($FactorySpec){$FactoryPhase}else{''});error=$failure;scope=$scope;processMemory=@($memoryRecords.Values);memoryScope='Windows process working-set high-water mark and private-byte peaks observed at roughly 500 ms intervals; short functional run, not steady-state capacity or a baseline comparison';resumedFrom=$ResumeFrom;restartContinuityChecked=[bool]($ResumeFrom -and !$ServerOnly -and !$failure);reports=$clientReports;serverReport=$serverReport}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $runRoot 'result.json') -Encoding utf8
    Write-Output "Session probe $status; reports: $runRoot"
}
if($failure){throw $failure}
