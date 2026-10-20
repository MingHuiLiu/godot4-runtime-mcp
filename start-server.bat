@echo off
dotnet build "%~dp0RuntimeHost\RuntimeHost.csproj" -c Release -v:q 1>&2
if errorlevel 1 exit /b %errorlevel%
dotnet "%~dp0RuntimeHost\bin\Release\net8.0\GodotRuntimeMcp.dll" mcp %*
