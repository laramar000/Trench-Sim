<#
.SYNOPSIS
  Gets the newest Trench Sim from GitHub, puts a "Test Trench Sim" shortcut on the desktop and opens the test center.

.DESCRIPTION
  For a Windows PC without git. Paste this one line into PowerShell (it downloads and runs this script):

    irm https://raw.githubusercontent.com/laramar000/Trench-Sim/m7-client/tools/TestCenter/Get-TrenchSim.ps1 | iex

  1. Asks GitHub for the newest commit of the branch and downloads that exact commit as a ZIP into
     %USERPROFILE%\TrenchSim\Trench-Sim-<commit> (kept if it is already there), so the folder name says which version
     is tested.
  2. Checks for the .NET 10 SDK; if it is missing, offers to install it with winget (or opens its download page).
  3. Puts a "Test Trench Sim" shortcut on the desktop pointing at that folder's "Test Trench Sim.cmd", and opens it.
  Older downloaded folders are left as they are.
#>
param(
    [string]$Branch = 'm7-client',
    [string]$Into = (Join-Path $env:USERPROFILE 'TrenchSim'),
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$repo = 'laramar000/Trench-Sim'

try {
    Write-Host "Trench Sim: looking up the newest version of '$Branch'..."
    $sha = (Invoke-RestMethod "https://api.github.com/repos/$repo/commits/$Branch" -Headers @{ 'User-Agent' = 'TrenchSim-TestCenter' }).sha
    if ($sha -notmatch '^[0-9a-f]{40}$') { throw "GitHub did not say which commit is newest (got '$sha')." }
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
    $link.Description = "Trench Sim test center ($($sha.Substring(0, 7)))"
    $link.Save()
    Write-Host "The 'Test Trench Sim' shortcut on your desktop now opens version $($sha.Substring(0, 7))."
    if (-not $NoStart) { Start-Process -FilePath $launcher -WorkingDirectory $folder }
}
catch {
    Write-Host ''
    Write-Host "Could not get Trench Sim: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host 'Check the internet connection and try again.'
}
