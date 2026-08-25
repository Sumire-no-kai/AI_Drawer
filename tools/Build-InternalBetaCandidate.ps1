[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')]
    [string]$CandidateLabel = ("local-{0:yyyyMMdd-HHmmss}" -f (Get-Date).ToUniversalTime()),

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture = 'x64',

    [string]$DotNetPath = 'dotnet',

    [string]$OutputRoot = 'artifacts\beta-candidates',

    [switch]$AllowDirtySource
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$projectPath = Join-Path $repositoryRoot 'src\AIDrawer.App\AIDrawer.App.csproj'
$limitationsPath = Join-Path $repositoryRoot 'docs\release\KNOWN_LIMITATIONS.md'
$resolvedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputRoot)) {
    [System.IO.Path]::GetFullPath($OutputRoot)
}
else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputRoot))
}
$candidateDirectory = Join-Path $resolvedOutputRoot $CandidateLabel
$packageDirectory = Join-Path $candidateDirectory 'package'
$normalizedArchitecture = $Architecture.ToLowerInvariant()
$platform = if ($normalizedArchitecture -eq 'arm64') { 'ARM64' } else { $normalizedArchitecture }
$runtimeIdentifier = "win-$normalizedArchitecture"

Push-Location $repositoryRoot
try {
    $sourceStatus = @(& git status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) {
        throw 'Unable to inspect the Git working tree.'
    }

    $sourceIsDirty = $sourceStatus.Count -gt 0
    if ($sourceIsDirty -and -not $AllowDirtySource) {
        throw 'The working tree is not clean. Commit the source or pass -AllowDirtySource for a local development probe.'
    }

    $sourceCommit = (& git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($sourceCommit)) {
        throw 'Unable to resolve the source commit.'
    }

    if (Test-Path -LiteralPath $candidateDirectory) {
        throw "Candidate output already exists: $candidateDirectory"
    }

    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null

    & $DotNetPath restore $projectPath --runtime $runtimeIdentifier "/p:Platform=$platform"
    if ($LASTEXITCODE -ne 0) {
        throw "Restore failed with exit code $LASTEXITCODE."
    }

    $packageDirectoryWithSeparator = $packageDirectory.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $buildArguments = @(
        'msbuild'
        $projectPath
        '/t:Build'
        '/m'
        '/p:Configuration=Release'
        "/p:Platform=$platform"
        "/p:RuntimeIdentifier=$runtimeIdentifier"
        '/p:GenerateAppxPackageOnBuild=true'
        '/p:AppxPackageSigningEnabled=false'
        "/p:AppxPackageDir=$packageDirectoryWithSeparator"
        '/p:UapAppxPackageBuildMode=SideloadOnly'
        '/p:AppxBundle=Never'
        '/p:PublishReadyToRun=false'
    )

    & $DotNetPath @buildArguments
    if ($LASTEXITCODE -ne 0) {
        throw "MSIX build failed with exit code $LASTEXITCODE."
    }

    $packages = @(
        Get-ChildItem -LiteralPath $packageDirectory -Filter '*.msix' -File -Recurse |
            Where-Object FullName -NotMatch '[\\/]Dependencies[\\/]'
    )
    if ($packages.Count -ne 1) {
        throw "Expected exactly one MSIX package, but found $($packages.Count)."
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $signatureEntry = $archive.Entries | Where-Object FullName -EQ 'AppxSignature.p7x'
        if ($null -ne $signatureEntry) {
            throw 'The internal candidate unexpectedly contains an MSIX signature.'
        }

        $sensitiveArchiveEntries = @(
            $archive.Entries |
                Where-Object { [System.IO.Path]::GetExtension($_.FullName) -in '.pfx', '.p12', '.key', '.pem' }
        )
        if ($sensitiveArchiveEntries.Count -gt 0) {
            throw 'Signing material or a private-key file was found inside the generated MSIX.'
        }

        $manifestEntry = $archive.Entries | Where-Object FullName -EQ 'AppxManifest.xml'
        if ($null -eq $manifestEntry) {
            throw 'The generated MSIX does not contain AppxManifest.xml.'
        }

        $manifestStream = $manifestEntry.Open()
        $manifestReader = [System.IO.StreamReader]::new($manifestStream)
        try {
            [xml]$packageManifest = $manifestReader.ReadToEnd()
        }
        finally {
            $manifestReader.Dispose()
            $manifestStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }

    $identity = $packageManifest.Package.Identity
    if ($identity.Name -ne 'AIDrawer.App') {
        throw "Expected package identity 'AIDrawer.App', but found '$($identity.Name)'."
    }

    if ($identity.ProcessorArchitecture -ne $normalizedArchitecture) {
        throw "Expected package architecture '$normalizedArchitecture', but found '$($identity.ProcessorArchitecture)'."
    }

    $namespaceManager = [System.Xml.XmlNamespaceManager]::new($packageManifest.NameTable)
    $namespaceManager.AddNamespace('foundation', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
    $namespaceManager.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')
    $namespaceManager.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')
    $startupTask = $packageManifest.SelectSingleNode(
        '/foundation:Package/foundation:Applications/foundation:Application/foundation:Extensions/uap5:Extension[@Category="windows.startupTask" and @Executable="AIDrawer.App.exe" and @EntryPoint="Windows.FullTrustApplication"]/uap5:StartupTask[@TaskId="AIDrawerStartupTask" and @Enabled="false"]',
        $namespaceManager)
    if ($null -eq $startupTask) {
        throw 'The package is missing the disabled AIDrawerStartupTask declaration for the packaged executable.'
    }

    $capabilities = @($packageManifest.SelectNodes('/foundation:Package/foundation:Capabilities/*', $namespaceManager))
    $hasExpectedCapabilities = $capabilities.Count -eq 1
    if ($hasExpectedCapabilities) {
        $hasExpectedCapabilities = $capabilities[0].NamespaceURI -eq 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities'
        $hasExpectedCapabilities = $hasExpectedCapabilities -and $capabilities[0].LocalName -eq 'Capability'
        $hasExpectedCapabilities = $hasExpectedCapabilities -and $capabilities[0].GetAttribute('Name') -eq 'runFullTrust'
    }
    if (-not $hasExpectedCapabilities) {
        throw 'The package capabilities must contain only the reviewed runFullTrust capability.'
    }

    $sensitiveFiles = @(Get-ChildItem -LiteralPath $candidateDirectory -File -Recurse | Where-Object Extension -In '.pfx', '.p12', '.key', '.pem')
    if ($sensitiveFiles.Count -gt 0) {
        throw 'Signing material or a private-key file was found in the candidate output.'
    }

    $packageHash = Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256
    $relativePackagePath = [System.IO.Path]::GetRelativePath($candidateDirectory, $packages[0].FullName).Replace('\', '/')
    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    $checksumText = "$($packageHash.Hash.ToLowerInvariant())  $relativePackagePath$([Environment]::NewLine)"
    [System.IO.File]::WriteAllText(
        (Join-Path $candidateDirectory 'SHA256SUMS.txt'),
        $checksumText,
        $utf8WithoutBom)

    Copy-Item -LiteralPath $limitationsPath -Destination (Join-Path $candidateDirectory 'KNOWN_LIMITATIONS.md')

    $candidateManifest = [ordered]@{
        schemaVersion = 1
        candidateKind = 'internal-unsigned-msix'
        candidateLabel = $CandidateLabel
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
        source = [ordered]@{
            commit = $sourceCommit
            dirty = $sourceIsDirty
        }
        package = [ordered]@{
            file = $relativePackagePath
            sha256 = $packageHash.Hash.ToLowerInvariant()
            identityName = [string]$identity.Name
            identityVersion = [string]$identity.Version
            publisher = [string]$identity.Publisher
            architecture = [string]$identity.ProcessorArchitecture
            signed = $false
            startupTaskDeclared = $true
            capabilities = @('runFullTrust')
        }
        distribution = [ordered]@{
            publicReleaseAllowed = $false
            purpose = 'Build-pipeline and packaging validation only'
        }
    }

    $candidateManifestJson = ($candidateManifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine
    [System.IO.File]::WriteAllText(
        (Join-Path $candidateDirectory 'candidate-manifest.json'),
        $candidateManifestJson,
        $utf8WithoutBom)

    Write-Host "Internal candidate created: $candidateDirectory"
    Write-Host "Package: $relativePackagePath"
    Write-Host "SHA-256: $($packageHash.Hash.ToLowerInvariant())"
    Write-Warning 'This MSIX is intentionally unsigned and must not be published as an end-user release.'
}
finally {
    Pop-Location
}
