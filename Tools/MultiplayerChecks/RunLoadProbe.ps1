param(
    [ValidateSet(2,8,32,100)][int]$ClientCount=2,
    [ValidateSet('crowded','dispersed')][string]$Layout='crowded',
    [ValidateRange(30,300)][int]$HoldSeconds=90,
    [string]$ServerExe,
    [string]$LoadExe,
    [ValidateRange(1,10)][int]$Workers=1
)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if(!$ServerExe){$ServerExe=Join-Path $taskRoot 'Builds/WindowsServer/SyntheticFrontServer.exe'}
if(!$LoadExe){$LoadExe=$ServerExe}
if($ClientCount % $Workers -ne 0 -or $ClientCount/$Workers -lt 2){throw 'Workers must divide the population into groups of at least two clients'}
$clientsPerWorker=[int]($ClientCount/$Workers)
foreach($exe in @($ServerExe,$LoadExe)){
    if(!(Test-Path -LiteralPath $exe)){throw "Build executable missing: $exe"}
    $marker=Join-Path (Split-Path -Parent $exe) 'load-probe.version'
    if(!(Test-Path -LiteralPath $marker) -or ((Get-Content -LiteralPath $marker -Raw).Trim() -notin @('2','3') -or ($Workers -gt 1 -and (Get-Content -LiteralPath $marker -Raw).Trim() -ne '3'))){throw 'Rebuild the dedicated server with the opt-in multi-client load fixture'}
}
$runId=[guid]::NewGuid().ToString('N').Substring(0,12)
$runRoot=Join-Path $taskRoot "TestResults/Multiplayer/Live/$runId"
[IO.Directory]::CreateDirectory($runRoot)|Out-Null
$config=Get-Content -LiteralPath (Join-Path $taskRoot 'Server/server.example.json') -Raw|ConvertFrom-Json
$config.worldId="probe-load-$runId";$config.instanceId=$config.worldId;$config.displayName="Scripted load validation $runId";$config.maxPlayers=$ClientCount;$config.savePath=Join-Path $runRoot 'industry.xml'
$configPath=Join-Path $runRoot 'server.json';$config|ConvertTo-Json|Set-Content -LiteralPath $configPath -Encoding utf8
$serverReport=Join-Path $runRoot 'server-report.json';$loadReport=Join-Path $runRoot 'load-report.json'
$summary=@{status='STARTING';workers=$Workers;clientsPerWorker=$clientsPerWorker;requested=$ClientCount;layout=$Layout;holdSeconds=$HoldSeconds;scope='Real independently authenticated clients running production transport, walking prediction, AOI and two authoritative consumable commands each in controlled headless worker processes. Scripted gameplay load; not a full survival, rendered-client, Vivox, ship/factory, army/fog or full gameplay capacity acceptance.'}
$processes=[Collections.Generic.List[Diagnostics.Process]]::new();$memory=@{};$failure=$null
$ambientStarted=[DateTime]::UtcNow;$ambient=@(Get-Process -Name Unity -ErrorAction SilentlyContinue|ForEach-Object {@{id=$_.Id;cpuSecondsBefore=$_.CPU;workingSetBytesBefore=$_.WorkingSet64}})
function Assembly-Hash([string]$exe){$managed=Join-Path (Split-Path -Parent $exe) (([IO.Path]::GetFileNameWithoutExtension($exe))+'_Data/Managed/Assembly-CSharp.dll');(Get-FileHash -LiteralPath $managed -Algorithm SHA256).Hash}
$summary.serverAssemblySha256=Assembly-Hash $ServerExe;$summary.loadAssemblySha256=Assembly-Hash $LoadExe
$summary.backgroundProcessScope='Other Unity process CPU deltas and working sets over the entire invocation, including startup/cleanup; original editors are preserved. GPU/background work outside Unity is not sampled.'

