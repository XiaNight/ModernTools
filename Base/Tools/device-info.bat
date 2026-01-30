@echo off

:: Set code page to UTF-8
chcp 65001

:: Get Model
for /f "delims=" %%A in ('powershell -Command "(Get-WmiObject -Class Win32_ComputerSystem).Model"') do set Model=%%A

:: Get OS Caption (Windows Edition)
for /f "delims=" %%A in ('powershell -Command "(Get-WmiObject -Class Win32_OperatingSystem).Caption"') do set OSCaption=%%A

:: Get Windows Release Version dynamically from registry
for /f "tokens=3 delims= " %%A in ('reg query "HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion" /v DisplayVersion 2^>nul') do set ReleaseVersion=%%A

:: Get CPU Model
@REM for /f "delims=" %%A in ('powershell -Command "(Get-WmiObject -Class Win32_Processor).Name"') do set CPU=%%A

:: Get Motherboard Model
@REM for /f "delims=" %%A in ('powershell -Command "(Get-WmiObject -Class Win32_BaseBoard).Product"') do set Motherboard=%%A

:: Display information
echo %Model% / %OSCaption% / %ReleaseVersion%
