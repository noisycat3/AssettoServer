@echo off
REM copyplugin.bat

REM Check if correct number of arguments is provided
if "%~3"=="" (
    echo Usage: CALL copyplugin.bat ^(AssemblyName^) ^(Configuration^) ^(TargetFramework^)
    exit /b 1
)

set AssemblyName=%~1
set Configuration=%~2
set TargetFramework=%~3

set SourceFolder="%AssemblyName%\bin\%Configuration%\%TargetFramework%\"
set TargetFolder="AssettoServer\bin\%Configuration%\%TargetFramework%\win-x64\plugins\%AssemblyName%"

REM Check if SourceFolder exists
if not exist %SourceFolder% (
    echo Source folder does not exist: %SourceFolder%
    exit /b 1
)

REM Check if TargetFolder exists, if not create it
if not exist %TargetFolder% (
    mkdir %TargetFolder%
)

REM Copy files
echo Copying files from %SourceFolder% to %TargetFolder%...
xcopy /y %SourceFolder%*.dll %TargetFolder%\
xcopy /y %SourceFolder%*.pdb %TargetFolder%\
xcopy /y %SourceFolder%*.json %TargetFolder%\

echo Done!
exit /b 0