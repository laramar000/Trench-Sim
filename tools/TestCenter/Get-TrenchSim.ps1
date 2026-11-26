<#
.SYNOPSIS
  Gets the newest Trench Sim from GitHub, puts a "Test Trench Sim" shortcut on the desktop and opens the test center.

.DESCRIPTION
  For a Windows PC without git. Paste this one line into PowerShell (it downloads and runs this script):

    irm https://raw.githubusercontent.com/laramar000/Trench-Sim/claude/adoring-hamilton-s5yohc/tools/TestCenter/Get-TrenchSim.ps1 | iex

  0. Picks the branch: the working branch updated most recently on GitHub (m7-client, main or any claude/ branch), so
     the newest work is always the one fetched, whichever branch it is on (the owner, 2026-11-26: "always make it so
     get the newest version through the test center by entering 6"). -Branch <name> fetches that branch instead.
  1. Asks GitHub for the newest commit of the branch and downloads that exact commit as a ZIP into
     %USERPROFILE%\TrenchSim\Trench-Sim-<commit> (kept if it is already there), so the folder name says which version
     is tested.
  2. Checks for the .NET 10 SDK; if it is missing, offers to install it with winget (or opens its download page).
  3. Puts a "Test Trench Sim" shortcut on the desktop pointing at that folder's "Test Trench Sim.cmd", and opens it.
  Older downloaded folders are left as they are.
#>
param(
    [string]$Branch = '',
    [string]$Into = (Join-Path $env:USERPROFILE 'TrenchSim'),
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$repo = 'laramar000/Trench-Sim'

try {
    $headers = @{ 'User-Agent' = 'TrenchSim-TestCenter' }
    if ($Branch) {
        $candidates = @($Branch)
    }
    else {
        Write-Host 'Trench Sim: looking for the newest version on any working branch...'
        $candidates = @(Invoke-RestMethod "https://api.github.com/repos/$repo/branches?per_page=100" -Headers $headers |
            ForEach-Object { $_ } | ForEach-Object { $_.name } |
            Where-Object { $_ -eq 'm7-client' -or $_ -eq 'main' -or $_ -like 'claude/*' })
        if ($candidates.Count -eq 0) { $candidates = @('m7-client') }
    }
    $best = $null
    foreach ($name in $candidates) {
        try {
            $commit = Invoke-RestMethod "https://api.github.com/repos/$repo/commits/$name" -Headers $headers
            $when = [datetime]$commit.commit.committer.date
            if ($null -eq $best -or $when -gt $best.When) { $best = @{ Name = $name; Sha = $commit.sha; When = $when } }
        }
        catch {
            Write-Host "  (could not read branch '$name': $($_.Exception.Message))"
        }
    }
    if ($null -eq $best) { throw 'GitHub did not say which version is newest.' }
    $Branch = $best.Name
    $sha = $best.Sha
    if ($sha -notmatch '^[0-9a-f]{40}$') { throw "GitHub did not say which commit is newest (got '$sha')." }
    Write-Host "Newest version: branch '$Branch', $($sha.Substring(0, 7)), updated $($best.When.ToString('yyyy-MM-dd HH:mm'))."
    $folder = Join-Path $Into "Trench-Sim-$sha"
    if (Test-Path (Join-Path $folder 'TrenchSim.sln')) {
        Write-Host "Already downloaded: $folder"
    }
    else {
        New-Item -ItemType Directory -Force -Path $Into | Out-Null
        $zip = Join-Path $Into "Trench-Sim-$sha.zip"
        Write-Host "Downloading version $($sha.Substring(0, 7))..."
        Invoke-WebRequest "https://github.com/$repo/archive/$sha.zip" -OutFile $zip -UseBasicParsing
        Expand-Archive $zip -DestinationPath $Into -Force
        Remove-Item $zip
        if (-not (Test-Path (Join-Path $folder 'TrenchSim.sln'))) { throw "The download did not contain Trench Sim ($folder)." }
        Write-Host "Downloaded to $folder"
    }

    $version = $null
    try { $version = (& dotnet --version 2>$null) } catch { $version = $null }
    if (-not ($version -match '^10\.')) {
        Write-Host ''
        Write-Host 'The .NET 10 SDK is needed to build and test Trench Sim, and it is not installed.'
        if (Get-Command winget -ErrorAction SilentlyContinue) {
            $answer = Read-Host 'Install it now with winget? [Y/N]'
            if ($answer -match '^[Yy]') {
                winget install --id Microsoft.DotNet.SDK.10 --exact --accept-package-agreements --accept-source-agreements
                Write-Host 'If the test center says .NET is still missing, close this window and open the shortcut again.'
            }
        }
        else {
            Start-Process 'https://dotnet.microsoft.com/download/dotnet/10.0'
            Write-Host 'Install the .NET 10 SDK for Windows x64 from the page that opened, then open the shortcut.'
        }
    }

    $launcher = Join-Path $folder 'Test Trench Sim.cmd'
    $shortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Test Trench Sim.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut($shortcut)
    $link.TargetPath = $launcher
    $link.WorkingDirectory = $folder
    $link.Description = "Trench Sim test center ($Branch, $($sha.Substring(0, 7)))"
    $link.Save()
    Write-Host "The 'Test Trench Sim' shortcut on your desktop now opens version $($sha.Substring(0, 7))."
    if (-not $NoStart) { Start-Process -FilePath $launcher -WorkingDirectory $folder }
}
catch {
    Write-Host ''
    Write-Host "Could not get Trench Sim: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Check the internet connection and try again.'
}
