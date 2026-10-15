<#
.SYNOPSIS
  Runs the M7.3 UI check (tools/UiCheck) unattended on Windows and writes a short verdict.

.DESCRIPTION
  1. Finds the Godot 4.7.2 .NET console executable (-GodotPath, else $env:GODOT, else a search of the usual folders),
     and accepts it only if it reports version 4.7.2.stable.mono.
  2. Runs: dotnet run --project tools\UiCheck -- --godot <godot> --out <run>\detail
     with a time limit, keeping the PC awake, and records the start and end time, command, exit code and the
     complete console output.
  3. Opens the project in the real Godot editor (--editor --quit) and records its output: the editor loads and
     imports the project. Its Build and Play buttons are not pressed (see docs/client.md).
  4. Stops every process this run started that is still running: children of the processes it launched, or
     processes started after this script began whose command line names this run's folder. Never other Godot
     windows.
  5. Writes <run>\summary.md: PASS / FAIL / NOT TESTED for every check, keeping the runner's own detailed
     summary, logs and screenshots in <run>\detail.

  It never asks a question. Run it from a logged-in desktop session (the Godot windows need a desktop); leave the
  PC unlocked while it runs.

  Exit codes: 0 every automated check passed (manual checks still listed as NOT TESTED); 1 a check failed;
  2 the run did not finish (time limit, or the runner stopped early); 3 it could not start (no Godot 4.7.2 .NET
  console executable, no .NET 10 SDK, or no desktop session).

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\UiCheck\Run-WindowsUiCheck.ps1
.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File tools\UiCheck\Run-WindowsUiCheck.ps1 -GodotPath "C:\Godot\Godot_v4.7.2-stable_mono_win64_console.exe"
#>
[CmdletBinding()]
param(
    # The Godot 4.7.2 .NET console executable. When left out: $env:GODOT, then a search (see Find-Godot).
    [string]$GodotPath,
    # Time limit for the whole UI check runner (builds included).
    [double]$TimeoutMinutes = 30,
    # Time limit for opening the project in the Godot editor.
    [int]$EditorTimeoutSeconds = 300,
    # Where to put this run's folder. Default: <repository>\ui-check-results.
    [string]$ResultsRoot,
    # Extra arguments for the runner. Only for testing this script elsewhere (for example '--opengl' on Linux).
    [string[]]$RunnerArgs = @()
)

Set-StrictMode -Version 2
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$onWindows = $env:OS -eq 'Windows_NT'
$started = Get-Date
$repo = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
if (-not (Test-Path (Join-Path $repo 'TrenchSim.sln'))) {
    Write-Error "TrenchSim.sln not found above $PSScriptRoot; run the script from inside the repository."
    exit 3
}
if (-not $ResultsRoot) { $ResultsRoot = Join-Path $repo 'ui-check-results' }
$run = Join-Path $ResultsRoot ('windows-' + $started.ToString('yyyyMMdd-HHmmss'))
$detail = Join-Path $run 'detail'
New-Item -ItemType Directory -Force -Path $run | Out-Null
$runLog = Join-Path $run 'run.log'
$facts = New-Object System.Collections.Generic.List[string]
$own = New-Object System.Collections.Generic.List[object]   # rows this script adds: Result, Id, Detail
$roots = New-Object System.Collections.Generic.List[int]    # processes this script started itself

function Log([string]$text) {
    $line = '{0:HH:mm:ss} {1}' -f (Get-Date), $text
    Write-Host $line
    Add-Content -Path $runLog -Value $line -Encoding UTF8
}

function Fact([string]$name, [string]$value) {
    $facts.Add("| $name | $($value -replace '\|', '\|') |")
    Log "$name`: $value"
}

function Row([string]$result, [string]$id, [string]$text) {
    $own.Add([pscustomobject]@{ Result = $result; Id = $id; Detail = $text })
    Log "$result $id`: $text"
}

