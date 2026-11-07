param([string]$Build='TestResults/Planetary/Atmosphere/Build/Atmosphere.exe',[string]$Output='TestResults/Planetary/Atmosphere/Measured')
$ErrorActionPreference='Stop'
$fixtureRoot=(Get-Location).Path
$fixtureExe=(Resolve-Path -LiteralPath $Build).Path
$fixtureOutput=[System.IO.Path]::GetFullPath((Join-Path $fixtureRoot $Output))
New-Item -ItemType Directory -Force -Path $fixtureOutput | Out-Null
$fixtureReports=@{}
foreach($fixtureRun in 1..3){foreach($fixtureMode in @('legacy','current')){
    $fixtureDirectory=Join-Path $fixtureOutput "$fixtureMode-$fixtureRun"
    New-Item -ItemType Directory -Force -Path $fixtureDirectory | Out-Null
    $fixtureArgs=@('-batchmode','-force-d3d11','-screen-fullscreen','0','-screen-width','2560','-screen-height','1440','-atmosphere-validation','-planet-output',('"'+$fixtureDirectory+'"'),'-logFile',('"'+(Join-Path $fixtureDirectory 'player.log')+'"'))
    if($fixtureMode -eq 'legacy'){$fixtureArgs+='-atmosphere-legacy'}
    $fixtureProcess=Start-Process -FilePath $fixtureExe -ArgumentList $fixtureArgs -WindowStyle Hidden -WorkingDirectory $fixtureRoot -PassThru
    if(-not $fixtureProcess.WaitForExit(60000)){Stop-Process -Id $fixtureProcess.Id;throw "Atmosphere $fixtureMode-$fixtureRun exceeded its 60 second fixture limit"}
    $fixtureReport=Get-Content -LiteralPath (Join-Path $fixtureDirectory 'validation.json') -Raw | ConvertFrom-Json
    if($fixtureProcess.ExitCode -ne 0 -or $fixtureReport.status -ne 'PASS'){throw "Atmosphere $fixtureMode-$fixtureRun failed: $($fixtureReport.error)"}
    if(-not $fixtureReports.ContainsKey($fixtureMode)){$fixtureReports[$fixtureMode]=@()}
    $fixtureReports[$fixtureMode]+=$fixtureReport
    Write-Output "$fixtureMode-$fixtureRun PASS: median $($fixtureReport.medianMs) ms / p95 $($fixtureReport.p95Ms) ms"
}}
$fixtureSummary=[ordered]@{scope='Isolated standalone atmosphere/cloud graphics, not full gameplay or army throughput. Exact frozen shader baseline, identical camera/density/2560x1440 target, 40 warm-up and 120 sampled frames, three alternating runs. Timings include CPU submission and GPU fence wait.'}
foreach($fixtureMode in @('legacy','current')){
    $fixtureStats=[ordered]@{}
    foreach($fixtureMetric in @('medianMs','p95Ms','frameMedianMs','frameP95Ms','renderFramesPerSecond','gpuMs','privateBytes','workingSetBytes','managedBytes','unityAllocatedBytes','unityReservedBytes','graphicsDriverBytes','targetBytes')){
        $fixtureValues=@($fixtureReports[$fixtureMode] | ForEach-Object {$_.$fixtureMetric} | Sort-Object)
        $fixtureStats[$fixtureMetric]=$fixtureValues[1]
    }
    $fixtureStats['checks']=$fixtureReports[$fixtureMode][0].checks.Count
    $fixtureSummary[$fixtureMode]=$fixtureStats
}
$fixtureSummary['fencedP95DeltaMs']=$fixtureSummary.current.p95Ms-$fixtureSummary.legacy.p95Ms
$fixtureSummary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $fixtureOutput 'comparison.json')
