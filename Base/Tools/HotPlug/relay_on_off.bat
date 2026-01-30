
:RETRY
@echo off
@set DEVICE_PID=0000
@set USB_INTERFACE=0

for /f "tokens=1,2 delims==" %%a in (config.ini) do (
    if %%a==device_pid set dev_pid=%%b
    if %%a==usb_interface set usb_if=%%b
)

@if "%dev_pid%" EQU "" (
    @echo device_pid NOT DEFINED
    @goto EXIT
)
@if "%usb_if%" EQU "" (
    @echo usb_interface NOT DEFINED
    @goto EXIT
)

@if "%dev_pid%" NEQ "%DEVICE_PID%" (set DEVICE_PID=%dev_pid%)
@if "%usb_if%" NEQ "%USB_INTERFACE%" (set USB_INTERFACE=%usb_if%)

:Set_relay_status
@REM                                            CMD | Key | Index  | Data...
@usb_hid_cmd.exe w %DEVICE_PID% %USB_INTERFACE%  02   04    02  00   00

ping 127.0.0.1 -n 2 > nul

:Set_relay_status
@REM                                            CMD | Key | Index  | Data...
@usb_hid_cmd.exe w %DEVICE_PID% %USB_INTERFACE%  02   04    02  00   01