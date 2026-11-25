@echo off
setlocal
rem Builds RGBOff.exe from RGBOff.cs using the C# compiler included with Windows (.NET Framework 4.x).
cd /d "%~dp0"

set "VERSION=2.2.0.0"

set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" (
    echo ERROR: C# compiler not found at "%CSC%"
    pause
    exit /b 1
)

rem Generate the version attributes in a temp file so nothing extra is left on the desktop.
set "VERFILE=%TEMP%\RGBOff_Version.cs"
> "%VERFILE%" echo using System.Reflection;
>>"%VERFILE%" echo [assembly: AssemblyTitle("RGBOff")]
>>"%VERFILE%" echo [assembly: AssemblyProduct("RGBOff")]
>>"%VERFILE%" echo [assembly: AssemblyVersion("%VERSION%")]
>>"%VERFILE%" echo [assembly: AssemblyFileVersion("%VERSION%")]
>>"%VERFILE%" echo [assembly: AssemblyInformationalVersion("%VERSION%")]

echo Building RGBOff.exe version %VERSION%...
"%CSC%" /nologo /target:winexe /platform:x64 /optimize+ ^
    /win32icon:RGBOff.ico ^
    /out:RGBOff.exe ^
    RGBOff.cs "%VERFILE%"
set "RESULT=%ERRORLEVEL%"
del "%VERFILE%" 2>nul

if not "%RESULT%"=="0" (
    echo.
    echo BUILD FAILED
    pause
    exit /b 1
)

echo.
echo Build succeeded: %~dp0RGBOff.exe (version %VERSION%)
pause
