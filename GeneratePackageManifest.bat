@echo off

set APP_NAME=PixelViewer

echo ********** Start generating package manifest of %APP_NAME% **********

REM Get current version
dotnet run PackagingTool.cs -- get-current-version %APP_NAME%\%APP_NAME%.csproj > Packages\Packaging.txt
if %ERRORLEVEL% neq 0 (
    del /Q Packages\Packaging.txt
    exit
)
set /p CURRENT_VERSION=<Packages\Packaging.txt
dotnet run PackagingTool.cs -- get-current-informational-version %APP_NAME%\%APP_NAME%.csproj > Packages\Packaging.txt
if %ERRORLEVEL% neq 0 (
    del /Q Packages\Packaging.txt
    exit
)
set /p CURRENT_INFORMATIONAL_VERSION=<Packages\Packaging.txt
echo Version: %CURRENT_VERSION% (%CURRENT_INFORMATIONAL_VERSION%)

REM Generate package manifest
dotnet run PackagingTool.cs -- create-package-manifest %APP_NAME% %CURRENT_VERSION% %CURRENT_INFORMATIONAL_VERSION%
if %ERRORLEVEL% neq 0 (
    del /Q Packages\Packaging.txt
    exit
)

REM Rename to match URI in App.axaml.cs and duplicate for preview channel
move /Y Packages\%CURRENT_VERSION%\PackageManifest.json Packages\%CURRENT_VERSION%\PackageManifest-v2.json
if %ERRORLEVEL% neq 0 (
    del /Q Packages\Packaging.txt
    exit
)
copy /Y Packages\%CURRENT_VERSION%\PackageManifest-v2.json Packages\%CURRENT_VERSION%\PackageManifest-Preview-v2.json

REM Complete
del /Q Packages\Packaging.txt
