@echo off

:RETRY
set DEVICE_PID=0000
set USB_INTERFACE=0

for /f "tokens=1,2 delims==" %%a in (config.ini) do (
    if %%a==device_pid set dev_pid=%%b
    if %%a==usb_interface set usb_if=%%b
)

if "%dev_pid%" EQU "" (
    echo device_pid NOT DEFINED
    goto EXIT
)
if "%usb_if%" EQU "" (
    echo usb_interface NOT DEFINED
    goto EXIT
)

if "%dev_pid%" NEQ "%DEVICE_PID%" set DEVICE_PID=%dev_pid%
if "%usb_if%" NEQ "%USB_INTERFACE%" set USB_INTERFACE=%usb_if%

:Set_relay_status
REM Send USB HID Command
usb_hid_cmd.exe w %DEVICE_PID% %USB_INTERFACE% 02 02 00 00 20 4E 01 00 F4 01 B8 0B

REM Enable hibernation if needed
powercfg /hibernate on

REM Trigger S4 sleep after 10s, wake after 30s
echo Initiating S4 sleep in 10 seconds, duration 30 seconds...
pwrtest.exe /sleep /delay 10 /duration 30 /state s4

:EXIT
exit /b
