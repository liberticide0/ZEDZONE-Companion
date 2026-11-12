param([string]$FixtureVersion, [switch]$Local)

$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $runtime = '8.0.33'
    $artifacts = Join-Path (Get-Location) 'artifacts'
    if ($Local -and $FixtureVersion) { throw 'Choose either Local or FixtureVersion.' }
    if ($Local) { $artifacts = Join-Path $artifacts 'local' }
    $publishOptions = @('-p:UpdateFixture=false')
    if ($FixtureVersion) {
        $version = $FixtureVersion
        $artifacts = Join-Path $artifacts 'update-popup-fixtures'
        $publishOptions = @('-p:UpdateFixture=true', "-p:Version=$version")
    } else {
        $version = (dotnet msbuild src/Companion -nologo -getProperty:Version).Trim()
        if ($LASTEXITCODE -ne 0) { throw 'Cannot read the app version.' }
    }
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Invalid app version.' }
    $staging = Join-Path $artifacts "staging-$version"
    dotnet publish src/Companion -c Release -r win-x64 --self-contained true `
        "-p:RuntimeFrameworkVersion=$runtime" -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -o $staging @publishOptions
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    $productVersion = (Get-Item -LiteralPath (Join-Path $staging 'ZEDZONE.Companion.exe')).VersionInfo.ProductVersion
    if (($productVersion -split '\+', 2)[0] -ne $version) { throw 'App and package versions differ.' }

    $cacheLine = dotnet nuget locals global-packages --list
    if ($LASTEXITCODE -ne 0) { throw 'Cannot resolve the NuGet cache.' }
    $packages = ($cacheLine -split ':', 2)[1].Trim()
    $notices = Join-Path $staging 'dotnet-notices'
    New-Item -ItemType Directory -Force -Path $notices | Out-Null
    foreach ($package in 'microsoft.netcore.app.runtime.win-x64', 'microsoft.windowsdesktop.app.runtime.win-x64', 'microsoft.netcore.app.host.win-x64') {
        $runtimePackage = Join-Path (Join-Path $packages $package) $runtime
        $licenseFiles = @(Get-ChildItem -LiteralPath $runtimePackage -File |
            Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' })
        if (-not $licenseFiles.Count) { throw "Missing runtime license files for $package." }
        foreach ($notice in $licenseFiles) {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $notices "$package-$($notice.Name)")
        }
    }

    $pythonVersion = '3.14.10'
    $pythonSha256 = '90905e64a840557d8c1bc4f5b74db3d7ee85e3ffb217611cac1c7813acea4159'
    $downloads = Join-Path (Get-Location) 'artifacts/converter-downloads'
    New-Item -ItemType Directory -Force -Path $downloads | Out-Null
    $pythonFile = "python-$pythonVersion-embed-amd64.zip"
    $pythonZip = Join-Path $downloads $pythonFile
    if (-not (Test-Path -LiteralPath $pythonZip -PathType Leaf)) {
        Invoke-WebRequest "https://www.python.org/ftp/python/$pythonVersion/$pythonFile" -OutFile $pythonZip
    }
    if ((Get-FileHash -LiteralPath $pythonZip -Algorithm SHA256).Hash -ne $pythonSha256) {
        throw 'CPython runtime SHA-256 mismatch.'
    }
    $converter = Join-Path $staging 'converter'
    New-Item -ItemType Directory -Force -Path $converter | Out-Null
    $python = Join-Path $converter 'python'
    Expand-Archive -LiteralPath $pythonZip -DestinationPath $python -Force
    if (-not (Test-Path -LiteralPath (Join-Path $python 'LICENSE.txt'))) {
        throw 'CPython redistribution notice is missing.'
    }
    Copy-Item -LiteralPath scripts/converter-bootstrap.py, scripts/converter-packages.json, scripts/probe-icons.py -Destination $converter
    Copy-Item -LiteralPath README.md, LICENSE, scripts/probe-recipes.py -Destination $staging
    $zip = Join-Path $artifacts "ZEDZONE-Companion-$version-win-x64.zip"
    $contents = 'ZEDZONE.Companion.exe', 'README.md', 'LICENSE', 'probe-recipes.py', 'dotnet-notices', 'converter' |
        ForEach-Object { Join-Path $staging $_ }
    Compress-Archive -LiteralPath $contents -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $zip)" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
    Get-Item -LiteralPath $zip
}
finally {
    Pop-Location
}
