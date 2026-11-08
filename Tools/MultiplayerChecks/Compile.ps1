param(
    [string]$UnityEditor = $env:UNITY_EDITOR,
    [ValidateSet('Editor','Client','Server','GraphicsProbe','All')][string]$Target='Editor',
    [string]$Overlay
)
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (-not $UnityEditor) {
    $taskVersion = ((Get-Content -LiteralPath (Join-Path $taskRoot 'ProjectSettings/ProjectVersion.txt') | Select-String '^m_EditorVersion:').Line -split ': ')[1]
    $UnityEditor = Join-Path $env:ProgramFiles "Unity/Hub/Editor/$taskVersion/Editor/Unity.exe"
}
$taskEditorDirectory = Split-Path -Parent $UnityEditor
$taskDotnet = Join-Path $taskEditorDirectory 'Data/DotNetSdk/dotnet.exe'
$taskCompiler = Get-ChildItem -LiteralPath (Join-Path $taskEditorDirectory 'Data/DotNetSdk/sdk') -Directory | ForEach-Object { Join-Path $_.FullName 'Roslyn/bincore/csc.dll' } | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$taskResponse = Get-ChildItem -Path (Join-Path $taskRoot 'Library/Bee/artifacts/*.dag/Assembly-CSharp.rsp') | Sort-Object LastWriteTime -Descending | Where-Object {Select-String -LiteralPath $_.FullName -Pattern '^[-/]define:UNITY_EDITOR$' -Quiet} | Select-Object -First 1
if (-not $taskResponse -or -not $taskCompiler) { throw 'Open this project in the pinned Unity editor and resolve packages before this compile-only check.' }
$taskOutput = Join-Path $taskRoot 'TestResults/Multiplayer'
[IO.Directory]::CreateDirectory($taskOutput) | Out-Null
$taskBaseArguments = [Collections.Generic.List[string]]::new()
foreach ($line in Get-Content -LiteralPath $taskResponse.FullName) {
    if ($line -match '^(-out:|-refout:|-analyzer:|/additionalfile:|[-/]shared)' -or $line.TrimStart('"') -match '^Assets[/\\]') { continue }
    $taskBaseArguments.Add($line)
}
$taskOverlayFiles=@{};$taskOverlayUsed=[Collections.Generic.HashSet[string]]::new()
if($Overlay){
    foreach($source in Get-ChildItem -LiteralPath ([IO.Path]::GetFullPath($Overlay)) -Filter '*.cs' -File){
        if($taskOverlayFiles.ContainsKey($source.Name)){throw 'Overlay source names must be unique'}
        $taskOverlayFiles[$source.Name]=$source.FullName
    }
    if($taskOverlayFiles.Count -eq 0){throw 'Overlay has no C# sources'}
}
foreach ($source in Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Assets') -Recurse -Filter '*.cs') {
    $relative = $source.FullName.Substring($taskRoot.Length + 1).Replace('\','/')
    if ($relative -match '/Editor/') {continue}
    if($taskOverlayFiles.ContainsKey($source.Name)){
        if(!$taskOverlayUsed.Add($source.Name)){throw "Ambiguous overlay source name: $($source.Name)"}
        $taskBaseArguments.Add('"'+$taskOverlayFiles[$source.Name]+'"')
    }else{$taskBaseArguments.Add('"' + $relative + '"')}
}
foreach($name in $taskOverlayFiles.Keys){if(!$taskOverlayUsed.Contains($name)){$taskBaseArguments.Add('"'+$taskOverlayFiles[$name]+'"')}}
Push-Location -LiteralPath $taskRoot
try {
    $taskTargets=if($Target -eq 'All'){@('Editor','Client','Server','GraphicsProbe')}else{@($Target)}
    foreach($taskTarget in $taskTargets){
        $taskArguments=[Collections.Generic.List[string]]::new()
        foreach($line in $taskBaseArguments){
            if($line -match '^[-/]define:(.+)$'){
                foreach($symbol in ($Matches[1] -split '[;,]')){
                    if($symbol -eq 'UNITY_SERVER' -or $symbol -eq 'SURVIVAL_GRAPHICS_PROBE' -or ($taskTarget -ne 'Editor' -and $symbol -match '^UNITY_EDITOR(?:_|$)')){continue}
                    $taskArguments.Add('-define:'+$symbol)
                }
            }else{$taskArguments.Add($line)}
        }
        if($taskTarget -eq 'Server'){$taskArguments.Add('-define:UNITY_SERVER')}
        if($taskTarget -eq 'GraphicsProbe'){$taskArguments.Add('-define:SURVIVAL_GRAPHICS_PROBE')}
        $taskSuffix=if($taskTarget -eq 'Editor'){''}else{'.'+$taskTarget};if($Overlay){$taskSuffix='.Overlay'+$taskSuffix}
        $taskArguments.Add('-out:"TestResults/Multiplayer/Assembly-CSharp'+$taskSuffix+'.dll"')
        $taskArguments.Add('-warn:0')
        $taskResponsePath=Join-Path $taskOutput ('runtime'+$taskSuffix+'.rsp')
        [IO.File]::WriteAllLines($taskResponsePath,$taskArguments)
        & $taskDotnet $taskCompiler "@$taskResponsePath"
        if($LASTEXITCODE -ne 0){throw "$taskTarget runtime C# compilation failed."}
        Write-Output "PASS: $taskTarget runtime conditional compilation (editor reference assemblies; staged overlay: $([bool]$Overlay))."
    }
    Write-Output 'Compile-only checks. Unity platform stripping, NGO processing, player builds and live multiplayer are separate checks.'
} finally { Pop-Location }
