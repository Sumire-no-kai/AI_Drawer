[CmdletBinding()]
param(
    [string]$AppPath,
    [string]$OutputPath,
    [ValidateRange(1, 30)]
    [int]$GracePeriodMinutes = 5,
    [switch]$SkipFiveMinuteWait
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($AppPath)) {
    $AppPath = Join-Path $repositoryRoot 'src\AIDrawer.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\AIDrawer.App.exe'
}

$AppPath = [IO.Path]::GetFullPath($AppPath)
if (-not (Test-Path -LiteralPath $AppPath -PathType Leaf)) {
    throw "AI Drawer executable was not found: $AppPath"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $OutputPath = Join-Path $repositoryRoot "artifacts\runtime-acceptance\$stamp.json"
}

$OutputPath = [IO.Path]::GetFullPath($OutputPath)
$outputDirectory = Split-Path -Parent $OutputPath
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class AIDrawerAcceptanceNative
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindowVisible(IntPtr windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PostThreadMessage(uint threadId, uint message, UIntPtr wParam, IntPtr lParam);

}
'@

$results = [ordered]@{
    schemaVersion = 1
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    appPath = $AppPath
    machine = [ordered]@{
        osVersion = [Environment]::OSVersion.VersionString
        osArchitecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        processArchitecture = [Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString()
    }
    checks = [System.Collections.Generic.List[object]]::new()
    snapshots = [System.Collections.Generic.List[object]]::new()
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("AI-Drawer-RuntimeAcceptance-" + [Guid]::NewGuid().ToString('N'))
$appDataRoot = Join-Path $testRoot 'AI Drawer'
$settingsPath = Join-Path $appDataRoot 'settings-v1.json'
$application = $null

function Add-Check {
    param([string]$Name, [bool]$Passed, [string]$Evidence)

    $results.checks.Add([ordered]@{
        name = $Name
        passed = $Passed
        evidence = $Evidence
        observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    })
    if (-not $Passed) {
        throw "$Name failed: $Evidence"
    }
}

function Wait-Condition {
    param(
        [scriptblock]$Condition,
        [TimeSpan]$Timeout,
        [string]$Description
    )

    $deadline = [DateTime]::UtcNow.Add($Timeout)
    while (-not (& $Condition)) {
        if ([DateTime]::UtcNow -ge $deadline) {
            throw "$Description was not ready within $Timeout."
        }

        Start-Sleep -Milliseconds 200
    }
}

function Get-RootElement {
    if ($null -eq $application -or $application.HasExited -or $application.MainWindowHandle -eq [IntPtr]::Zero) {
        throw 'The AI Drawer main window is unavailable.'
    }

    return [System.Windows.Automation.AutomationElement]::FromHandle($application.MainWindowHandle)
}

function Find-ElementByName {
    param([string]$Name)

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::NameProperty,
        $Name)
    return (Get-RootElement).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-ElementByAutomationId {
    param([string]$AutomationId)

    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty,
        $AutomationId)
    return (Get-RootElement).FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}

function Find-AppElementByName {
    param([string]$Name)

    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $application.Id))
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Find-ButtonNames {
    $condition = [System.Windows.Automation.PropertyCondition]::new(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    return @((Get-RootElement).FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition) |
        ForEach-Object { $_.Current.Name })
}

function Invoke-Element {
    param([System.Windows.Automation.AutomationElement]$Element)

    if ($null -eq $Element) {
        throw 'The requested UI Automation element was not found.'
    }

    try {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern)
        ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
    }
    catch [InvalidOperationException] {
        $pattern = $Element.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern)
        ([System.Windows.Automation.ExpandCollapsePattern]$pattern).Expand()
    }
}

function Invoke-ByName {
    param([string]$Name)

    Wait-Condition { $null -ne (Find-ElementByName $Name) } ([TimeSpan]::FromSeconds(20)) "$Name UI action"
    Invoke-Element (Find-ElementByName $Name)
}

function Read-Settings {
    if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Read-Session {
    $sessionPath = Join-Path $appDataRoot 'workspaces-v1.json'
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) {
        return $null
    }

    try {
        return Get-Content -LiteralPath $sessionPath -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }
}

function Wait-SuccessfulOpenCount {
    param([int]$Count, [string]$Description)

    Wait-Condition {
        $settings = Read-Settings
        return $null -ne $settings -and [int]$settings.SuccessfulOpenCount -ge $Count
    } ([TimeSpan]::FromSeconds(90)) $Description
}

