param([string]$InstallerName = "Nocturne.exe")
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
$refs = @('System.dll','System.Core.dll','System.Xaml.dll','Microsoft.VisualBasic.dll','System.Web.Extensions.dll','System.Drawing.dll','System.Windows.Forms.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('PresentationCore.dll','PresentationFramework.dll','WindowsBase.dll') | ForEach-Object { '/reference:' + (Join-Path "$framework\WPF" $_) }
New-Item -ItemType Directory -Force -Path "$root\dist\Nocturne" | Out-Null
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/win32icon:$root\src\Nocturne.ico" "/out:$root\dist\Nocturne\Nocturne.exe" "/resource:$root\src\Main.xaml,Main.xaml" @refs "$root\src\Nocturne.cs" "$root\src\ExplorerFolderPicker.cs" "$root\src\Version.cs"
if ($LASTEXITCODE -ne 0) { throw 'Player compilation failed.' }
Copy-Item "$root\src\Nocturne.exe.config" "$root\dist\Nocturne\Nocturne.exe.config" -Force
Copy-Item "$root\README.md" "$root\dist\Nocturne\README.md" -Force
Copy-Item "$root\THIRD-PARTY-NOTICES.md" "$root\dist\Nocturne\THIRD-PARTY-NOTICES.md" -Force
$selfTest = Start-Process -FilePath "$root\dist\Nocturne\Nocturne.exe" -ArgumentList '--self-test' -PassThru -Wait -WindowStyle Hidden
Get-Content "$root\dist\Nocturne\self-test.txt"
if ($selfTest.ExitCode -ne 0) { throw 'Self-test failed.' }
Remove-Item "$root\dist\Nocturne\self-test.txt"
$installerRefs = @('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','Microsoft.CSharp.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/win32icon:$root\src\Nocturne.ico" "/out:$root\dist\Nocturne\Uninstall.exe" @installerRefs "$root\src\Installer.cs" "$root\src\ExplorerFolderPicker.cs" "$root\src\Version.cs"
if ($LASTEXITCODE -ne 0) { throw 'Uninstaller compilation failed.' }
Compress-Archive -Path "$root\dist\Nocturne\*" -DestinationPath "$root\dist\payload.zip" -Force
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ "/win32icon:$root\src\Nocturne.ico" "/out:$root\dist\$InstallerName" "/resource:$root\dist\payload.zip,payload.zip" @installerRefs "$root\src\Installer.cs" "$root\src\ExplorerFolderPicker.cs" "$root\src\Version.cs"
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
Remove-Item "$root\dist\payload.zip"
Write-Output "Built $root\dist\$InstallerName"


