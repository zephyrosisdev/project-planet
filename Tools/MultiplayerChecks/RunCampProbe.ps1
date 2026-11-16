param(
    [string]$ServerExe='.mpb/Builds/WindowsServer/SyntheticFrontServer.exe',
    [string]$ClientExe='.mpb/TestResults/Multiplayer/GraphicsProbeClient/SyntheticFrontGraphicsProbe.exe',
    [Parameter(Mandatory)][string]$Source,
    [ValidateSet('trade','landing','eva')][Parameter(Mandatory)][string]$Phase,
    [ValidateRange(0,500)][int]$OneWayDelayMs=0,
    [ValidateRange(0,250)][int]$JitterMs=0
)
$ErrorActionPreference='Stop'
$campRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$campLive=Join-Path $campRoot 'TestResults/Multiplayer/Live'
$campSource=(Resolve-Path -LiteralPath $Source).Path
if(!$campSource.StartsWith($campLive+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture source must be a disposable Live session'}
$campClone=Join-Path $campLive ([guid]::NewGuid().ToString('N').Substring(0,12))
[IO.Directory]::CreateDirectory($campClone)|Out-Null
foreach($campFile in @('industry.xml','client-1.json','client-2.json','server.json')){Copy-Item -LiteralPath (Join-Path $campSource $campFile) -Destination (Join-Path $campClone $campFile)}
$campConfig=Get-Content -LiteralPath (Join-Path $campClone 'server.json') -Raw|ConvertFrom-Json
$campConfig.savePath=Join-Path $campClone 'industry.xml'
$campConfig|ConvertTo-Json -Depth 8|Set-Content -LiteralPath (Join-Path $campClone 'server.json') -Encoding utf8
$campOwner=Get-Content -LiteralPath (Join-Path $campClone 'client-1.json') -Raw|ConvertFrom-Json
$campGuest=Get-Content -LiteralPath (Join-Path $campClone 'client-2.json') -Raw|ConvertFrom-Json
if($campOwner.status -ne 'PASS' -or $campGuest.status -ne 'PASS'){throw 'Fixture source requires successful character reports'}
$campSpec=Join-Path $campClone 'camp-spec.json'
@{version=1;seeded=$false;configPath=(Join-Path $campClone 'server.json');owner=[long]$campOwner.actor;passenger=[long]$campGuest.actor;phase=$Phase}|ConvertTo-Json|Set-Content -LiteralPath $campSpec -Encoding utf8
$campServer=(Resolve-Path -LiteralPath $ServerExe).Path
$campClient=(Resolve-Path -LiteralPath $ClientExe).Path
$campSeeder=Start-Process -FilePath $campServer -ArgumentList @('-batchmode','-nographics','-survival-camp-seed',('"'+$campSpec+'"'),'-logFile',('"'+(Join-Path $campClone 'seed.log')+'"')) -WorkingDirectory $campRoot -WindowStyle Hidden -PassThru
$campTimer=[Diagnostics.Stopwatch]::StartNew()
while(!$campSeeder.HasExited -and $campTimer.Elapsed.TotalSeconds -lt 60){Start-Sleep -Milliseconds 500}
if(!$campSeeder.HasExited){Stop-Process -InputObject $campSeeder -Force;throw 'Camp seeding timed out'}
$campSeeded=Get-Content -LiteralPath $campSpec -Raw|ConvertFrom-Json
if($campSeeder.ExitCode -ne 0 -or !$campSeeded.seeded){throw "Camp seeding failed: $($campSeeded.error)"}
& (Join-Path $PSScriptRoot 'RunSessionProbe.ps1') -ServerExe $campServer -ClientExe $campClient -ResumeFrom $campClone -CampSpec $campSpec -OneWayDelayMs $OneWayDelayMs -JitterMs $JitterMs
if($Phase -eq 'trade'){
    [xml]$campAfter=Get-Content -LiteralPath $campConfig.savePath
    $campSaved=$campAfter.IndustrialWorld.camps.SurvivalCampState|Where-Object {[long]$_.id -eq $campSeeded.camp}
    $campShop=$campSaved.shops.SurvivalCampShop|Where-Object {[int]$_.index -eq 0}
    $campStock=$campShop.stock.items.ItemStack|Where-Object {$_.item -eq 'ration'}
    $campPayment=$campShop.stock.items.ItemStack|Where-Object {$_.item -eq 'stone'}
    if([long]$campStock.count -ne 198 -or [long]$campPayment.count -ne 8){throw 'Two real barter commands did not conserve camp goods/payments in the checkpoint'}
    Write-Output 'PASS: two players each received one ration and paid four stone; durable camp stock and payment conserved.'
}
Write-Output "Camp $Phase fixture: $campClone"
