@echo off
rem Запуск Mazzacha CRM на Windows. Нужны .NET 10 SDK и Node.js 20 или новее.
chcp 65001 >nul
cd /d "%~dp0"

if exist "src\MazzachaCRM.Api\wwwroot\index.html" goto run

echo Собираю интерфейс: это нужно только при первом запуске...
pushd web
call npm install --no-audit --no-fund
if errorlevel 1 goto failed
call npm run build
if errorlevel 1 goto failed
popd

:run
echo.
echo Mazzacha CRM запускается и откроется в браузере: http://localhost:5080
echo Чтобы остановить программу, закройте это окно.
echo.
dotnet run --project src\MazzachaCRM.Api -c Release --no-launch-profile -- --open-browser %*
if errorlevel 1 pause
goto :eof

:failed
popd
echo.
echo Не удалось собрать интерфейс. Проверьте, что установлен Node.js: https://nodejs.org
pause