function Open-ProviderWorkspace {
    param([string]$ProviderName, [int]$ExpectedOpenCount)

    Invoke-ByName "Open $ProviderName workspace"
    Wait-SuccessfulOpenCount $ExpectedOpenCount "$ProviderName successful provider navigation"
}

function Invoke-ProfileActionForAcceptance {
    param([string]$Action)

    $markerPath = Join-Path $testRoot 'profile-action.acceptance'
    $resultPath = Join-Path $testRoot 'profile-result.acceptance'
    Remove-Item -LiteralPath $resultPath -Force -ErrorAction SilentlyContinue
    Set-Content -LiteralPath $markerPath -Value $Action -Encoding ascii
    $redirect = Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path -Parent $AppPath) -PassThru -WindowStyle Hidden -Environment @{
        AI_DRAWER_TEST_DATA_ROOT = $testRoot
        AI_DRAWER_TEST_PROVIDER_ORIGIN = 'https://example.com/'
    }
    try {
        Wait-Condition { $redirect.Refresh(); $redirect.HasExited } ([TimeSpan]::FromSeconds(20)) "$Action activation redirect"
        Wait-Condition { Test-Path -LiteralPath $resultPath -PathType Leaf } ([TimeSpan]::FromSeconds(60)) "$Action profile result"
        $result = (Get-Content -LiteralPath $resultPath -Raw).Trim()
        if ($result -ne 'passed') {
            throw "$Action profile action returned $result."
        }
    }
    finally {
        if (-not $redirect.HasExited) {
            Stop-Process -Id $redirect.Id -Force
        }
        $redirect.Dispose()
    }
}

function New-Workspace {
    Invoke-ByName 'New workspace'
    Wait-Condition {
        $names = Find-ButtonNames
        return @($names | Where-Object { $_ -like 'New workspace*' }).Count -ge 1
    } ([TimeSpan]::FromSeconds(10)) 'new workspace activation'
}

function Set-ActiveWorkspaceKeepActive {
    Invoke-ByName 'Workspace actions'
    $element = $null
    $deadline = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $element = Find-AppElementByName 'Keep active'
        if ($null -eq $element) {
            $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
            if ($null -ne $focused -and $focused.Current.ProcessId -eq $application.Id) {
                try {
                    [void]$focused.GetCurrentPattern([System.Windows.Automation.TogglePattern]::Pattern)
                    $element = $focused
                }
                catch {
                }
            }
        }

        if ($null -eq $element) {
            Start-Sleep -Milliseconds 200
        }
    } while ($null -eq $element -and [DateTime]::UtcNow -lt $deadline)

    if ($null -eq $element) {
        throw 'Keep active toggle was not available to UI Automation.'
    }

    $pattern = [System.Windows.Automation.TogglePattern]$element.GetCurrentPattern(
        [System.Windows.Automation.TogglePattern]::Pattern)
    if ($pattern.Current.ToggleState -ne [System.Windows.Automation.ToggleState]::On) {
        $pattern.Toggle()
    }
}

