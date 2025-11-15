@echo off
REM Godot MCP 服务器启动脚本 (Windows)

echo ======================================
echo   Godot MCP Server
echo ======================================
echo.

REM 检查 .NET SDK
where dotnet >nul 2>nul
if %ERRORLEVEL% NEQ 0 (
    echo 错误: 未找到 .NET SDK
    echo 请访问 https://dotnet.microsoft.com/download 安装 .NET 8.0 SDK
    pause
    exit /b 1
)

for /f "tokens=*" %%i in ('dotnet --version') do set DOTNET_VERSION=%%i
echo √ .NET SDK 版本: %DOTNET_VERSION%
echo.

REM 切换到 McpServer 目录
cd /d "%~dp0McpServer"

REM 检查是否需要还原依赖
if not exist "obj" (
    echo 正在还原 NuGet 包...
    dotnet restore
    echo.
)

REM 构建项目
echo 正在构建项目...
dotnet build --configuration Release
if %ERRORLEVEL% NEQ 0 (
    echo.
    echo 错误: 构建失败
    pause
    exit /b 1
)

echo.
echo ======================================
echo   MCP 服务器正在运行
echo   端口: 7777
echo   按 Ctrl+C 停止服务器
echo ======================================
echo.

REM 运行服务器
dotnet run --configuration Release
