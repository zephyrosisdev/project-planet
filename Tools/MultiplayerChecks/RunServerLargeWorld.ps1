param([string]$Config=(Join-Path $PSScriptRoot 'server.json'),[ValidateRange(1,1000)][double]$Multiplier=100,[string]$ServerExe=(Join-Path $PSScriptRoot 'SyntheticFrontServer.exe'),[switch]$PrepareOnly)
$ErrorActionPreference='Stop'
if([double]::IsNaN($Multiplier)-or[double]::IsInfinity($Multiplier)){throw 'Multiplier must be finite'}
$source=(Resolve-Path -LiteralPath $Config).Path
$settings=Get-Content -Raw -LiteralPath $source|ConvertFrom-Json
$tag=$Multiplier.ToString('R',[Globalization.CultureInfo]::InvariantCulture).Replace('.','_')
$suffix='-large-'+$tag
foreach($key in @('worldId','instanceId')){if($settings.$key -notmatch '^[A-Za-z0-9_-]+$' -or ($settings.$key+$suffix).Length -gt 64){throw "Choose a shorter letters/numbers/hyphens/underscores $key in the source config"}}
$world=$settings.worldId+$suffix;$instance=$settings.instanceId+$suffix
$output=Join-Path (Split-Path -Parent $source) (([IO.Path]::GetFileNameWithoutExtension($source))+'.large-'+$tag+'.json')
if(Test-Path -LiteralPath $output){
    $existing=Get-Content -Raw -LiteralPath $output|ConvertFrom-Json
    if($existing.worldId -ne $world -or $existing.instanceId -ne $instance -or $existing.savePath -ne '' -or [double]$existing.systemSizeMultiplier -ne $Multiplier){throw 'Existing large-world configuration differs; it was left untouched. Use a separate source configuration for a different world.'}
}else{
    $settings.worldId=$world;$settings.instanceId=$instance;$settings.savePath='';$settings.displayName=($settings.displayName+' · Large '+$Multiplier+'x');$settings|Add-Member -NotePropertyName systemSizeMultiplier -NotePropertyValue $Multiplier -Force
    # Resolve the preset relative to the original config, which shares this directory.
    $settings|ConvertTo-Json -Depth 8|Set-Content -LiteralPath $output -Encoding utf8
}
if($PrepareOnly){Write-Output $output;exit 0}
if(!(Test-Path -LiteralPath $ServerExe)){throw 'Dedicated server executable missing'}
Write-Host "Starting separate world $world at $Multiplier`x. The original configuration/save is unchanged. Existing large-world saves retain their recorded definitions."
& $ServerExe -batchmode -nographics -server-config $output -logFile -
exit $LASTEXITCODE
