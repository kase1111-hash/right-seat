@echo off
setlocal

echo ============================================
echo  Flight Guardian — Build Script
echo ============================================
echo.

:: Check for .NET 8 SDK
dotnet --version >nul 2>&1
if errorlevel 1 (
    echo ERROR: .NET SDK not found. Install .NET 8 SDK from:
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    exit /b 1
)

for /f "tokens=1 delims=." %%v in ('dotnet --version') do set DOTNET_MAJOR=%%v
if %DOTNET_MAJOR% LSS 8 (
    echo ERROR: .NET 8 or later required. Found .NET %DOTNET_MAJOR%.
    echo   https://dotnet.microsoft.com/download/dotnet/8.0
    exit /b 1
)

echo [1/4] Restoring NuGet packages...
dotnet restore FlightGuardian.sln
if errorlevel 1 (
    echo ERROR: Package restore failed.
    exit /b 1
)
echo.

echo [2/4] Building solution (Release)...
dotnet build FlightGuardian.sln -c Release --no-restore
if errorlevel 1 (
    echo ERROR: Build failed.
    exit /b 1
)
echo.

echo [3/4] Running tests...
dotnet test FlightGuardian.sln -c Release --no-build --verbosity normal
if errorlevel 1 (
    echo WARNING: Some tests failed. See output above.
    echo.
)

echo [4/4] Publishing executables...

echo   - Guardian.App (headless console)...
dotnet publish src\Guardian.App\Guardian.App.csproj -c Release --no-build -o publish\Guardian.App
if errorlevel 1 (
    echo WARNING: Guardian.App publish failed.
)

echo   - Guardian.Desktop (Avalonia companion window)...
dotnet publish src\Guardian.Desktop\Guardian.Desktop.csproj -c Release --no-build -o publish\Guardian.Desktop
if errorlevel 1 (
    echo WARNING: Guardian.Desktop publish failed.
)

echo   - Guardian.Replay (scenario replay CLI)...
dotnet publish src\Guardian.Replay\Guardian.Replay.csproj -c Release --no-build -o publish\Guardian.Replay
if errorlevel 1 (
    echo WARNING: Guardian.Replay publish failed.
)

echo.
echo ============================================
echo  Build complete!
echo ============================================
echo.
echo  Outputs:
echo    publish\Guardian.App\        — Headless console monitor
echo    publish\Guardian.Desktop\    — Desktop companion window
echo    publish\Guardian.Replay\     — Scenario replay CLI
echo    efb\GuardianApp\             — EFB tablet app (copy to MSFS EFB folder)
echo.
echo  Quick start:
echo    publish\Guardian.Desktop\Guardian.Desktop.exe
echo.
echo  Replay a scenario:
echo    publish\Guardian.Replay\Guardian.Replay.exe training\scenarios\
echo.

endlocal
