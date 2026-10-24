param([string]$UnityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.24f1\Editor')
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$resultRoot = Join-Path $PSScriptRoot '.results'
New-Item -ItemType Directory -Force $resultRoot | Out-Null
$dataRoot = Join-Path $UnityEditor 'Data'
$nunitPackage = Get-ChildItem (Join-Path $repoRoot 'Library/PackageCache') -Directory -Filter 'com.unity.ext.nunit*' | Select-Object -First 1
if (!$nunitPackage) { throw 'Open the Unity project first to restore its NUnit package.' }
$nunit = Join-Path $nunitPackage.FullName 'net40/unity-custom/nunit.framework.dll'
Copy-Item -LiteralPath $nunit -Destination (Join-Path $resultRoot 'nunit.framework.dll')
$testExecutable = Join-Path $resultRoot 'ProductionTests.exe'
$arguments = @('-nologo', '-target:exe', '-nostdlib+', ('-out:"' + $testExecutable + '"'), ('-r:"' + $nunit + '"'))
$arguments += Get-ChildItem (Join-Path $dataRoot 'UnityReferenceAssemblies/unity-4.8-api') -Recurse -Filter '*.dll' | ForEach-Object { '-r:"' + $_.FullName + '"' }
foreach ($directory in @('Assets/Cartograph/Core','Assets/Cartograph/EditorBridge','Assets/Cartograph/EditorUI/NavigationModel','Assets/Cartograph/EditorUI/UndoModel','Assets/Cartograph/EditorUI/Tests','Tests/Standalone')) {
    $arguments += Get-ChildItem (Join-Path $repoRoot $directory) -Recurse -Filter '*.cs' | ForEach-Object { '"' + $_.FullName + '"' }
}
foreach ($file in @('EditorContract.cs','ProjectNavigation.cs','SecondaryNoteContract.cs')) {
    $arguments += '"' + (Join-Path $repoRoot ('Assets/Cartograph/EditorUI/' + $file)) + '"'
}
$responseFile = Join-Path $resultRoot 'tests.rsp'
$arguments | Set-Content -LiteralPath $responseFile -Encoding UTF8
& (Join-Path $dataRoot 'NetCoreRuntime/dotnet.exe') (Join-Path $dataRoot 'DotNetSdkRoslyn/csc.dll') ('@' + $responseFile)
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
$previousTemp = $env:TEMP; $previousTmp = $env:TMP
try {
    $testTemp = Join-Path $resultRoot 'temp'
    New-Item -ItemType Directory -Force $testTemp | Out-Null
    $env:TEMP = $testTemp; $env:TMP = $testTemp
    & (Join-Path $dataRoot 'MonoBleedingEdge/bin/mono.exe') $testExecutable | Tee-Object (Join-Path $resultRoot 'test-results.log')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed. See .results/test-results.log.' }
}
finally { $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
