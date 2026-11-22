$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$ver = '1.2.4'

dotnet build "$root\src\SkinChanceMod" -c Release -v q --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet build failed" }

$out = Join-Path $root 'dist\SkinChanceMod'
New-Item -ItemType Directory -Force $out | Out-Null
Copy-Item "$root\src\SkinChanceMod\bin\Release\netstandard2.0\SkinChanceMod.dll" $out -Force
Copy-Item "$root\src\SkinChanceMod\SkinChanceMod.json" $out -Force
Copy-Item "$root\icon.png" $out -Force
Copy-Item "$root\SKINS.md" $out -Force
Copy-Item "$root\使用说明.md" $out -Force

# Pre-built folder tree for every skin (unzip into the mod directory).
$py = 'C:\Users\DELL\.dsh\dsh-runtimes\dsh-primary-runtime\dependencies\python\python.exe'
if (Test-Path $py) {
    $env:PYTHONIOENCODING = 'utf-8'
    & $py "$root\tools\make_skins_zip.py" "$root\dist\skins.zip"
} elseif (-not (Test-Path "$root\dist\skins.zip")) {
    Write-Warning "python not found, skins.zip skipped"
}

$zip = Join-Path $root "dist\SkinChanceMod-$ver.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path "$out\*" -DestinationPath $zip

Write-Host "OK -> $out"
Get-ChildItem $out | Select-Object Name, Length | Format-Table -AutoSize
Get-ChildItem "$root\dist" -Filter '*.zip' | Select-Object Name, Length | Format-Table -AutoSize
