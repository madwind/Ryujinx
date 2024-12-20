@echo off
:: 设置变量
set PLATFORM_NAME=win-x64
set ZIP_OS_NAME=windows
set BUILD_VERSION=1.0.0
set GIT_SHORT_HASH=abc123

:: 发布 Ryujinx
dotnet publish -c Release -r %PLATFORM_NAME% -o publish ^
  -p:Version=%BUILD_VERSION% ^
  -p:SourceRevisionId=%GIT_SHORT_HASH% ^
  -p:DebugType=embedded src/Ryujinx --self-contained

:: 删除无用文件
del publish\libarmeilleure-jitsupport.dylib

:: 压缩文件
mkdir release_output
7z a release_output\ryujinx-%BUILD_VERSION%-%ZIP_OS_NAME%.zip publish

echo 打包完成！输出目录为 release_output
pause
