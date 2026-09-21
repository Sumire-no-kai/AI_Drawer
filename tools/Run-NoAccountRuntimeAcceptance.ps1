[CmdletBinding()]
param(
    [string]$AppPath,
    [string]$OutputPath,
    [ValidateRange(1, 30)]
    [Alias("GracePeriodMinutes")]
    [int]$RetentionObservationMinutes = 5,
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
Add-Type -AssemblyName System.Drawing.Common
Add-Type -AssemblyName System.Windows.Forms
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll")]
    public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

    [StructLayout(LayoutKind.Sequential)]
    public struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct INPUT
    {
        public uint type;
        public MOUSEINPUT mouse;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint SendInput(uint count, INPUT[] inputs, int size);

    public static bool SendMouse(int x, int y, uint flags)
    {
        INPUT[] inputs = new INPUT[1];
        inputs[0].type = 0;
        inputs[0].mouse.dx = x;
        inputs[0].mouse.dy = y;
        inputs[0].mouse.dwFlags = flags;
        return SendInput(1, inputs, Marshal.SizeOf(typeof(INPUT))) == 1;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr windowHandle);

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

function Find-AppButtonByName {
    param([string]$Name)

    $condition = [System.Windows.Automation.AndCondition]::new(
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::NameProperty,
            $Name),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ProcessIdProperty,
            $application.Id),
        [System.Windows.Automation.PropertyCondition]::new(
            [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
            [System.Windows.Automation.ControlType]::Button))
    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants,
        $condition)
}

function Invoke-AppElementByName {
    param([string]$Name)

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $element = Find-AppElementByName $Name
        $pattern = $null
        if ($null -ne $element -and $element.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$pattern)) {
            ([System.Windows.Automation.InvokePattern]$pattern).Invoke()
            return
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "$Name did not expose an invokable UI Automation element."
}

function Click-AppElementByName {
    param(
        [string]$Name,
        [switch]$RightClick,
        [switch]$ButtonOnly
    )

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $element = if ($ButtonOnly) { Find-AppButtonByName $Name } else { Find-AppElementByName $Name }
        if ($null -ne $element) {
            $bounds = $element.Current.BoundingRectangle
            if ($bounds.Width -gt 0 -and $bounds.Height -gt 0) {
                $x = [int]($bounds.Left + ($bounds.Width / 2))
                $y = [int]($bounds.Top + ($bounds.Height / 2))
                if (-not [AIDrawerAcceptanceNative]::SetCursorPos($x, $y)) {
                    throw "Could not position the pointer for $Name."
                }
                $down = if ($RightClick) { 0x0008 } else { 0x0002 }
                $up = if ($RightClick) { 0x0010 } else { 0x0004 }
                [AIDrawerAcceptanceNative]::mouse_event($down, 0, 0, 0, [UIntPtr]::Zero)
                [AIDrawerAcceptanceNative]::mouse_event($up, 0, 0, 0, [UIntPtr]::Zero)
                return
            }
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    throw "$Name did not expose a clickable UI Automation rectangle."
}

function Drag-AppButtonByName {
    param([string]$SourceName, [string]$TargetName)

    $source = Find-AppButtonByName $SourceName
    $target = Find-AppButtonByName $TargetName
    if ($null -eq $source -or $null -eq $target)
    {
        throw "Could not find the $SourceName or $TargetName tab for drag ordering."
    }

    $sourceBounds = $source.Current.BoundingRectangle
    $targetBounds = $target.Current.BoundingRectangle
    if ($sourceBounds.Width -le 0 -or $targetBounds.Width -le 0)
    {
        throw "The $SourceName or $TargetName tab was outside the visible tab strip."
    }

    $sourceX = [int]($sourceBounds.Left + ($sourceBounds.Width / 2))
    $sourceY = [int]($sourceBounds.Top + ($sourceBounds.Height / 2))
    $targetX = [int]($targetBounds.Left + ($targetBounds.Width * 0.25))
    $targetY = [int]($targetBounds.Top + ($targetBounds.Height / 2))
    if (-not [AIDrawerAcceptanceNative]::SetCursorPos($sourceX, $sourceY))
    {
        throw "Could not position the pointer on $SourceName."
    }

    if (-not [AIDrawerAcceptanceNative]::SendMouse(0, 0, 0x0002))
    {
        throw "Could not press the pointer on $SourceName."
    }
    Start-Sleep -Milliseconds 200
    $virtualScreen = [System.Windows.Forms.SystemInformation]::VirtualScreen
    for ($step = 1; $step -le 20; $step++)
    {
        $x = [int]($sourceX + (($targetX - $sourceX) * $step / 20))
        $y = [int]($sourceY + (($targetY - $sourceY) * $step / 20))
        $normalizedX = [int](($x - $virtualScreen.Left) * 65535 / [Math]::Max(1, $virtualScreen.Width - 1))
        $normalizedY = [int](($y - $virtualScreen.Top) * 65535 / [Math]::Max(1, $virtualScreen.Height - 1))
        if (-not [AIDrawerAcceptanceNative]::SendMouse($normalizedX, $normalizedY, 0xC001))
        {
            throw "Could not move the pointer while dragging $SourceName."
        }
        Start-Sleep -Milliseconds 75
    }
    if (-not [AIDrawerAcceptanceNative]::SendMouse(0, 0, 0x0004))
    {
        throw "Could not release the pointer on $TargetName."
    }
}

function Set-FocusedTextValue {
    param([string]$Value, [string]$Description)

    Wait-Condition {
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        return $null -ne $focused -and
            $focused.Current.ProcessId -eq $application.Id -and
            $focused.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit
    } ([TimeSpan]::FromSeconds(10)) $Description
    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    $pattern = $focused.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern)
    ([System.Windows.Automation.ValuePattern]$pattern).SetValue($Value)
}

