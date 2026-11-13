@echo off
rem Trench Sim test center: double-click this file. See docs/testing.md.
setlocal
cd /d "%~dp0"
title Trench Sim test center
where dotnet >nul 2>nul
if errorlevel 1 (
  echo.
  echo   The .NET 10 SDK is needed to build and test Trench Sim, and it is not installed.
  echo   Your browser now opens its download page: install the SDK for Windows x64,
  echo   then double-click "Test Trench Sim" again.
  start "" "https://dotnet.microsoft.com/download/dotnet/10.0"
  echo.
  pause
  exit /b 3
)
echo   Starting the Trench Sim test center (the first start takes a minute)...
dotnet run --project tools\TestCenter -c Release -v q -- %*
set code=%errorlevel%
echo.
pause
exit /b %code%
