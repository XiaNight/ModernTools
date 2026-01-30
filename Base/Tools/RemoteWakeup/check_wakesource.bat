@echo off
setlocal enabledelayedexpansion

set "PCI_FOUND=0"

for /f "delims=" %%L in ('powercfg -lastwake') do (
	echo "%%L" | findstr /i "PCI" >nul
	if !errorlevel! equ 0 (
		set "PCI_FOUND=1"
		goto :end_check
	)
)

:end_check

echo PCI_FOUND=!PCI_FOUND!

exit /b %PCI_FOUND%
endlocal