function Get-ProcessSnapshot {
    param([string]$Label)

    $allProcesses = @(Get-CimInstance Win32_Process)
    $descendantIds = [System.Collections.Generic.HashSet[int]]::new()
    [void]$descendantIds.Add($application.Id)
    do {
        $changed = $false
        foreach ($process in $allProcesses) {
            if ($descendantIds.Contains([int]$process.ParentProcessId) -and $descendantIds.Add([int]$process.ProcessId)) {
                $changed = $true
            }
        }
    } while ($changed)

    $webProcesses = foreach ($process in $allProcesses) {
        $matchesIsolatedProfile = -not [string]::IsNullOrWhiteSpace($process.CommandLine) -and
            $process.CommandLine.IndexOf($appDataRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0
        if ((-not $descendantIds.Contains([int]$process.ProcessId) -and -not $matchesIsolatedProfile) -or
            -not [string]::Equals($process.Name, 'msedgewebview2.exe', [StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $kind = 'browser'
        if ($process.CommandLine -match '(?:^|\s)--type=([^\s"]+)') {
            $kind = $Matches[1]
        }

        $runtimeProcess = Get-Process -Id $process.ProcessId -ErrorAction SilentlyContinue
        if ($null -eq $runtimeProcess) {
            continue
        }

        [pscustomobject][ordered]@{
            processId = [int]$process.ProcessId
            parentProcessId = [int]$process.ParentProcessId
            kind = $kind
            startTimeUtc = $runtimeProcess.StartTime.ToUniversalTime().ToString('O')
            workingSetBytes = [long]$runtimeProcess.WorkingSet64
            totalProcessorSeconds = [double]$runtimeProcess.TotalProcessorTime.TotalSeconds
        }
    }

    $application.Refresh()
    $totalWebViewWorkingSetBytes = [long]0
    foreach ($webProcess in @($webProcesses)) {
        $totalWebViewWorkingSetBytes += [long]$webProcess.workingSetBytes
    }
    $snapshot = [pscustomobject][ordered]@{
        label = $Label
        observedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
        appProcessId = $application.Id
        appWorkingSetBytes = [long]$application.WorkingSet64
        webViewProcessCount = @($webProcesses).Count
        totalWebViewWorkingSetBytes = $totalWebViewWorkingSetBytes
        processes = @($webProcesses)
    }
    $results.snapshots.Add($snapshot)
    return $snapshot
}

function Wait-NewProcessKind {
    param([string]$Kind, [int[]]$PreviousIds, [string]$Description)

    Wait-Condition {
        $snapshot = Get-ProcessSnapshot "$Description probe"
        return @($snapshot.processes | Where-Object {
            $_.kind -eq $Kind -and $_.processId -notin $PreviousIds
        }).Count -gt 0
    } ([TimeSpan]::FromSeconds(45)) $Description
}

function Stop-ExactWebViewProcess {
    param(
        [object]$ProcessRecord,
        [switch]$EntireProcessTree
    )

    if ($null -eq $ProcessRecord) {
        throw 'No isolated WebView2 process matched the requested failure injection.'
    }

    $current = Get-Process -Id $ProcessRecord.processId -ErrorAction Stop
    if (-not [string]::Equals($current.ProcessName, 'msedgewebview2', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to stop unexpected process $($current.ProcessName) ($($current.Id))."
    }
    if ($current.StartTime.ToUniversalTime().ToString('O') -ne $ProcessRecord.startTimeUtc) {
        throw "Refusing to stop reused WebView2 process ID $($current.Id)."
    }

    if ($EntireProcessTree) {
        $current.Kill($true)
    }
    else {
        $current.Kill()
    }
}

function Stop-IsolatedBrowserProcessTree {
    param(
        [object]$Snapshot,
        [object]$BrowserProcess
    )

    $capturedProcesses = @($Snapshot.processes)
    $capturedIds = @($capturedProcesses | ForEach-Object processId)
    Stop-ExactWebViewProcess $BrowserProcess -EntireProcessTree
    foreach ($capturedProcess in $capturedProcesses) {
        $remainingProcess = Get-Process -Id $capturedProcess.processId -ErrorAction SilentlyContinue
        if ($null -eq $remainingProcess) {
            continue
        }

        Stop-ExactWebViewProcess $capturedProcess
    }

    Wait-Condition {
        return @($capturedIds | Where-Object { $null -ne (Get-Process -Id $_ -ErrorAction SilentlyContinue) }).Count -eq 0
    } ([TimeSpan]::FromSeconds(15)) 'captured browser process tree exit'
}

try {
    New-Item -ItemType Directory -Force -Path $appDataRoot | Out-Null
    [ordered]@{
        SchemaVersion = 1
        OnboardingVersion = 2
        RestoreExactWorkspace = $true
        MemoryMode = 1
        FirstUsedUtc = [DateTimeOffset]::UtcNow.AddDays(-1).ToString('O')
        SuccessfulOpenCount = 0
        SupportReminderDismissed = $true
        DefaultProviderId = $null
        GlobalShortcut = [ordered]@{
            Enabled = $true
            Modifiers = 7
            Key = 'Q'
        }
        LaunchOnStartup = $false
        CloseToTray = $true
        AlwaysOnTop = $false
    } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $settingsPath -Encoding utf8

    $start = [Diagnostics.Stopwatch]::StartNew()
    $application = Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path -Parent $AppPath) -PassThru -Environment @{
        AI_DRAWER_TEST_DATA_ROOT = $testRoot
        AI_DRAWER_TEST_PROVIDER_ORIGIN = 'https://example.com/'
    }
    Wait-Condition {
        $application.Refresh()
        return -not $application.HasExited -and $application.MainWindowHandle -ne [IntPtr]::Zero
    } ([TimeSpan]::FromSeconds(45)) 'cold-start main window'
    $start.Stop()
    Add-Check 'Cold start reaches the native home surface' $true "$([Math]::Round($start.Elapsed.TotalMilliseconds)) ms"

    $second = Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path -Parent $AppPath) -PassThru -WindowStyle Hidden -Environment @{
        AI_DRAWER_TEST_DATA_ROOT = $testRoot
        AI_DRAWER_TEST_PROVIDER_ORIGIN = 'https://example.com/'
    }
    try {
        Wait-Condition { $second.Refresh(); $second.HasExited } ([TimeSpan]::FromSeconds(20)) 'secondary instance redirect'
        Add-Check 'Single-instance redirect exits the secondary process' $true "secondary exit code $($second.ExitCode)"
    }
    finally {
        if (-not $second.HasExited) {
            Stop-Process -Id $second.Id -Force
        }
        $second.Dispose()
    }

    Open-ProviderWorkspace 'Gemini' 1
    New-Workspace
    Open-ProviderWorkspace 'ChatGPT' 2
    if (-not $SkipFiveMinuteWait) {
        Set-ActiveWorkspaceKeepActive
        Wait-Condition {
            $session = Read-Session
            return $null -ne $session -and @($session.Workspaces | Where-Object {
                $_.ProviderId -eq 'chatgpt' -and $_.KeepActive -eq $true
            }).Count -eq 1
        } ([TimeSpan]::FromSeconds(15)) 'Keep active session persistence'
        Add-Check 'Keep active is persisted for the selected ChatGPT workspace' $true 'session metadata contains KeepActive=true'
    }
    New-Workspace
    Open-ProviderWorkspace 'Claude' 3
    New-Workspace
    Open-ProviderWorkspace 'Grok' 4

    Wait-Condition {
        return @(Find-ButtonNames | Where-Object { $_ -like 'Gemini*reload*' }).Count -eq 1
    } ([TimeSpan]::FromSeconds(30)) 'hard live-limit disposal'
    $burstSnapshot = Get-ProcessSnapshot 'four-workspace burst after hard-limit enforcement'
    Add-Check 'Fourth workspace opens after bounded hard-limit disposal' $true "WebView2 processes: $($burstSnapshot.webViewProcessCount)"

    if (-not $SkipFiveMinuteWait) {
        $graceWait = [TimeSpan]::FromMinutes($GracePeriodMinutes).Add([TimeSpan]::FromSeconds(45))
        try {
            Wait-Condition {
                $names = Find-ButtonNames
                return @($names | Where-Object { $_ -like '*reload*' }).Count -ge 2
            } $graceWait 'steady live-limit disposal after the five-minute protection period'
        }
        catch {
            $visibleNames = (Find-ButtonNames | Where-Object { $_ -match 'Gemini|ChatGPT|Claude|Grok|reload|recent' }) -join '; '
            throw "$($_.Exception.Message) Visible workspace actions: $visibleNames"
        }
        $steadyTabNames = Find-ButtonNames
        Add-Check -Name 'Keep active workspace survives steady-state disposal' `
            -Passed (@($steadyTabNames | Where-Object { $_ -like 'ChatGPT*reload*' }).Count -eq 0) `
            -Evidence (($steadyTabNames | Where-Object { $_ -match 'Gemini|ChatGPT|Claude|Grok|reload|recent' }) -join '; ')
        Add-Check -Name 'A second ordinary inactive workspace is released at steady state' `
            -Passed (@($steadyTabNames | Where-Object { $_ -like 'Claude*reload*' }).Count -eq 1) `
            -Evidence (($steadyTabNames | Where-Object { $_ -match 'Gemini|ChatGPT|Claude|Grok|reload|recent' }) -join '; ')
        $steadySnapshot = Get-ProcessSnapshot 'steady state after grace period'
        Add-Check 'Steady limit preserves Keep active and releases another inactive workspace' $true "WebView2 processes: $($steadySnapshot.webViewProcessCount)"
    }

    $geminiReload = Find-ButtonNames | Where-Object { $_ -like 'Gemini*reload*' } | Select-Object -First 1
    Invoke-ByName $geminiReload
    Wait-SuccessfulOpenCount 5 'disposed Gemini workspace recovery'
    Add-Check 'Disposed workspace recreates with the same isolated profile' $true 'Gemini returned to a successful navigation'

    Invoke-ProfileActionForAcceptance 'clear-cache'
    Add-Check 'Clear cache completes through the WebView2 profile API' $true 'isolated Debug profile action succeeded'

    Invoke-ProfileActionForAcceptance 'reset-provider'
    Add-Check 'Selected-provider reset completes without affecting other native workspaces' $true 'isolated Debug profile action succeeded'

    Invoke-ProfileActionForAcceptance 'reset-all'
    Add-Check 'Reset all reports complete profile cleanup' $true 'all known isolated Debug profiles succeeded'

    $postResetOpenCount = [int](Read-Settings).SuccessfulOpenCount
    $geminiAfterReset = Find-ButtonNames | Where-Object { $_ -like 'Gemini*reload*' } | Select-Object -First 1
    Invoke-ByName $geminiAfterReset
    Wait-SuccessfulOpenCount ($postResetOpenCount + 1) 'active workspace recreation after profile reset'

    $beforeRenderer = Get-ProcessSnapshot 'before renderer failure injection'
    $renderer = $beforeRenderer.processes |
        Where-Object { $_.kind -eq 'renderer' } |
        Sort-Object workingSetBytes -Descending |
        Select-Object -First 1
    $rendererIds = @($beforeRenderer.processes | Where-Object { $_.kind -eq 'renderer' } | ForEach-Object processId)
    Stop-ExactWebViewProcess $renderer
    Wait-NewProcessKind 'renderer' $rendererIds 'renderer recovery'
    Add-Check 'Renderer exit is contained and a renderer is recreated' (-not $application.HasExited) "stopped renderer PID $($renderer.processId)"

    $beforeGpu = Get-ProcessSnapshot 'before GPU failure injection'
    $gpu = $beforeGpu.processes | Where-Object { $_.kind -eq 'gpu-process' } | Select-Object -First 1
    $gpuIds = @($beforeGpu.processes | Where-Object { $_.kind -eq 'gpu-process' } | ForEach-Object processId)
    Stop-ExactWebViewProcess $gpu
    Wait-NewProcessKind 'gpu-process' $gpuIds 'GPU helper recovery'
    Add-Check 'GPU helper exit is contained and the helper is recreated' (-not $application.HasExited) "stopped GPU PID $($gpu.processId)"

    $beforeBrowser = Get-ProcessSnapshot 'before browser failure injection'
    $beforeBrowserOpenCount = [int](Read-Settings).SuccessfulOpenCount
    $browser = $beforeBrowser.processes | Where-Object { $_.kind -eq 'browser' } | Select-Object -First 1
    $browserIds = @($beforeBrowser.processes | Where-Object { $_.kind -eq 'browser' } | ForEach-Object processId)
    Stop-IsolatedBrowserProcessTree $beforeBrowser $browser
    try {
        Wait-NewProcessKind 'browser' $browserIds 'browser process recovery'
    }
    catch {
        $visibleNames = (Find-ButtonNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) -join '; '
        $statusTitle = (Find-ElementByAutomationId 'StatusTitle')?.Current.Name
        $statusMessage = (Find-ElementByAutomationId 'StatusMessage')?.Current.Name
        $isolatedProcesses = @(Get-CimInstance Win32_Process | Where-Object {
            [string]::Equals($_.Name, 'msedgewebview2.exe', [StringComparison]::OrdinalIgnoreCase) -and
            ((-not [string]::IsNullOrWhiteSpace($_.CommandLine) -and
                $_.CommandLine.IndexOf($appDataRoot, [StringComparison]::OrdinalIgnoreCase) -ge 0) -or
                [int]$_.ParentProcessId -eq $application.Id)
        } | ForEach-Object { "$($_.ProcessId):$($_.ParentProcessId)" }) -join ', '
        throw "$($_.Exception.Message) Status: $statusTitle — $statusMessage. Isolated processes: $isolatedProcesses. Buttons: $visibleNames"
    }
    Wait-SuccessfulOpenCount ($beforeBrowserOpenCount + 1) 'active workspace navigation after browser-process recovery'
    Add-Check 'Browser-process exit recreates the isolated WebView2 environment' (-not $application.HasExited) "stopped browser PID $($browser.processId)"
    $windowHandle = $application.MainWindowHandle
    $nativeProcessId = [uint32]0
    $threadId = [AIDrawerAcceptanceNative]::GetWindowThreadProcessId($windowHandle, [ref]$nativeProcessId)
    [void]$application.CloseMainWindow()
    Wait-Condition {
        return -not $application.HasExited -and -not [AIDrawerAcceptanceNative]::IsWindowVisible($windowHandle)
    } ([TimeSpan]::FromSeconds(10)) 'close-to-tray behavior'
    Add-Check 'Close-to-tray keeps the process alive and hides the window' $true "PID $($application.Id)"

    $posted = [AIDrawerAcceptanceNative]::PostThreadMessage($threadId, 0x0312, [UIntPtr]0xA1D0, [IntPtr]::Zero)
    Add-Check 'Global-shortcut message reaches the registered application thread' $posted "thread $threadId"
    Wait-Condition { [AIDrawerAcceptanceNative]::IsWindowVisible($windowHandle) } ([TimeSpan]::FromSeconds(10)) 'global shortcut restore'
    Add-Check 'Global shortcut restores the tray-hidden native window' $true 'window became visible'

    [void]$application.CloseMainWindow()
    Wait-Condition {
        return -not $application.HasExited -and -not [AIDrawerAcceptanceNative]::IsWindowVisible($windowHandle)
    } ([TimeSpan]::FromSeconds(10)) 'second close-to-tray behavior'

    $reactivate = Start-Process -FilePath $AppPath -WorkingDirectory (Split-Path -Parent $AppPath) -PassThru -WindowStyle Hidden -Environment @{
        AI_DRAWER_TEST_DATA_ROOT = $testRoot
        AI_DRAWER_TEST_PROVIDER_ORIGIN = 'https://example.com/'
    }
    try {
        Wait-Condition { $reactivate.Refresh(); $reactivate.HasExited } ([TimeSpan]::FromSeconds(20)) 'tray reactivation redirect'
        Wait-Condition { [AIDrawerAcceptanceNative]::IsWindowVisible($windowHandle) } ([TimeSpan]::FromSeconds(10)) 'tray window restore'
    }
    finally {
        if (-not $reactivate.HasExited) {
            Stop-Process -Id $reactivate.Id -Force
        }
        $reactivate.Dispose()
    }
    Add-Check 'A second launch restores the tray-hidden primary window' $true 'primary window visible'

    Invoke-ByName 'Workspace actions'
    Wait-Condition { $null -ne (Find-ElementByAutomationId 'ExitApplicationButton') } ([TimeSpan]::FromSeconds(15)) 'exit menu item'
    Invoke-Element (Find-ElementByAutomationId 'ExitApplicationButton')
    Wait-Condition { $application.Refresh(); $application.HasExited } ([TimeSpan]::FromSeconds(30)) 'clean application exit'
    Add-Check 'Exit releases the native process, tray icon, shortcut, and WebView2 tree' $true "exit code $($application.ExitCode)"

    Start-Sleep -Seconds 3
    $remaining = @(Get-CimInstance Win32_Process | Where-Object {
        $_.CommandLine -like "*$testRoot*"
    })
    Add-Check 'No process retains the isolated test profile after exit' ($remaining.Count -eq 0) "remaining process count $($remaining.Count)"
}
catch {
    $results.failure = $_.Exception.ToString()
    throw
}
finally {
    if ($null -ne $application) {
        try {
            $application.Refresh()
            if (-not $application.HasExited) {
                Stop-Process -Id $application.Id -Force
                $application.WaitForExit()
            }
        }
        catch {
        }
        $application.Dispose()
    }

    $results.completedAtUtc = [DateTimeOffset]::UtcNow.ToString('O')
    $results | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8

    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd('\') + '\'
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    if ($resolvedTestRoot.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $resolvedTestRoot) -like 'AI-Drawer-RuntimeAcceptance-*') {
        for ($attempt = 1; $attempt -le 20; $attempt++) {
            try {
                if (Test-Path -LiteralPath $resolvedTestRoot) {
                    Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
                }
                break
            }
            catch {
                if ($attempt -eq 20) {
                    $results.tempProfileCleanupFailure = $_.Exception.Message
                    $results | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $OutputPath -Encoding utf8
                    break
                }
                Start-Sleep -Milliseconds 500
            }
        }
    }
}

Write-Output "Runtime acceptance report: $OutputPath"
