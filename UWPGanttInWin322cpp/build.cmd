@echo off
rem Builds the managed bridge (GanttIsland) and the native Win32 host (GanttHost) into .\out
setlocal
set ROOT=%~dp0
set OUT=%ROOT%out

dotnet build "%ROOT%GanttIsland\GanttIsland.csproj" -c Release -o "%OUT%" || exit /b 1

for /f "usebackq delims=" %%i in (`"%ProgramFiles(x86)%\Microsoft Visual Studio\Installer\vswhere.exe" -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath`) do set VS=%%i
call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || exit /b 1

pushd "%ROOT%GanttHost"
cl /nologo /EHsc /W4 /std:c++17 /DUNICODE /D_UNICODE /Fo"%OUT%\\" /Fe"%OUT%\GanttHost.exe" main.cpp ^
   /link /SUBSYSTEM:WINDOWS /MANIFEST:EMBED /MANIFESTINPUT:"%ROOT%GanttHost\GanttHost.exe.manifest" mscoree.lib user32.lib gdi32.lib ole32.lib || (popd & exit /b 1)
popd

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT%GanttHost\make-config.ps1" -Out "%OUT%" || exit /b 1
echo Built %OUT%\GanttHost.exe
