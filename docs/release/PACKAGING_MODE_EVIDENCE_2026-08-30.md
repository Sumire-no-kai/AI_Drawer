# Internal MSIX dependency-mode comparison — 2026-08-30

This is local build and package-inspection evidence from one Windows 11 x64 development host. It is not installation, startup, ARM64 device, signing, Store, or public-release evidence.

## Method

- Source baseline: merged `origin/master` commit `a4bc03a1d15041aa785513bc0bdc67d172a1ae94`, with the current uncommitted release-hardening changes included and recorded as dirty.
- SDK: repository-local .NET SDK `10.0.400`.
- Package version: `1.0.0.0`; publisher remained the unapproved placeholder `CN=AppPublisher`.
- ReadyToRun was disabled for the comparison.
- Every output was intentionally unsigned and was independently re-read by `tools/Test-BetaCandidate.ps1`.
- The builder verified identity, architecture, version, disabled startup task, the sole reviewed `runFullTrust` capability, signature absence, no private-key extensions, all emitted MSIX hashes, and delivery byte totals.

## Results

| Architecture | Dependency mode | Main MSIX | Delivery MSIX count | Total MSIX bytes | Runtime evidence |
| --- | --- | ---: | ---: | ---: | --- |
| x64 | FrameworkDependent | 29,445,987 bytes | 5 | 159,029,775 bytes | Main package contains neither .NET runtime nor Windows App SDK runtime; manifest depends on `Microsoft.WindowsAppRuntime.2`. |
| x64 | SelfContained | 91,345,402 bytes | 1 | 91,345,402 bytes | Main package contains both .NET and Windows App SDK runtimes; no manifest package dependency. |
| ARM64 | FrameworkDependent | 29,017,960 bytes | 5 | 158,601,748 bytes | Main package contains neither .NET runtime nor Windows App SDK runtime; manifest depends on `Microsoft.WindowsAppRuntime.2`. |
| ARM64 | SelfContained | 87,879,391 bytes | 1 | 87,879,391 bytes | Main package contains both .NET and Windows App SDK runtimes; no manifest package dependency. |

The framework-dependent total above includes every dependency MSIX emitted by the sideload build, including multiple dependency architectures. Microsoft Store dependency acquisition is a different delivery path, so this number must not be used as the Store download size.

## Interpretation

- For a zero-budget GitHub prerelease intended for informed testers, the self-contained output is operationally simpler: one architecture-specific file and no separate Windows App Runtime package. It is approximately 62 MB larger than the framework-dependent main package, but smaller than the full offline sideload folder emitted by this tool.
- For Microsoft Store distribution, framework-dependent packaging remains a credible option because the Store can manage framework dependencies. That route requires exact Store package testing before choosing it.
- No final mode is approved. Clean install, update, startup-task, rollback, uninstall, cold-start, working-set, servicing, and signed-artifact tests on the intended distribution channel remain required.
- A previous ARM64 candidate inherited `SelfContained=true` from its publish profile while documentation described the overall candidate as framework-dependent. The updated tool now passes both `.NET` and Windows App SDK dependency properties explicitly so x64 and ARM64 candidates cannot silently use different modes.
