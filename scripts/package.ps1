$ErrorActionPreference = 'Stop'
Push-Location (Split-Path -Parent $PSScriptRoot)
try {
    $runtime = '8.0.33'
    $version = '0.1.1'
    $staging = Join-Path (Get-Location) 'artifacts/staging'
    dotnet publish src/Companion -c Release -r win-x64 --self-contained true `
        "-p:RuntimeFrameworkVersion=$runtime" -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -o $staging
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

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

    Copy-Item -LiteralPath README.md, LICENSE -Destination $staging
    $zip = Join-Path (Get-Location) "artifacts/ZEDZONE-Companion-$version-win-x64.zip"
    $contents = 'ZEDZONE.Companion.exe', 'README.md', 'LICENSE', 'dotnet-notices' |
        ForEach-Object { Join-Path $staging $_ }
    Compress-Archive -LiteralPath $contents -DestinationPath $zip -Force
    $hash = (Get-FileHash -LiteralPath $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $zip)" | Set-Content -LiteralPath "$zip.sha256" -Encoding ascii
    Get-Item -LiteralPath $zip
}
finally {
    Pop-Location
}