# Runs a program with a time limit; stdout and stderr go to files. Returns the exit code, or $null on time-out
# (the process and its children are then stopped).
function Invoke-Limited([string]$file, [string[]]$arguments, [string]$stdout, [string]$stderr, [int]$seconds, [switch]$Echo) {
    $quoted = ($arguments | ForEach-Object { if ($_ -match '[\s"]') { '"' + ($_ -replace '"', '\"') + '"' } else { $_ } }) -join ' '
    $process = Start-Process -FilePath $file -ArgumentList $quoted -RedirectStandardOutput $stdout -RedirectStandardError $stderr -NoNewWindow -PassThru
    $null = $process.Handle   # keeps ExitCode readable after exit (Windows PowerShell 5.1)
    $roots.Add($process.Id)
    $deadline = (Get-Date).AddSeconds($seconds)
    $shown = 0
    while (-not $process.WaitForExit(2000)) {
        if ($Echo) { $shown = Show-New $stdout $shown }
        if ((Get-Date) -gt $deadline) {
            Log "Time limit of $seconds s reached for $file; stopping it and everything it started."
            $null = Stop-OwnProcesses 'time limit'
            return $null
        }
    }
    $process.WaitForExit()
    if ($Echo) { $null = Show-New $stdout $shown }
    return $process.ExitCode
}

function Show-New([string]$path, [int]$shown) {
    if (-not (Test-Path $path)) { return $shown }
    $lines = @(Get-Content -Path $path -Encoding UTF8 -ErrorAction SilentlyContinue)
    for ($i = $shown; $i -lt $lines.Count; $i++) { Write-Host $lines[$i] }
    return $lines.Count
}

# Processes this run started that are still running: the ones this script started and all their descendants,
# plus any process started after this script began whose command line names this run's own folder (in case its
# parent is already gone). Every one was started after this script began; nothing else is ever included.
function Get-OwnProcesses {
    $all = @()
    if ($onWindows) {
        foreach ($p in Get-CimInstance Win32_Process) {
            if (-not $p.CreationDate) { continue }
            $all += [pscustomobject]@{ Id = [int]$p.ProcessId; Parent = [int]$p.ParentProcessId; Start = $p.CreationDate; Name = $p.Name; CommandLine = [string]$p.CommandLine }
        }
    }
    else {
        foreach ($p in Get-Process) {
            try { $all += [pscustomobject]@{ Id = $p.Id; Parent = $(if ($p.Parent) { $p.Parent.Id } else { 0 }); Start = $p.StartTime; Name = $p.ProcessName; CommandLine = [string]$p.CommandLine } } catch { }
        }
    }
    $all = @($all | Where-Object { $_.Id -ne $PID -and $_.Start -ge $started })
    $ours = @{}
    foreach ($id in $roots) { $ours[$id] = $true }
    do {
        $added = $false
        foreach ($p in $all) {
            if (-not $ours.ContainsKey($p.Id) -and $ours.ContainsKey($p.Parent)) { $ours[$p.Id] = $true; $added = $true }
        }
    } while ($added)
    return @($all | Where-Object { $ours.ContainsKey($_.Id) -or ($_.CommandLine -and $_.CommandLine.IndexOf($run, [StringComparison]::OrdinalIgnoreCase) -ge 0) })
}

