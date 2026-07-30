@echo off
setlocal
REM RDPWrap Tool launcher (framework-dependent).
REM Uses the .NET 8 Desktop Runtime installed on the machine. If a private
REM runtime is bundled next to this script under "dotnet\", prefer it instead.
if exist "%~dp0dotnet\dotnet.exe" (
    set "DOTNET_ROOT=%~dp0dotnet"
    set "DOTNET_ROOT_X64=%~dp0dotnet"
    set "DOTNET_MULTILEVEL_LOOKUP=0"
)
start "" "%~dp0RDPWrapTool.exe"
endlocal
