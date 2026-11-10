param(
    [ValidateSet('ok', 'none', 'bad-hash', 'unavailable')][string]$Mode = 'ok',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$StartVersion = '0.4.8',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$TargetVersion = '0.4.9',
    [ValidateRange(0, 2)][double]$Delay = 0
)

$ErrorActionPreference = 'Stop'
$fixtures = Join-Path (Split-Path -Parent $PSScriptRoot) 'artifacts/update-popup-fixtures'
$startZip = (Resolve-Path -LiteralPath (Join-Path $fixtures "ZEDZONE-Companion-$StartVersion-win-x64.zip")).Path
$targetZip = (Resolve-Path -LiteralPath (Join-Path $fixtures "ZEDZONE-Companion-$TargetVersion-win-x64.zip")).Path
$install = [IO.Directory]::CreateTempSubdirectory('ZEDZONE-Companion-popup-').FullName
Expand-Archive -LiteralPath $startZip -DestinationPath $install

$data = Join-Path $install '.qa-local/ZEDZONE.Companion'
New-Item -ItemType Directory -Path $data -Force | Out-Null
$source = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'ZEDZONE.Companion'
if (Test-Path -LiteralPath $source -PathType Container) {
    Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -match '^recipes-\d+\.json$' } |
        ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $data }
}
@'
{"Version":1,"Goals":[{"Id":29,"Name":"CNC 부품","Quantity":2}],"Preferences":[],"Collected":[{"Build":"26554206","Id":3,"Name":"철광석","Quantity":8}]}
'@ | Set-Content -LiteralPath (Join-Path $data 'wishlist.json') -Encoding utf8

Write-Host "Test install: $install"
Write-Host "Close the fixture app and press Ctrl+C in this terminal when done."
$feedRuntime = Join-Path $install '.qa-local/feed-runtime'
Copy-Item -LiteralPath (Join-Path $install 'converter/python') -Destination $feedRuntime -Recurse
Start-Process -FilePath (Join-Path $install 'ZEDZONE.Companion.exe')
$delayArg = $Delay.ToString([Globalization.CultureInfo]::InvariantCulture)
& (Join-Path $feedRuntime 'python.exe') (Join-Path $PSScriptRoot 'update-test-feed.py') $targetZip --mode $Mode --delay $delayArg
if ($LASTEXITCODE -ne 0) { throw 'The local update fixture could not run.' }
