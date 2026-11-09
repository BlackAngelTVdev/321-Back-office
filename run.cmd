@echo off
rem Compile et lance la version C# / .NET de 321 Back Office.
setlocal
cd /d "%~dp0"

set "PROJECT=dotnet\BackOffice.csproj"
if not defined BUILD_DIR set "BUILD_DIR=dotnet\build"

if not exist "%PROJECT%" (
  echo Erreur : projet introuvable ^(%PROJECT%^).
  exit /b 1
)

set "DOTNET=dotnet"
if exist ".dotnet\dotnet.exe" set "DOTNET=%CD%\.dotnet\dotnet.exe"

where "%DOTNET%" >nul 2>nul
if errorlevel 1 (
  echo Erreur : le SDK .NET est introuvable. Installez-le : https://dotnet.microsoft.com/download
  exit /b 1
)

echo Compilation ...
"%DOTNET%" build "%PROJECT%" -c Release -o "%BUILD_DIR%" --nologo -v minimal
if errorlevel 1 exit /b 1

if not exist "%BUILD_DIR%\BackOffice.dll" (
  echo Erreur : compilation terminee sans binaire.
  exit /b 1
)

echo Lancement de 321 Back Office ...
start "" "%DOTNET%" "%BUILD_DIR%\BackOffice.dll" %*
