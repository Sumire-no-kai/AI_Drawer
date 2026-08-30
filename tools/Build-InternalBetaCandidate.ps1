[CmdletBinding()]
param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$')]
    [string]$CandidateLabel = ("local-{0:yyyyMMdd-HHmmss}" -f (Get-Date).ToUniversalTime()),

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture = 'x64',

    [ValidateSet('FrameworkDependent', 'SelfContained')]
    [string]$DependencyMode = 'FrameworkDependent',

    [ValidatePattern('^\d{1,5}\.\d{1,5}\.\d{1,5}\.\d{1,5}$')]
    [string]$PackageVersion = '1.0.0.0',

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
$isSelfContained = $DependencyMode -eq 'SelfContained'
$selfContainedProperty = $isSelfContained.ToString().ToLowerInvariant()
$candidateDirectoryCreated = $false
$candidateCompleted = $false

function Read-PackageManifest {
    param([Parameter(Mandatory)][string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $manifestEntry = $archive.Entries | Where-Object FullName -EQ 'AppxManifest.xml'
        if ($null -eq $manifestEntry) {
            throw 'The generated MSIX does not contain AppxManifest.xml.'
        }

        $manifestStream = $manifestEntry.Open()
        $manifestReader = [System.IO.StreamReader]::new($manifestStream)
        try {
            return [xml]$manifestReader.ReadToEnd()
        }
        finally {
            $manifestReader.Dispose()
            $manifestStream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Resolve-MakeAppxPath {
    param([Parameter(Mandatory)][string]$AssetsPath)

    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
    $packageKey = @($assets.libraries.PSObject.Properties.Name | Where-Object {
        $_ -like 'Microsoft.Windows.SDK.BuildTools/*'
    }) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($packageKey)) {
        throw 'Microsoft.Windows.SDK.BuildTools was not found in project.assets.json.'
    }

    $packageFolder = @($assets.packageFolders.PSObject.Properties.Name) | Select-Object -First 1
    $packageRelativePath = $packageKey.ToLowerInvariant().Replace('/', [System.IO.Path]::DirectorySeparatorChar)
    $packageRoot = Join-Path $packageFolder $packageRelativePath
    $hostArchitecture = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()
    $makeAppx = Get-ChildItem -LiteralPath (Join-Path $packageRoot 'bin') -Filter 'makeappx.exe' -File -Recurse |
        Where-Object { $_.Directory.Name.ToLowerInvariant() -eq $hostArchitecture } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $makeAppx) {
        throw "A MakeAppx executable for the host architecture '$hostArchitecture' was not found."
    }

    return $makeAppx.FullName
}

function Set-InternalPackageVersion {
    param(
        [Parameter(Mandatory)][string]$PackagePath,
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$AssetsPath
    )

    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("AI-Drawer-MsixRepack-{0}" -f [guid]::NewGuid().ToString('N'))
    $expandedDirectory = Join-Path $temporaryRoot 'expanded'
    $repackedPath = Join-Path $temporaryRoot 'repacked.msix'
    try {
        New-Item -ItemType Directory -Path $expandedDirectory -Force | Out-Null
        $makeAppxPath = Resolve-MakeAppxPath -AssetsPath $AssetsPath
        & $makeAppxPath unpack /p $PackagePath /d $expandedDirectory /o
        if ($LASTEXITCODE -ne 0) {
            throw "MakeAppx unpack failed with exit code $LASTEXITCODE."
        }

        $manifestPath = Join-Path $expandedDirectory 'AppxManifest.xml'
        [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw
        $manifest.Package.Identity.Version = $Version
        $manifest.Save($manifestPath)

        & $makeAppxPath pack /d $expandedDirectory /p $repackedPath /o
        if ($LASTEXITCODE -ne 0) {
            throw "MakeAppx pack failed with exit code $LASTEXITCODE."
        }

        [System.IO.File]::Move($repackedPath, $PackagePath, $true)
    }
    finally {
        $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
        $systemTemporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
        $temporaryLeaf = [System.IO.Path]::GetFileName($resolvedTemporaryRoot)
        if ($resolvedTemporaryRoot.StartsWith($systemTemporaryRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
            $temporaryLeaf.StartsWith('AI-Drawer-MsixRepack-', [System.StringComparison]::Ordinal) -and
            [System.IO.Directory]::Exists($resolvedTemporaryRoot)) {
            [System.IO.Directory]::Delete($resolvedTemporaryRoot, $true)
        }
    }
}

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

    if (Test-Path -LiteralPath $resolvedOutputRoot) {
        $outputRootItem = Get-Item -LiteralPath $resolvedOutputRoot
        $outputRootIsReparsePoint = $outputRootItem.Attributes.HasFlag(
            [System.IO.FileAttributes]::ReparsePoint)
        if (-not $outputRootItem.PSIsContainer -or $outputRootIsReparsePoint) {
            throw 'The candidate output root must be a normal directory, not a file or reparse point.'
        }
    }
    else {
        New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
    }

    New-Item -ItemType Directory -Path $packageDirectory -Force | Out-Null
    $candidateDirectoryCreated = $true

    & $DotNetPath restore $projectPath --runtime $runtimeIdentifier "/p:Platform=$platform" `
        "/p:SelfContained=$selfContainedProperty" `
        "/p:WindowsAppSDKSelfContained=$selfContainedProperty"
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
        "/p:SelfContained=$selfContainedProperty"
        "/p:WindowsAppSDKSelfContained=$selfContainedProperty"
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

    $initialManifest = Read-PackageManifest -PackagePath $packages[0].FullName
    if ($initialManifest.Package.Identity.Version -ne $PackageVersion) {
        $assetsPath = Join-Path (Split-Path -Parent $projectPath) 'obj\project.assets.json'
        Set-InternalPackageVersion -PackagePath $packages[0].FullName -Version $PackageVersion -AssetsPath $assetsPath
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($packages[0].FullName)
    try {
        $archiveEntryNames = @($archive.Entries | ForEach-Object FullName)
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

    if ($identity.Version -ne $PackageVersion) {
        throw "Expected package version '$PackageVersion', but found '$($identity.Version)'."
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

    $manifestDependencies = @(
        $packageManifest.SelectNodes(
            '/foundation:Package/foundation:Dependencies/foundation:PackageDependency',
            $namespaceManager) |
            ForEach-Object {
                [ordered]@{
                    name = [string]$_.Name
                    publisher = [string]$_.Publisher
                    minimumVersion = [string]$_.MinVersion
                }
            }
    )

    $containsDotNetRuntime = $archiveEntryNames -contains 'coreclr.dll'
    $containsWindowsAppSdkRuntime = $archiveEntryNames -contains 'Microsoft.WindowsAppRuntime.dll'
    if ($isSelfContained) {
        if (-not $containsDotNetRuntime -or -not $containsWindowsAppSdkRuntime -or $manifestDependencies.Count -ne 0) {
            throw 'The self-contained package did not contain both runtimes without a framework dependency.'
        }
    }
    else {
        $hasWindowsAppRuntimeDependency = $manifestDependencies.name -contains 'Microsoft.WindowsAppRuntime.2'
        if ($containsDotNetRuntime -or $containsWindowsAppSdkRuntime -or -not $hasWindowsAppRuntimeDependency) {
            throw 'The framework-dependent package runtime or manifest dependency contract was not satisfied.'
        }
    }

    $sensitiveFiles = @(Get-ChildItem -LiteralPath $candidateDirectory -File -Recurse | Where-Object Extension -In '.pfx', '.p12', '.key', '.pem')
    if ($sensitiveFiles.Count -gt 0) {
        throw 'Signing material or a private-key file was found in the candidate output.'
    }

    $packageHash = Get-FileHash -LiteralPath $packages[0].FullName -Algorithm SHA256
    $relativePackagePath = [System.IO.Path]::GetRelativePath($candidateDirectory, $packages[0].FullName).Replace('\', '/')
    $utf8WithoutBom = [System.Text.UTF8Encoding]::new($false)
    $deliveryPackages = @(
        Get-ChildItem -LiteralPath $packageDirectory -Filter '*.msix' -File -Recurse |
            Sort-Object FullName
    )
    $checksumText = ($deliveryPackages | ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($candidateDirectory, $_.FullName).Replace('\', '/')
        $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
        "$($hash.Hash.ToLowerInvariant())  $relativePath"
    }) -join [Environment]::NewLine
    $checksumText += [Environment]::NewLine
    [System.IO.File]::WriteAllText(
        (Join-Path $candidateDirectory 'SHA256SUMS.txt'),
        $checksumText,
        $utf8WithoutBom)

    Copy-Item -LiteralPath $limitationsPath -Destination (Join-Path $candidateDirectory 'KNOWN_LIMITATIONS.md')

    $candidateManifest = [ordered]@{
        schemaVersion = 2
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
            dependencyMode = $DependencyMode
            mainPackageBytes = $packages[0].Length
            deliveryPackageCount = $deliveryPackages.Count
            deliveryPackageBytes = ($deliveryPackages | Measure-Object Length -Sum).Sum
            archiveEntryCount = $archiveEntryNames.Count
            containsDotNetRuntime = $containsDotNetRuntime
            containsWindowsAppSdkRuntime = $containsWindowsAppSdkRuntime
            manifestPackageDependencies = $manifestDependencies
            signed = $false
            signatureStatus = [string](Get-AuthenticodeSignature -LiteralPath $packages[0].FullName).Status
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

    $candidateCompleted = $true
    Write-Host "Internal candidate created: $candidateDirectory"
    Write-Host "Package: $relativePackagePath"
    Write-Host "Dependency mode: $DependencyMode"
    Write-Host "Main package bytes: $($packages[0].Length)"
    Write-Host "Delivery MSIX bytes: $(($deliveryPackages | Measure-Object Length -Sum).Sum)"
    Write-Host "SHA-256: $($packageHash.Hash.ToLowerInvariant())"
    Write-Warning 'This MSIX is intentionally unsigned and must not be published as an end-user release.'
}
finally {
    Pop-Location
    if ($candidateDirectoryCreated -and -not $candidateCompleted -and (Test-Path -LiteralPath $candidateDirectory)) {
        try {
            $safeOutputRoot = $resolvedOutputRoot.TrimEnd('\', '/') + [System.IO.Path]::DirectorySeparatorChar
            $safeCandidateDirectory = [System.IO.Path]::GetFullPath($candidateDirectory)
            $isInsideOutputRoot = $safeCandidateDirectory.StartsWith(
                $safeOutputRoot,
                [System.StringComparison]::OrdinalIgnoreCase)
            $hasExpectedName = [System.IO.Path]::GetFileName($safeCandidateDirectory) -eq $CandidateLabel
            $isReparsePoint = (Get-Item -LiteralPath $safeCandidateDirectory).Attributes.HasFlag(
                [System.IO.FileAttributes]::ReparsePoint)
            if (-not $isInsideOutputRoot -or -not $hasExpectedName -or $isReparsePoint) {
                throw 'Refusing to clean an unverified candidate output directory.'
            }

            [System.IO.Directory]::Delete($safeCandidateDirectory, $true)
        }
        catch {
            Write-Warning "Incomplete candidate cleanup failed: $($_.Exception.Message)"
        }
    }
}