function Stop-OwnProcesses([string]$when) {
    $left = @(Get-OwnProcesses)
    if ($left.Count -eq 0) {
        Log "Cleanup ($when): no processes from this run were left running."
        return 0
    }
    foreach ($p in $left) {
        Log "Cleanup ($when): stopping $($p.Name) (PID $($p.Id)): $($p.CommandLine)"
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
    return $left.Count
}

# Godot version line, or $null when the file is not a Godot that answers --version in time.
function Get-GodotVersion([string]$path) {
    $out = Join-Path $run 'godot-version-probe.txt'
    $err = Join-Path $run 'godot-version-probe-err.txt'
    $code = Invoke-Limited $path @('--version') $out $err 60
    $text = ((Get-Content $out -ErrorAction SilentlyContinue) -join ' ').Trim()
    Remove-Item $out, $err -ErrorAction SilentlyContinue
    if ($code -ne 0) { return $null }
    return $text
}

# The Godot 4.7.2 .NET console executable: the given path, $env:GODOT, or the only build found in the usual folders.
# Every candidate must report 4.7.2.stable.mono; nothing is assumed from a file name alone.
function Find-Godot {
    $name = 'Godot_v4.7.2-stable_mono_win64_console.exe'
    if ($GodotPath) { return @{ Path = $GodotPath; How = 'given with -GodotPath' } }
    if ($env:GODOT) { return @{ Path = $env:GODOT; How = 'from the GODOT environment variable' } }
    if (-not $onWindows) { return $null }
    $roots = @(
        (Join-Path $env:USERPROFILE 'Downloads'), (Join-Path $env:USERPROFILE 'Desktop'), (Join-Path $env:USERPROFILE 'Documents'),
        $env:LOCALAPPDATA, ${env:ProgramFiles}, ${env:ProgramFiles(x86)}, 'C:\Godot', 'C:\Tools'
    ) | Where-Object { $_ -and (Test-Path $_) }
    $found = @()
    foreach ($root in $roots) {
        $found += @(Get-ChildItem -Path $root -Filter $name -File -Recurse -Depth 4 -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
    }
    $found = @($found | Sort-Object -Unique)
    Log ("Search for $name in $($roots -join '; '): " + $(if ($found.Count -eq 0) { 'none found' } else { $found -join '; ' }))
    $verified = @()
    foreach ($candidate in $found) {
        $version = Get-GodotVersion $candidate
        Log "  $candidate reports: $version"
        if ($version -match '^4\.7\.2\.stable\.mono\.') { $verified += [pscustomobject]@{ Path = $candidate; Version = $version; Time = (Get-Item $candidate).LastWriteTime } }
    }
    if ($verified.Count -eq 0) { return $null }
    $builds = @($verified | ForEach-Object { $_.Version } | Sort-Object -Unique)
    if ($builds.Count -gt 1) {
        Log "Several different Godot 4.7.2 .NET builds were found ($($builds -join ', ')); not choosing one. Give -GodotPath."
        return $null
    }
    $chosen = $verified | Sort-Object Time -Descending | Select-Object -First 1
    return @{ Path = $chosen.Path; How = "found by search ($($verified.Count) copy/copies of the same build $($builds[0]))" }
}

# ---- keep the PC awake while this runs (Windows) ----
$awake = $false
if ($onWindows) {
    try {
        Add-Type -Namespace TrenchSim -Name Power -MemberDefinition '[DllImport("kernel32.dll")] public static extern uint SetThreadExecutionState(uint flags);'
        # ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED: no sleep and no display time-out while the run lasts.
        $null = [TrenchSim.Power]::SetThreadExecutionState([uint32]2147483651)
        $awake = $true
    }
    catch { Log "Could not keep the PC awake: $($_.Exception.Message)" }
}

$runnerExit = $null
$editorExit = $null
$finished = $false
try {
    Log "M7.3 Windows UI check. Results in $run"
    Fact 'Started' $started.ToString('yyyy-MM-dd HH:mm:ss zzz')
    Fact 'Computer' ("{0}; {1}; PowerShell {2}" -f [Environment]::MachineName, [Environment]::OSVersion.VersionString, $PSVersionTable.PSVersion)
    Fact 'Repository folder' $repo
    $gitHead = $null
    if (Test-Path (Join-Path $repo '.git')) { try { $gitHead = (& git -C $repo rev-parse HEAD 2>$null) } catch { $gitHead = $null } }
    Fact 'Commit' $(if ($gitHead) { "$gitHead (git)" } else { "no .git folder (ZIP download); folder name: $(Split-Path $repo -Leaf)" })

    # ---- preflight ----
    try { $dotnet = (& dotnet --version 2>$null) } catch { $dotnet = $null }
    Fact '.NET SDK' $(if ($dotnet) { $dotnet } else { 'not found' })
    if (-not $dotnet -or -not ($dotnet -match '^10\.')) {
        Row 'FAIL' 'preflight-dotnet' "The .NET 10 SDK is needed; dotnet --version gave '$dotnet'."
        return
    }
    if ($onWindows) {
        $session = (Get-Process -Id $PID).SessionId
        Fact 'Desktop session' "session $session, interactive: $([Environment]::UserInteractive), SESSIONNAME=$env:SESSIONNAME"
        if ($session -eq 0 -or -not [Environment]::UserInteractive) {
            Row 'FAIL' 'preflight-desktop' 'No interactive desktop (for example run as a service or a scheduled task while logged off): Godot windows cannot open. Run it from your logged-in desktop.'
            return
        }
        $locked = @(Get-Process -Name LogonUI -ErrorAction SilentlyContinue).Count -gt 0
        Fact 'Screen locked at start' $(if ($locked) { 'yes (results may be unreliable)' } else { 'no' })
    }
    $godot = Find-Godot
    if (-not $godot) {
        Row 'FAIL' 'preflight-godot' 'No Godot 4.7.2 .NET console executable found or given. Pass -GodotPath "<...>\Godot_v4.7.2-stable_mono_win64_console.exe" or set $env:GODOT.'
        return
    }
    if (-not (Test-Path $godot.Path)) {
        Row 'FAIL' 'preflight-godot' "The Godot path does not exist: $($godot.Path)"
        return
    }
    $godotVersion = Get-GodotVersion $godot.Path
    Fact 'Godot' "$($godot.Path) ($($godot.How)); --version: $godotVersion"
    if ($godotVersion -notmatch '^4\.7\.2\.stable\.mono\.') {
        Row 'FAIL' 'preflight-godot' "That executable reports '$godotVersion', not 4.7.2.stable.mono."
        return
    }
    if ($onWindows -and $godot.Path -notmatch '_console\.exe$') {
        Row 'FAIL' 'preflight-godot' 'Use the _console.exe from the Godot download, so its output (engine errors) can be read.'
        return
    }

    # ---- 1. the UI check runner ----
    $arguments = @('run', '--project', (Join-Path (Join-Path $repo 'tools') 'UiCheck'), '--', '--godot', $godot.Path, '--out', $detail) + $RunnerArgs
    Fact 'Runner command' ('dotnet ' + ($arguments -join ' '))
    $runnerStart = Get-Date
    $runnerExit = Invoke-Limited 'dotnet' $arguments (Join-Path $run 'console-stdout.txt') (Join-Path $run 'console-stderr.txt') ($TimeoutMinutes * 60) -Echo
    Fact 'Runner exit code' $(if ($null -eq $runnerExit) { "none: stopped at the $TimeoutMinutes-minute limit" } else { "$runnerExit" })
    Fact 'Runner time' ('{0:N0} s' -f ((Get-Date) - $runnerStart).TotalSeconds)
    if ($null -eq $runnerExit) { Row 'FAIL' 'runner-finished' "The runner did not finish within $TimeoutMinutes minutes and was stopped; results it reported before that are kept." }
    elseif ($runnerExit -ne 0 -and $runnerExit -ne 1) { Row 'FAIL' 'runner-finished' "The runner ended with exit code $runnerExit (see console.log)." }
    $null = Stop-OwnProcesses 'after the runner'

    # ---- 2. the Godot editor opens the project ----
    $editorArgs = @('--editor', '--path', (Join-Path $repo 'client'), '--quit')
    if ($RunnerArgs -contains '--opengl') { $editorArgs = @('--rendering-method', 'gl_compatibility', '--rendering-driver', 'opengl3') + $editorArgs }
    $editorOut = Join-Path $run 'editor-stdout.txt'
    $editorErr = Join-Path $run 'editor-stderr.txt'
    # The editor rewrites project.godot in its own format on open; put the committed file back afterwards.
    $projectFile = Join-Path (Join-Path $repo 'client') 'project.godot'
    $projectBytes = [System.IO.File]::ReadAllBytes($projectFile)
    try { $editorExit = Invoke-Limited $godot.Path $editorArgs $editorOut $editorErr $EditorTimeoutSeconds }
    finally {
        $null = Stop-OwnProcesses 'after the editor'
        $now = [System.IO.File]::ReadAllBytes($projectFile)
        if ([Convert]::ToBase64String($now) -ne [Convert]::ToBase64String($projectBytes)) {
            [System.IO.File]::WriteAllBytes($projectFile, $projectBytes)
            Log 'The editor reformatted client/project.godot; the original file was restored.'
        }
    }
    $editorText = @(Get-Content $editorOut, $editorErr -ErrorAction SilentlyContinue)
    $editorErrors = @()
    for ($i = 0; $i -lt $editorText.Count; $i++) {
        if ($editorText[$i] -cnotmatch 'ERROR') { continue }
        $next = if ($i + 1 -lt $editorText.Count) { $editorText[$i + 1] } else { '' }
        if ($editorText[$i] -match 'ERR_CANT_OPEN' -and $next -match 'init_output_device') { continue }   # no audio device (see docs/client.md)
        $editorErrors += $editorText[$i].Trim()
    }
    $editorCommand = "`"$($godot.Path)`" $($editorArgs -join ' ')"
    if ($null -eq $editorExit) {
        Row 'FAIL' 'editor-opens-project' "$editorCommand did not finish within $EditorTimeoutSeconds s."
    }
    elseif ($editorExit -eq 0 -and $editorErrors.Count -eq 0) {
        Row 'PASS' 'editor-opens-project' "$editorCommand`: exit 0, no engine errors (the editor window opened, loaded and imported the project, then quit)."
    }
    else {
        Row 'FAIL' 'editor-opens-project' ("$editorCommand`: exit $editorExit; " + (($editorErrors | Select-Object -First 5) -join ' | '))
    }
    $null = Stop-OwnProcesses 'after the editor'
    $finished = $true
}
catch {
    Row 'FAIL' 'script' ("$($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)")
}
finally {
    $null = Stop-OwnProcesses 'at the end'
    Start-Sleep -Seconds 3
    $left = @(Get-OwnProcesses)
    if ($left.Count -gt 0) { Row 'FAIL' 'cleanup' ("Still running after cleanup: " + (($left | ForEach-Object { "$($_.Name) (PID $($_.Id))" }) -join ', ')) }
    else { Log 'Cleanup checked: nothing this run started is still running.' }
    if ($awake) { $null = [TrenchSim.Power]::SetThreadExecutionState([uint32]2147483648) }
    $ended = Get-Date
    Fact 'Ended' $ended.ToString('yyyy-MM-dd HH:mm:ss zzz')
    Fact 'Duration' ('{0:N0} s' -f ($ended - $started).TotalSeconds)
    if ($onWindows) {
        $lockedEnd = @(Get-Process -Name LogonUI -ErrorAction SilentlyContinue).Count -gt 0
        Fact 'Screen locked at the end' $(if ($lockedEnd) { 'yes' } else { 'no' })
    }

    # Merge the runner's console output into one file, as it appeared (stdout, then stderr).
    $console = Join-Path $run 'console.log'
    Set-Content -Path $console -Value @(Get-Content (Join-Path $run 'console-stdout.txt') -ErrorAction SilentlyContinue) -Encoding UTF8
    Add-Content -Path $console -Value '----- stderr -----' -Encoding UTF8
    Add-Content -Path $console -Value @(Get-Content (Join-Path $run 'console-stderr.txt') -ErrorAction SilentlyContinue) -Encoding UTF8

    # ---- the verdict ----
    try {
    $rows = New-Object System.Collections.Generic.List[object]
    $detailSummary = Join-Path $detail 'summary.md'
    if (Test-Path $detailSummary) {
        foreach ($line in Get-Content $detailSummary -Encoding UTF8) {
            if ($line -match '^\| (PASS|FAIL|INFO) \| (.+?) \| (.*) \|$') {
                $rows.Add([pscustomobject]@{ Result = $Matches[1]; Id = $Matches[2]; Detail = $Matches[3] })
            }
        }
    }
    else {
        # The runner was stopped before writing its summary: keep what it had already reported. Builds and Godot
        # checks are printed as they happen; each client writes its checks to its log as it goes.
        foreach ($line in Get-Content (Join-Path $run 'console-stdout.txt') -Encoding UTF8 -ErrorAction SilentlyContinue) {
            if ($line -cmatch '^(PASS|FAIL|INFO)\s+([^:]+): (.*)$') {
                $rows.Add([pscustomobject]@{ Result = $Matches[1]; Id = $Matches[2]; Detail = $Matches[3] })
            }
        }
        foreach ($role in 'host', 'guest', 'reopen') {
            $log = Join-Path (Join-Path $detail 'e2e') "$role.log"
            foreach ($line in Get-Content $log -Encoding UTF8 -ErrorAction SilentlyContinue) {
                if ($line -cmatch '^\S+ CHECK (\S+) (PASS|FAIL) ?(.*)$') {
                    $rows.Add([pscustomobject]@{ Result = $Matches[2]; Id = "$role`: $($Matches[1])"; Detail = $Matches[3] })
                }
            }
        }
        if ($rows.Count -gt 0) { Log "The runner wrote no summary; $($rows.Count) results were recovered from its output and the client logs." }
    }
    $expected = @(Get-Content (Join-Path $PSScriptRoot 'expected-checks.txt') -Encoding UTF8 | Where-Object { $_ -and -not $_.StartsWith('#') })
    $reported = @($rows | ForEach-Object { $_.Id })
    $missing = @($expected | Where-Object {
        $entry = $_
        if ($entry -match '^([^:]+\|[^:]+): (.*)$') {
            $check = $Matches[2]
            -not @($Matches[1].Split('|') | Where-Object { $reported -contains "$_`: $check" }).Count
        }
        else { $reported -notcontains $entry }
    })
    $manual = @(
        @('editor-build-button', 'Pressing Build in the Godot editor (the same build ran from the command line: godot-build-solutions).'),
        @('editor-play-button', 'Pressing Play (F5) in the Godot editor (the main scene was launched from the command line: godot-launch-scene).'),
        @('mouse', 'Using the client with the mouse (every automated check uses the keyboard).'),
        @('clipboard-copy-code', 'Copy code puts the match code on the Windows clipboard.'),
        @('window-close-button', "Closing a client with its title-bar X (the check closes the client from inside), then rejoining."),
        @('visual-readability', 'Judging by eye that text at 150 % and high contrast read well (the sizes, widths and colours were checked automatically; see the screenshots).')
    )
    $all = New-Object System.Collections.Generic.List[object]
    foreach ($r in $rows) { $all.Add($r) }
    foreach ($r in $own) { $all.Add($r) }
    $pass = @($all | Where-Object { $_.Result -eq 'PASS' }).Count
    $fail = @($all | Where-Object { $_.Result -eq 'FAIL' }).Count
    $notTested = $missing.Count + $manual.Count
    $verdict = if ($fail -gt 0) { 'FAIL' } elseif (-not $finished -or $null -eq $runnerExit -or $missing.Count -gt 0) { 'INCOMPLETE' } elseif ($runnerExit -ne 0) { 'FAIL' } else { 'PASS' }

    $md = New-Object System.Collections.Generic.List[string]
    $md.Add('# M7.3 Windows UI check')
    $md.Add('')
    $md.Add("**Verdict: $verdict.** $pass PASS, $fail FAIL, $notTested NOT TESTED ($($missing.Count) automated checks that did not run, $($manual.Count) manual checks).")
    if ($verdict -eq 'PASS') { $md.Add('Every automated check passed on this PC. The manual checks below were not tested by this script.') }
    $md.Add('')
    $md.Add('| Run | |')
    $md.Add('|---|---|')
    foreach ($f in $facts) { $md.Add($f) }
    $md.Add('')
    if ($fail -gt 0) {
        $md.Add('## FAIL')
        $md.Add('')
        foreach ($r in $all | Where-Object { $_.Result -eq 'FAIL' }) { $md.Add("- **$($r.Id)**: $($r.Detail)") }
        $md.Add('')
    }
    $md.Add('## NOT TESTED')
    $md.Add('')
    foreach ($m in $missing) { $md.Add("- **$m**: automated check that did not report in this run (the run stopped before it, or it was not reached).") }
    foreach ($m in $manual) { $md.Add("- **$($m[0])** (manual): $($m[1])") }
    $md.Add('')
    $md.Add('## PASS')
    $md.Add('')
    $md.Add(($all | Where-Object { $_.Result -eq 'PASS' } | ForEach-Object { $_.Id }) -join ', ')
    $md.Add('')
    $md.Add('## Files')
    $md.Add('')
    $md.Add('- `detail/summary.md`: every check with what was seen (the runner''s own report).')
    $md.Add('- `detail/e2e/*.png`: screenshots; `detail/e2e/*.log`: each client''s checks in order; `detail/logs/*.txt`: every process''s output.')
    $md.Add('- `console.log`: the runner''s complete console output; `run.log`: this script''s log; `editor-stdout.txt` / `editor-stderr.txt`: the editor''s output.')
    Set-Content -Path (Join-Path $run 'summary.md') -Value $md -Encoding UTF8
    }
    catch {
        Log "Could not write the summary: $($_.Exception.Message) at line $($_.InvocationInfo.ScriptLineNumber)"
        $verdict = 'INCOMPLETE'; $pass = 0; $fail = 0; $notTested = 0
    }

    $code = switch ($verdict) { 'PASS' { 0 } 'FAIL' { 1 } default { 2 } }
    if (@($own | Where-Object { $_.Id -like 'preflight-*' }).Count -gt 0) { $code = 3 }
    Log "Verdict: $verdict ($pass PASS, $fail FAIL, $notTested NOT TESTED). Exit code $code. Summary: $(Join-Path $run 'summary.md')"
    exit $code
}
