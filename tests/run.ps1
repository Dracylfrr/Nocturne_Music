$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$run=Join-Path $root ('qa\updates-'+[guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force $run | Out-Null
$f='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
& "$f\csc.exe" /nologo "/out:$run\OldPlayer.exe" "$PSScriptRoot\OldPlayer.cs"
if($LASTEXITCODE -ne 0){throw 'Test fixture compilation failed'}
$refs=@('System.dll','System.Core.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll') | ForEach-Object {"/reference:$f\$_"}
& "$f\csc.exe" /nologo "/out:$run\UpdateTests.exe" @refs "$PSScriptRoot\UpdateTests.cs" "$root\src\GitHubUpdates.cs" "$root\src\UpdateInstall.cs"
if($LASTEXITCODE -ne 0){throw 'Test compilation failed'}
& "$run\UpdateTests.exe" $run "$root\dist\Nocturne.exe" "$run\OldPlayer.exe"
if($LASTEXITCODE -ne 0){throw 'Updater tests failed'}
Write-Output "Test artifacts: $run"
