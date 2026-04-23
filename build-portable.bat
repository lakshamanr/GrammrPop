@echo off
REM Quick Portable Build for GrammrPop
REM Usage: build-portable.bat

echo =====================================
echo    GrammrPop Portable Build
echo =====================================
echo.

REM Run the PowerShell script
powershell -ExecutionPolicy Bypass -File "%~dp0Build-Portable.ps1"

if %ERRORLEVEL% NEQ 0 (
    echo.
    echo Build failed! Check errors above.
    pause
    exit /b 1
)

echo.
echo Press any key to exit...
pause >nul
