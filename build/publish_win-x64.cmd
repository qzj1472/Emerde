cd /d %~dp0

@REM This script is used to publish the project to a folder and compress it to a 7z file.
@REM You should have 7z installed and added to PATH.
@REM You should prepare the bin file ffmpeg.exe and ffplay.exe in publish folder.

del /s /q ..\src\Emerde\bin\x64\Release\net9.0-windows10.0.26100.0\publish\win-x64\Emerde.exe
dotnet publish ..\src\Emerde\Emerde.csproj -c Release -p:PublishProfile=FolderProfile
rd /s /q ..\src\Emerde\bin\x64\Release\net9.0-windows10.0.26100.0\publish\win-x64\downloads\
copy ffmpeg.exe ..\src\Emerde\bin\x64\Release\net9.0-windows10.0.26100.0\publish\win-x64\
copy ffplay.exe ..\src\Emerde\bin\x64\Release\net9.0-windows10.0.26100.0\publish\win-x64\
del /s /q publish.7z
7z a publish.7z ..\src\Emerde\bin\x64\Release\net9.0-windows10.0.26100.0\publish\win-x64\* -t7z -mx=5 -mf=BCJ2 -r -y
for /f "usebackq delims=" %%i in (`powershell -NoLogo -NoProfile -Command "Get-Content '..\src\Emerde\Emerde.csproj' | Select-String -Pattern '<AssemblyVersion>(.*?)</AssemblyVersion>' | ForEach-Object { $_.Matches.Groups[1].Value }"`) do @set version=%%i
del /s /q Emerde_v%version%_win-x64.7z
makemica micasetup.json
rename publish.7z Emerde_v%version%_win-x64.7z

@pause
