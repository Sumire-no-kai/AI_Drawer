[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$CandidateDirectory,

    [string]$DownloadedPackagePath,

    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ExpectedSourceCommit,

    [string]$ExpectedPublisher,

    [ValidatePattern('^[0-9a-fA-F]{40}$')]
    [string]$ExpectedSignerThumbprint,

    [switch]$RequireCleanSource,

    [string]$OutputPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-ChildPath {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][string]$RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "Candidate path must be relative: $RelativePath"
    }

    $resolvedRoot = [System.IO.Path]::GetFullPath($Root).TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
    $resolvedPath = [System.IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    if (-not $resolvedPath.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Candidate path escapes its directory: $RelativePath"
    }

    return $resolvedPath
}

function Read-AppxManifest {
    param([Parameter(Mandatory)][string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        if ($archive.Entries.Count -gt 100000) {
            throw 'The package contains an unreasonable number of archive entries.'
        }

        $sensitiveEntries = @($archive.Entries | Where-Object {
            [System.IO.Path]::GetExtension($_.FullName) -in '.pfx', '.p12', '.key', '.pem'
        })
        if ($sensitiveEntries.Count -gt 0) {
            throw 'Private-key or signing-material extensions were found inside the package.'
        }

        $manifestEntry = $archive.Entries | Where-Object FullName -EQ 'AppxManifest.xml'
        if ($null -eq $manifestEntry) {
            throw 'The package does not contain AppxManifest.xml.'
        }
        if ($manifestEntry.Length -gt 1MB) {
            throw 'AppxManifest.xml exceeds the accepted size limit.'
        }

        $stream = $manifestEntry.Open()
        $reader = [System.IO.StreamReader]::new($stream)
        try {
            return [ordered]@{
                manifest = [xml]$reader.ReadToEnd()
                hasPackageSignature = $null -ne ($archive.Entries | Where-Object FullName -EQ 'AppxSignature.p7x')
                entryCount = $archive.Entries.Count
                entryNames = @($archive.Entries | ForEach-Object FullName)
            }
        }
        finally {
            $reader.Dispose()
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

$resolvedCandidateDirectory = [System.IO.Path]::GetFullPath($CandidateDirectory)
if (-not (Test-Path -LiteralPath $resolvedCandidateDirectory -PathType Container)) {
    throw "Candidate directory was not found: $resolvedCandidateDirectory"
}
if ((Get-Item -LiteralPath $resolvedCandidateDirectory).Attributes.HasFlag(
        [System.IO.FileAttributes]::ReparsePoint)) {
    throw 'Candidate directory must not be a reparse point.'
}
$candidateReparseEntries = @(Get-ChildItem -LiteralPath $resolvedCandidateDirectory -Force -Recurse | Where-Object {
    $_.Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)
})
if ($candidateReparseEntries.Count -gt 0) {
    throw 'Candidate contents must not contain reparse points.'
}

$candidateManifestPath = Join-Path $resolvedCandidateDirectory 'candidate-manifest.json'
$checksumPath = Join-Path $resolvedCandidateDirectory 'SHA256SUMS.txt'
if (-not (Test-Path -LiteralPath $candidateManifestPath -PathType Leaf)) {
    throw 'candidate-manifest.json was not found.'
}
if (-not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
    throw 'SHA256SUMS.txt was not found.'
}
if ((Get-Item -LiteralPath $candidateManifestPath).Length -gt 1MB) {
    throw 'candidate-manifest.json exceeds the accepted size limit.'
}
if ((Get-Item -LiteralPath $checksumPath).Length -gt 1MB) {
    throw 'SHA256SUMS.txt exceeds the accepted size limit.'
}

$candidate = Get-Content -LiteralPath $candidateManifestPath -Raw | ConvertFrom-Json
if ($candidate.schemaVersion -ne 2) {
    throw "Unsupported candidate manifest schema: $($candidate.schemaVersion)"
}
if ($candidate.candidateKind -notin 'internal-unsigned-msix', 'public-signed-msix') {
    throw "Unexpected candidate kind: $($candidate.candidateKind)"
}
if (($candidate.candidateKind -eq 'internal-unsigned-msix' -and $candidate.distribution.publicReleaseAllowed) -or
    ($candidate.candidateKind -eq 'public-signed-msix' -and -not $candidate.distribution.publicReleaseAllowed)) {
    throw 'Candidate kind and public-release permission disagree.'
}
if ($RequireCleanSource -and $candidate.source.dirty) {
    throw 'The candidate was generated from a dirty source tree.'
}
if ((-not [string]::IsNullOrWhiteSpace($ExpectedSourceCommit)) -and
    (-not [string]::Equals(
        $candidate.source.commit,
        $ExpectedSourceCommit,
        [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "Source commit mismatch. Expected $ExpectedSourceCommit, found $($candidate.source.commit)."
}

$packagePath = Resolve-ChildPath -Root $resolvedCandidateDirectory -RelativePath $candidate.package.file
if (-not (Test-Path -LiteralPath $packagePath -PathType Leaf)) {
    throw "Candidate package was not found: $packagePath"
}

$packageHash = Get-FileHash -LiteralPath $packagePath -Algorithm SHA256
if (-not [string]::Equals(
    $packageHash.Hash,
    $candidate.package.sha256,
    [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'The main package SHA-256 does not match candidate-manifest.json.'
}
if ((Get-Item -LiteralPath $packagePath).Length -ne $candidate.package.mainPackageBytes) {
    throw 'The main package byte count does not match candidate-manifest.json.'
}

$sensitiveCandidateFiles = @(Get-ChildItem -LiteralPath $resolvedCandidateDirectory -File -Recurse | Where-Object {
    $_.Extension -in '.pfx', '.p12', '.key', '.pem'
})
if ($sensitiveCandidateFiles.Count -gt 0) {
    throw 'Private-key or signing-material extensions were found in the candidate directory.'
}

$checksumLines = @(Get-Content -LiteralPath $checksumPath | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($checksumLines.Count -gt 100) {
    throw 'SHA256SUMS.txt contains too many artifact entries.'
}
if ($checksumLines.Count -ne $candidate.package.deliveryPackageCount) {
    throw "Checksum entry count mismatch. Expected $($candidate.package.deliveryPackageCount), found $($checksumLines.Count)."
}

$checksumTotalBytes = 0L
foreach ($line in $checksumLines) {
    if ($line -notmatch '^([0-9a-fA-F]{64})  (.+)$') {
        throw "Invalid checksum line: $line"
    }

    $artifactPath = Resolve-ChildPath -Root $resolvedCandidateDirectory -RelativePath $Matches[2]
    if (-not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
        throw "Checksummed artifact was not found: $($Matches[2])"
    }
    if ((Get-Item -LiteralPath $artifactPath).Attributes.HasFlag([System.IO.FileAttributes]::ReparsePoint)) {
        throw "Checksummed artifact must not be a reparse point: $($Matches[2])"
    }

    $artifactHash = Get-FileHash -LiteralPath $artifactPath -Algorithm SHA256
    if (-not [string]::Equals($artifactHash.Hash, $Matches[1], [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Checksum mismatch for $($Matches[2])."
    }
    $checksumTotalBytes += (Get-Item -LiteralPath $artifactPath).Length
}
if ($checksumTotalBytes -ne $candidate.package.deliveryPackageBytes) {
    throw "Delivery byte count mismatch. Expected $($candidate.package.deliveryPackageBytes), found $checksumTotalBytes."
}

$packageInspection = Read-AppxManifest -PackagePath $packagePath
$identity = $packageInspection.manifest.Package.Identity
if (-not [string]::Equals(
        [string]$identity.Name,
        'AIDrawer.App',
        [System.StringComparison]::Ordinal)) {
    throw "Unexpected package identity. Expected 'AIDrawer.App', found '$($identity.Name)'."
}
foreach ($comparison in @(
    @('identity name', [string]$identity.Name, [string]$candidate.package.identityName),
    @('identity version', [string]$identity.Version, [string]$candidate.package.identityVersion),
    @('publisher', [string]$identity.Publisher, [string]$candidate.package.publisher),
    @('architecture', [string]$identity.ProcessorArchitecture, [string]$candidate.package.architecture)
)) {
    if (-not [string]::Equals($comparison[1], $comparison[2], [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Package $($comparison[0]) mismatch. Expected '$($comparison[2])', found '$($comparison[1])'."
    }
}
if ($packageInspection.entryCount -ne $candidate.package.archiveEntryCount) {
    throw 'The package entry count does not match candidate-manifest.json.'
}

$containsDotNetRuntime = $packageInspection.entryNames -contains 'coreclr.dll'
$containsWindowsAppSdkRuntime = $packageInspection.entryNames -contains 'Microsoft.WindowsAppRuntime.dll'
$dotNetRuntimeMatches = $containsDotNetRuntime -eq $candidate.package.containsDotNetRuntime
$windowsAppSdkRuntimeMatches = $containsWindowsAppSdkRuntime -eq $candidate.package.containsWindowsAppSdkRuntime
if (-not $dotNetRuntimeMatches -or -not $windowsAppSdkRuntimeMatches) {
    throw 'The package runtime contents do not match candidate-manifest.json.'
}
if ($candidate.package.dependencyMode -eq 'FrameworkDependent') {
    if ($containsDotNetRuntime -or $containsWindowsAppSdkRuntime) {
        throw 'A framework-dependent candidate unexpectedly contains self-contained runtimes.'
    }
}
elseif ($candidate.package.dependencyMode -eq 'SelfContained') {
    if (-not $containsDotNetRuntime -or -not $containsWindowsAppSdkRuntime) {
        throw 'A self-contained candidate does not contain both required runtimes.'
    }
}
else {
    throw "Unexpected dependency mode: $($candidate.package.dependencyMode)"
}

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($packageInspection.manifest.NameTable)
$namespaceManager.AddNamespace('foundation', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$namespaceManager.AddNamespace('uap5', 'http://schemas.microsoft.com/appx/manifest/uap/windows10/5')
$namespaceManager.AddNamespace('rescap', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities')
$startupTask = $packageInspection.manifest.SelectSingleNode(
    '/foundation:Package/foundation:Applications/foundation:Application/foundation:Extensions/uap5:Extension[@Category="windows.startupTask" and @Executable="AIDrawer.App.exe" and @EntryPoint="Windows.FullTrustApplication"]/uap5:StartupTask[@TaskId="AIDrawerStartupTask" and @Enabled="false"]',
    $namespaceManager)
if ($candidate.package.startupTaskDeclared -ne ($null -ne $startupTask)) {
    throw 'The startup-task declaration does not match candidate-manifest.json.'
}

$capabilities = @($packageInspection.manifest.SelectNodes(
    '/foundation:Package/foundation:Capabilities/*',
    $namespaceManager))
$hasReviewedCapability = $capabilities.Count -eq 1
if ($hasReviewedCapability) {
    $hasReviewedCapability = $capabilities[0].NamespaceURI -eq 'http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities'
    $hasReviewedCapability = $hasReviewedCapability -and $capabilities[0].LocalName -eq 'Capability'
    $hasReviewedCapability = $hasReviewedCapability -and $capabilities[0].GetAttribute('Name') -eq 'runFullTrust'
    $hasReviewedCapability = $hasReviewedCapability -and @($candidate.package.capabilities).Count -eq 1
    $hasReviewedCapability = $hasReviewedCapability -and $candidate.package.capabilities[0] -eq 'runFullTrust'
}
if (-not $hasReviewedCapability) {
    throw 'The package capabilities do not match the reviewed runFullTrust-only contract.'
}

$inspectedDependencyNames = @(
    $packageInspection.manifest.SelectNodes(
        '/foundation:Package/foundation:Dependencies/foundation:PackageDependency',
        $namespaceManager) |
        ForEach-Object { [string]$_.Name } |
        Sort-Object
)
$recordedDependencyNames = @(
    $candidate.package.manifestPackageDependencies |
        ForEach-Object { [string]$_.name } |
        Sort-Object
)
if (@(Compare-Object $inspectedDependencyNames $recordedDependencyNames).Count -gt 0) {
    throw 'Manifest package dependencies do not match candidate-manifest.json.'
}

$authenticode = Get-AuthenticodeSignature -LiteralPath $packagePath
$isSigned = $authenticode.Status -eq [System.Management.Automation.SignatureStatus]::Valid
if ([string]$authenticode.Status -ne [string]$candidate.package.signatureStatus) {
    throw 'Authenticode status does not match candidate-manifest.json.'
}
if ($candidate.package.signed -ne $isSigned) {
    throw "Candidate signed-state mismatch. Manifest says $($candidate.package.signed); inspection says $isSigned."
}
if ($candidate.package.signed -ne $packageInspection.hasPackageSignature) {
    throw 'The package signature entry does not agree with candidate-manifest.json.'
}

if ($candidate.distribution.publicReleaseAllowed) {
    if ([string]::IsNullOrWhiteSpace($ExpectedSourceCommit) -or
        [string]::IsNullOrWhiteSpace($ExpectedPublisher) -or
        [string]::IsNullOrWhiteSpace($ExpectedSignerThumbprint)) {
        throw 'A public candidate requires independently supplied expected source commit, publisher, and signer thumbprint.'
    }
    if ($candidate.source.dirty -or -not $isSigned) {
        throw 'A public candidate must have clean source and a valid Authenticode signature.'
    }
    if (-not [string]::Equals(
            [string]$identity.Publisher,
            $ExpectedPublisher,
            [System.StringComparison]::Ordinal)) {
        throw "Public package publisher mismatch. Expected '$ExpectedPublisher', found '$($identity.Publisher)'."
    }
    if ($null -eq $authenticode.SignerCertificate -or
        -not [string]::Equals(
            $authenticode.SignerCertificate.Thumbprint,
            $ExpectedSignerThumbprint,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw 'Public package signer thumbprint does not match the independently supplied approved certificate.'
    }

    $releaseNotesPath = Join-Path $resolvedCandidateDirectory 'RELEASE_NOTES.md'
    if ((-not (Test-Path -LiteralPath $releaseNotesPath -PathType Leaf)) -or
        ((Get-Item -LiteralPath $releaseNotesPath).Length -eq 0)) {
        throw 'A public candidate must include non-empty RELEASE_NOTES.md.'
    }
}

$downloadHashMatches = $null
if (-not [string]::IsNullOrWhiteSpace($DownloadedPackagePath)) {
    $resolvedDownloadedPath = [System.IO.Path]::GetFullPath($DownloadedPackagePath)
    if (-not (Test-Path -LiteralPath $resolvedDownloadedPath -PathType Leaf)) {
        throw "Downloaded package was not found: $resolvedDownloadedPath"
    }

    $downloadHash = Get-FileHash -LiteralPath $resolvedDownloadedPath -Algorithm SHA256
    $downloadHashMatches = [string]::Equals(
        $downloadHash.Hash,
        $candidate.package.sha256,
        [System.StringComparison]::OrdinalIgnoreCase)
    if (-not $downloadHashMatches) {
        throw 'The downloaded package does not match the candidate SHA-256.'
    }
}

$report = [ordered]@{
    schemaVersion = 1
    verifiedAtUtc = (Get-Date).ToUniversalTime().ToString('O')
    candidateDirectory = $resolvedCandidateDirectory
    sourceCommit = [string]$candidate.source.commit
    sourceDirty = [bool]$candidate.source.dirty
    dependencyMode = [string]$candidate.package.dependencyMode
    packagePath = $packagePath
    sha256 = $packageHash.Hash.ToLowerInvariant()
    deliveryPackageCount = [int]$candidate.package.deliveryPackageCount
    deliveryPackageBytes = [long]$checksumTotalBytes
    authenticodeStatus = [string]$authenticode.Status
    certificateSubject = if ($null -eq $authenticode.SignerCertificate) { $null } else { $authenticode.SignerCertificate.Subject }
    certificateThumbprint = if ($null -eq $authenticode.SignerCertificate) { $null } else { $authenticode.SignerCertificate.Thumbprint }
    packageSignatureEntry = [bool]$packageInspection.hasPackageSignature
    downloadedPackageMatches = $downloadHashMatches
    publicReleaseAllowed = [bool]$candidate.distribution.publicReleaseAllowed
    passed = $true
}
$reportObject = [pscustomobject]$report

if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    $resolvedOutputPath = [System.IO.Path]::GetFullPath($OutputPath)
    $outputDirectory = Split-Path -Parent $resolvedOutputPath
    if (-not [string]::IsNullOrWhiteSpace($outputDirectory)) {
        New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
    }
    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        $resolvedOutputPath,
        (($reportObject | ConvertTo-Json -Depth 5) + [Environment]::NewLine),
        $utf8WithoutBom)
}

$reportObject
