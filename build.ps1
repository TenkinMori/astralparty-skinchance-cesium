$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ver = '1.0.1'

dotnet build "$root\src\SkinChanceMod" -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败" }

$out = Join-Path $root 'dist\SkinChanceMod'
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item "$root\src\SkinChanceMod\bin\Release\netstandard2.0\SkinChanceMod.dll" $out -Force
Copy-Item "$root\src\SkinChanceMod\SkinChanceMod.json" $out -Force
Copy-Item "$root\icon.png" $out -Force
Copy-Item "$root\SKINS.md" $out -Force

$zip = Join-Path $root "dist\SkinChanceMod-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip

Write-Host "OK -> $out"
Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize
Get-ChildItem $zip | Select-Object Name, Length | Format-Table -AutoSize