function Quote-Argument([string]$value){if($value.Contains('"')){throw 'Invalid quote in probe path'};'"'+$value+'"'}
function Sample-Memory {
    for($index=0;$index -lt $processes.Count;$index++){
        $process=$processes[$index];if($process.HasExited){continue};$process.Refresh()
        if(!$memory.ContainsKey($index)){$memory[$index]=@{role=$(if($index -eq 0){'server'}else{'scripted-client-worker-'+($index-1)});peakWorkingSetBytes=[long]0;peakObservedPrivateBytes=[long]0;samples=0}}
        $row=$memory[$index];$row.samples++;$row.peakWorkingSetBytes=[Math]::Max($row.peakWorkingSetBytes,$process.PeakWorkingSet64);$row.peakObservedPrivateBytes=[Math]::Max($row.peakObservedPrivateBytes,$process.PrivateMemorySize64)
    }
}
try{
    $serverArgs=@('-batchmode','-nographics','-server-config',(Quote-Argument $configPath),'-survival-probe-report',(Quote-Argument $serverReport),'-survival-probe-extra-wait','600','-logFile',(Quote-Argument (Join-Path $runRoot 'server.log')))
    $server=Start-Process -FilePath $ServerExe -ArgumentList $serverArgs -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru;$processes.Add($server)
    $timer=[Diagnostics.Stopwatch]::StartNew();$ready=$null
    while(!$server.HasExited -and $timer.Elapsed.TotalSeconds -lt 120){Sample-Memory;if(Test-Path -LiteralPath $serverReport){$ready=Get-Content -LiteralPath $serverReport -Raw|ConvertFrom-Json;if($ready.status -eq 'READY'){break}};Start-Sleep -Milliseconds 500}
    if(!$ready -or $ready.status -ne 'READY'){throw 'Dedicated load server did not become ready'}
    Write-Output "Authenticated load server ready; starting $ClientCount independent clients ($Layout)."
    $loadProcesses=[Collections.Generic.List[Diagnostics.Process]]::new();$loadReports=@();$loadLogs=@();$workerResults=@()
    foreach($worker in 0..($Workers-1)){
        $reportPath=if($Workers -eq 1){$loadReport}else{Join-Path $runRoot "load-report-worker-$worker.json"};$loadReports+=$reportPath
        $logPath=if($Workers -eq 1){Join-Path $runRoot 'load.log'}else{Join-Path $runRoot "load-worker-$worker.log"};$loadLogs+=$logPath
        $loadArgs=@('-batchmode','-nographics','-survival-load-report',(Quote-Argument $reportPath),'-survival-load-code',$ready.relayCode,'-survival-load-count',"$clientsPerWorker",'-survival-load-seconds',"$HoldSeconds",'-survival-load-layout',$Layout,'-logFile',(Quote-Argument $logPath))
        if($Workers -gt 1){$loadArgs+=@('-survival-load-workers',"$Workers",'-survival-load-worker',"$worker",'-survival-load-coordination',(Quote-Argument (Join-Path $runRoot 'workers')))}
        $load=Start-Process -FilePath $LoadExe -ArgumentList $loadArgs -WorkingDirectory $taskRoot -WindowStyle Hidden -PassThru;$loadProcesses.Add($load);$processes.Add($load)
    }
    $timer.Restart()
    while(@($loadProcesses|Where-Object {!$_.HasExited}).Count -gt 0 -and $timer.Elapsed.TotalSeconds -lt (660+$HoldSeconds)){
        Sample-Memory;if($server.HasExited){throw 'Server exited during scripted client load'}
        foreach($reportPath in $loadReports){if(Test-Path -LiteralPath $reportPath){$workerResult=Get-Content -LiteralPath $reportPath -Raw|ConvertFrom-Json;if($workerResult.status -eq 'FAIL'){throw "Load worker failed: $($workerResult.error)"}}};Start-Sleep -Milliseconds 500
    }
    foreach($worker in 0..($Workers-1)){
        if(!$loadProcesses[$worker].HasExited -or !(Test-Path -LiteralPath $loadReports[$worker])){throw 'Scripted worker did not finish or write its report'}
        $workerResult=Get-Content -LiteralPath $loadReports[$worker] -Raw|ConvertFrom-Json
        if($loadProcesses[$worker].ExitCode -ne 0 -or $workerResult.status -ne 'PASS' -or $workerResult.ready -ne $clientsPerWorker -or $workerResult.bots.Count -ne $clientsPerWorker){throw "Scripted worker failed: $($workerResult.error)"};$workerResults+=,$workerResult
    }
    $result=if($Workers -eq 1){$workerResults[0]}else{[pscustomobject]@{bots=@($workerResults|ForEach-Object {$_.bots});ready=$ClientCount;measurementFirstServerTick=($workerResults.measurementFirstServerTick|Measure-Object -Maximum).Maximum;measurementLastServerTick=($workerResults.measurementLastServerTick|Measure-Object -Minimum).Minimum;measurementStartRadiusMetres=($workerResults.measurementStartRadiusMetres|Measure-Object -Maximum).Maximum;measurementEndRadiusMetres=($workerResults.measurementEndRadiusMetres|Measure-Object -Maximum).Maximum;rallySeconds=($workerResults.rallySeconds|Measure-Object -Maximum).Maximum;clientFrameScope='Separate combined-worker engine loops; per-worker percentiles are reported individually and are not individual rendered frame times'}}
    if(@($result.bots.actor|Select-Object -Unique).Count -ne $ClientCount){throw 'Workers did not resolve to distinct authenticated characters'}
    $summary.clientReports=$loadReports;$summary.workerClientFrameTimes=@($workerResults|Select-Object workerIndex,requested,combinedClientFrameSamples,combinedClientFrameMedianMs,combinedClientFrameP95Ms,clientFrameScope)
    foreach($bot in $result.bots){if($bot.accepted -ne 2 -or $bot.rejected -ne 0 -or $bot.snapshotFailures -ne 0 -or $bot.matchedSamples -lt 30){throw 'A client failed command/prediction/snapshot assertions'}}
    foreach($log in @((Join-Path $runRoot 'server.log'))+$loadLogs){if(Select-String -LiteralPath $log -Pattern 'send queue full|NullReferenceException|IndexOutOfRangeException|Unhandled Exception' -Quiet){throw "Runtime failure in $log"}}
    $summary.clientReport=if($Workers -eq 1){$loadReport}else{$null}
    if($result.measurementStartRadiusMetres -gt 30){throw 'Characters did not rally into a genuinely dense starting hub'}
    $summary.measurementStartRadiusMetres=$result.measurementStartRadiusMetres;$summary.measurementEndRadiusMetres=$result.measurementEndRadiusMetres;$summary.rallySeconds=$result.rallySeconds;$summary.initialConnectionAttempts=@($result.bots|ForEach-Object {$_.connectionAttempts})
    $summary.combinedClientFrameMedianMs=$result.combinedClientFrameMedianMs;$summary.combinedClientFrameP95Ms=$result.combinedClientFrameP95Ms;$summary.clientFrameScope=$result.clientFrameScope
}catch{$failure=$_.Exception.Message}
finally{
    [IO.File]::WriteAllText($serverReport+'.stop','Stop this disposable load server')
    $timer=[Diagnostics.Stopwatch]::StartNew();while($timer.Elapsed.TotalSeconds -lt 40 -and @($processes|Where-Object {!$_.HasExited}).Count -gt 0){Sample-Memory;Start-Sleep -Milliseconds 500}
    foreach($process in $processes){if(!$process.HasExited){Stop-Process -InputObject $process -Force;if(!$failure){$failure='Load cleanup timed out'}}elseif($process.ExitCode -ne 0 -and !$failure){$failure='A load probe process failed during cleanup'}}
    if(!$failure){
        try{
            [xml]$saved=Get-Content -LiteralPath $config.savePath;$actors=@($saved.IndustrialWorld.actors.IndustrialActor);if($actors.Count -ne $ClientCount){throw 'Persistent character count differs from authenticated clients'}
            foreach($actor in $actors){foreach($item in 'water','ration'){[long]$count=0;foreach($stack in $actor.inventory.items.ItemStack){if($stack.item -eq $item){$count+=[long]$stack.count}};if($count -ne 2){throw 'Persistent consumable conservation failed'}}}
            $summary.persistedCharacters=$actors.Count;$summary.persistedConservation=$true
            $windows=@();foreach($file in Get-ChildItem -LiteralPath $runRoot -Filter 'industry.xml.network-metrics.window-*.json'){$metric=Get-Content -LiteralPath $file.FullName -Raw|ConvertFrom-Json;if($metric.minimumPlayers -eq $ClientCount -and $metric.maximumPlayers -eq $ClientCount -and $metric.firstServerTick -ge $result.measurementFirstServerTick -and $metric.lastServerTick -le $result.measurementLastServerTick){$windows+=@{file=$file.FullName;window=$metric.window;firstServerTick=$metric.firstServerTick;lastServerTick=$metric.lastServerTick;samples=$metric.samples;elapsedSeconds=$metric.elapsedSeconds;simulationTicksPerSecond=$metric.simulationTicksPerSecond;tickMedianMs=$metric.tickMedianMs;tickP95Ms=$metric.tickP95Ms;sentBytesPerSecond=$metric.sentBytesPerSecond;receivedBytesPerSecond=$metric.receivedBytesPerSecond;snapshot=($metric.phases|Where-Object {$_.name -eq 'Snapshot'});managedBytes=$metric.managedBytes;unityAllocatedBytes=$metric.unityAllocatedBytes}}}
            $summary.fullyOccupiedWindows=$windows
            $summary.tickBudgetMs=1000.0/[double]$config.tickRate;$summary.serverTickBudgetMet=$null;if($windows.Count){$summary.serverTickBudgetMet=@($windows|Where-Object {$_.tickP95Ms -gt $summary.tickBudgetMs -or $_.simulationTicksPerSecond -lt ([double]$config.tickRate*.99)}).Count -eq 0}
            $summary.renderedClientFrameGate='UNMEASURED: combined headless-client frame times are not rendered gameplay acceptance'
            if($HoldSeconds -ge 65 -and $windows.Count -eq 0){throw 'No fully occupied metric window was captured'}
        }catch{$failure=$_.Exception.Message}
    }
    foreach($row in $ambient){$current=Get-Process -Id $row.id -ErrorAction SilentlyContinue;if($current){$row.cpuSecondsDuring=[Math]::Max(0,$current.CPU-$row.cpuSecondsBefore);$row.workingSetBytesAfter=$current.WorkingSet64;$row.cpuFractionOfSingleCore=$row.cpuSecondsDuring/[Math]::Max(.001,([DateTime]::UtcNow-$ambientStarted).TotalSeconds)}};$summary.backgroundUnityProcesses=$ambient
    $summary.status=if($failure){'FAIL'}else{'PASS'};$summary.error=$failure;$summary.processMemory=@($memory.Values)
    $summary|ConvertTo-Json -Depth 12|Set-Content -LiteralPath (Join-Path $runRoot 'load-result.json') -Encoding utf8
    Write-Output "Scripted load $($summary.status); reports: $runRoot"
}
if($failure){throw $failure}
