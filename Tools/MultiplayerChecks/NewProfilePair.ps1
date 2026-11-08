param([Parameter(Mandatory=$true)][string]$From)
$ErrorActionPreference='Stop'
$taskRoot=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$liveRoot=[IO.Path]::GetFullPath((Join-Path $taskRoot 'TestResults/Multiplayer/Live'))
$source=(Resolve-Path -LiteralPath $From).Path
if(!$source.StartsWith($liveRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Source must be a completed diagnostic Live run in this workspace'}
$result=Get-Content -LiteralPath (Join-Path $source 'result.json') -Raw|ConvertFrom-Json
if($result.status -ne 'PASS' -or $result.reports.Count -lt 2){throw 'Source must have passing client reports'}
$config=Get-Content -LiteralPath (Join-Path $source 'server.json') -Raw|ConvertFrom-Json
$save=[IO.Path]::GetFullPath((Join-Path $source 'industry.xml'))
if(![IO.Path]::GetFullPath($config.savePath).Equals($save,[StringComparison]::OrdinalIgnoreCase)){throw 'Source must own its diagnostic save; use the original completed run, not a resume report'}
$clientReports=@(1..$result.reports.Count|ForEach-Object {Get-Content -LiteralPath (Join-Path $source "client-$_.json") -Raw|ConvertFrom-Json})
foreach($client in $clientReports){if($client.status -ne 'PASS' -or $client.profile -notmatch '^probe_[a-f0-9]{12}_[0-9]{1,3}$'){throw 'Only disposable diagnostic authentication profiles can be cloned'}}
$journal=[IO.File]::Open($save+'.journal',[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::None)
try{
    if($journal.Length -ne 0){throw 'Source must have a compacted checkpoint and empty journal'}
    $checkpoint=[IO.File]::Open($save,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try{
        $pair=@{source=$source;players=$clientReports.Count;scope='Matching diagnostic world checkpoints and player profiles; functional headless fixture, not army or full-survival acceptance'}
        foreach($label in @('before','after')){
            $target=[IO.Path]::GetFullPath((Join-Path $liveRoot ([guid]::NewGuid().ToString('N').Substring(0,12))))
            if(!$target.StartsWith($liveRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Clone target escaped the diagnostic workspace'}
            [IO.Directory]::CreateDirectory($target)|Out-Null
            $copy=[IO.File]::Create((Join-Path $target 'industry.xml'))
            try{$checkpoint.Position=0;$checkpoint.CopyTo($copy);$copy.Flush($true)}finally{$copy.Dispose()}
            [IO.File]::WriteAllText((Join-Path $target 'industry.xml.journal'),'')
            $config.savePath=Join-Path $target 'industry.xml';$config|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $target 'server.json') -Encoding utf8
            for($index=0;$index -lt $clientReports.Count;$index++){$clientReports[$index]|ConvertTo-Json -Depth 16|Set-Content -LiteralPath (Join-Path $target "client-$($index+1).json") -Encoding utf8}
            $pair[$label]=$target
            $pair[$label+'CheckpointSha256']=(Get-FileHash -LiteralPath (Join-Path $target 'industry.xml') -Algorithm SHA256).Hash
        }
        if($pair.beforeCheckpointSha256 -ne $pair.afterCheckpointSha256){throw 'Cloned starting checkpoints differ'}
        $pair|ConvertTo-Json
    }finally{$checkpoint.Dispose()}
}finally{$journal.Dispose()}