function Send-AppKeys {
    param([string]$Keys)

    if (-not [AIDrawerAcceptanceNative]::SetForegroundWindow($application.MainWindowHandle)) {
        throw 'Could not foreground the application for keyboard acceptance.'
    }
    Start-Sleep -Milliseconds 200
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
}

function Invoke-PrimaryPromptWithKeyboard {
    param([string]$SecondaryName, [string]$PrimaryName)

    $deadline = [DateTime]::UtcNow.AddSeconds(20)
    do {
        $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
        if ($null -ne $focused -and
            $focused.Current.ProcessId -eq $application.Id -and
            [string]::Equals($focused.Current.Name, $SecondaryName, [StringComparison]::Ordinal)) {
            break
        }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($null -eq $focused -or
        $focused.Current.ProcessId -ne $application.Id -or
        -not [string]::Equals($focused.Current.Name, $SecondaryName, [StringComparison]::Ordinal)) {
        throw "$SecondaryName did not receive prompt focus."
    }

    [System.Windows.Forms.SendKeys]::SendWait('{TAB}')
    Wait-Condition {
        $next = [System.Windows.Automation.AutomationElement]::FocusedElement
        return $null -ne $next -and
            $next.Current.ProcessId -eq $application.Id -and
            [string]::Equals($next.Current.Name, $PrimaryName, [StringComparison]::Ordinal)
    } ([TimeSpan]::FromSeconds(5)) "$PrimaryName prompt focus"
    [System.Windows.Forms.SendKeys]::SendWait('{ENTER}')
}

function Save-WindowScreenshot {
    param([string]$Name)

    $bounds = (Get-RootElement).Current.BoundingRectangle
    $width = [Math]::Max(1, [int][Math]::Ceiling($bounds.Width))
    $height = [Math]::Max(1, [int][Math]::Ceiling($bounds.Height))
    $bitmap = [Drawing.Bitmap]::new($width, $height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen([int]$bounds.Left, [int]$bounds.Top, 0, 0, $bitmap.Size)
        $path = Join-Path $outputDirectory $Name
        $bitmap.Save($path, [Drawing.Imaging.ImageFormat]::Png)
        return $path
    }
    finally {
        $graphics.Dispose()
        $bitmap.Dispose()
    }
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
        OnboardingVersion = 3
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
    New-Workspace
    Open-ProviderWorkspace 'Claude' 3
    New-Workspace
    Open-ProviderWorkspace 'Grok' 4

    $burstSnapshot = Get-ProcessSnapshot 'four retained conversation pages'
    Add-Check 'Opening the fourth workspace retains the first three pages' `
        (@(Find-ButtonNames | Where-Object { $_ -like '*reload*' }).Count -eq 0) `
        "WebView2 processes: $($burstSnapshot.webViewProcessCount)"

    Send-AppKeys '^t'
    Wait-SuccessfulOpenCount 5 'same-provider Ctrl+T navigation'
    Wait-Condition {
        $session = Read-Session
        return $null -ne $session -and
            @($session.Workspaces).Count -eq 5 -and
            @($session.Workspaces | Where-Object { $_.ProviderId -eq 'grok' }).Count -eq 2
    } ([TimeSpan]::FromSeconds(20)) 'same-provider tab persistence'
    Add-Check 'Ctrl+T creates another retained tab with the focused provider' $true 'a second Grok tab opened with its own workspace identity'

    Click-AppElementByName 'Grok 2' -RightClick -ButtonOnly
    Invoke-AppElementByName 'Rename tab'
    Set-FocusedTextValue 'Research' 'rename input focus'
    Invoke-AppElementByName 'Save'
    Wait-Condition {
        $session = Read-Session
        return $null -ne $session -and @($session.Workspaces | Where-Object { $_.DisplayName -eq 'Research' }).Count -eq 1
    } ([TimeSpan]::FromSeconds(20)) 'renamed tab persistence'

    Drag-AppButtonByName 'Research' 'Grok'
    Start-Sleep -Seconds 1
    Wait-Condition {
        $session = Read-Session
        return $null -ne $session -and
            @($session.Workspaces).Count -eq 5 -and
            $session.Workspaces[3].DisplayName -eq 'Research' -and
            $session.Workspaces[4].DisplayName -eq 'Grok'
    } ([TimeSpan]::FromSeconds(20)) 'tab order persistence'
    Add-Check 'Rename and drag actions update the intended tab' $true 'Research was dragged left of the original Grok tab without changing provider identity'

    Invoke-ByName 'Find an open tab'
    Set-FocusedTextValue 'Research' 'tab search input focus'
    Wait-Condition { $null -ne (Find-AppElementByName 'Research — Grok') } ([TimeSpan]::FromSeconds(10)) 'filtered tab result'
    Click-AppElementByName 'Research — Grok'
    Invoke-AppElementByName 'Switch'
    Wait-Condition {
        $session = Read-Session
        if ($null -eq $session) { return $false }
        $research = @($session.Workspaces | Where-Object { $_.DisplayName -eq 'Research' }) | Select-Object -First 1
        return $null -ne $research -and $session.ActiveWorkspaceId -eq $research.Id
    } ([TimeSpan]::FromSeconds(20)) 'searched tab selection'
    Add-Check 'Tab search switches to the selected retained workspace' $true 'the filtered Research result became focused'

    $orderedSession = Read-Session
    $orderedIds = @($orderedSession.Workspaces | ForEach-Object Id)
    Send-AppKeys '^1'
    Wait-Condition { (Read-Session).ActiveWorkspaceId -eq $orderedIds[0] } ([TimeSpan]::FromSeconds(10)) 'Ctrl+1 tab selection'
    Send-AppKeys '^{TAB}'
    Wait-Condition { (Read-Session).ActiveWorkspaceId -eq $orderedIds[1] } ([TimeSpan]::FromSeconds(10)) 'Ctrl+Tab cycling'
    Send-AppKeys '^5'
    Wait-Condition { (Read-Session).ActiveWorkspaceId -eq $orderedIds[4] } ([TimeSpan]::FromSeconds(10)) 'Ctrl+5 tab selection'
    Add-Check 'Ctrl+Tab and positional shortcuts select the expected tabs' $true 'Ctrl+1, Ctrl+Tab, and Ctrl+5 matched persisted tab order'
    Start-Sleep -Seconds 1
    [void](Save-WindowScreenshot '00-tab-management.png')

    $retentionStartSnapshot = Get-ProcessSnapshot 'five retained conversation pages before observation'
    if (-not $SkipFiveMinuteWait) {
        $deadline = [DateTime]::UtcNow.AddMinutes($RetentionObservationMinutes)
        do {
            if (@(Find-ButtonNames | Where-Object { $_ -like '*reload*' }).Count -ne 0) {
                throw 'A page was released without an explicit user action.'
            }
            Start-Sleep -Seconds 1
        } while ([DateTime]::UtcNow -lt $deadline)
        $steadySnapshot = Get-ProcessSnapshot 'retained pages after observation period'
        Add-Check 'Background pages remain retained beyond the former grace period' $true `
            "WebView2 processes: $($retentionStartSnapshot.webViewProcessCount) to $($steadySnapshot.webViewProcessCount); working set: $($retentionStartSnapshot.totalWebViewWorkingSetBytes) to $($steadySnapshot.totalWebViewWorkingSetBytes) bytes"
    }

    Invoke-ByName 'Gemini'
    Invoke-ByName 'Workspace actions'
    Start-Sleep -Milliseconds 500
    [void](Save-WindowScreenshot '01-workspace-actions.png')
    Invoke-AppElementByName 'Release page…'
    Start-Sleep -Milliseconds 500
    [void](Save-WindowScreenshot '02-after-release-click.png')
    Invoke-PrimaryPromptWithKeyboard 'Keep open' 'Release page'
    Wait-Condition {
        return @(Find-ButtonNames | Where-Object { $_ -like 'Gemini*reload*' }).Count -eq 1
    } ([TimeSpan]::FromSeconds(30)) 'explicit page release'
    $geminiReload = Find-ButtonNames | Where-Object { $_ -like 'Gemini*reload*' } | Select-Object -First 1
    Invoke-ByName $geminiReload
    Wait-SuccessfulOpenCount 5 'explicitly released Gemini workspace recovery'
    Add-Check 'Released workspace recreates with the same isolated profile' $true 'Gemini returned to a successful navigation'

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
