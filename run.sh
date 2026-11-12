#!/usr/bin/env bash
# Запуск Mazzacha CRM на Linux и macOS. Нужны .NET 10 SDK и Node.js 20 или новее.
set -e
cd "$(dirname "$0")"

if [ ! -f src/MazzachaCRM.Api/wwwroot/index.html ]; then
  echo "Собираю интерфейс: это нужно только при первом запуске..."
  (cd web && npm install --no-audit --no-fund && npm run build)
fi

echo "Mazzacha CRM: http://localhost:5080  (остановить: Ctrl+C)"
exec dotnet run --project src/MazzachaCRM.Api -c Release --no-launch-profile -- "$@"
