#!/usr/bin/env bash
# Запуск Mazzacha CRM на Linux и macOS. Нужны .NET 10 SDK и Node.js 20 или новее.
set -e
cd "$(dirname "$0")"

# Интерфейс собирается при каждом запуске, чтобы после обновления не остался старый.
echo "Собираю интерфейс..."
(cd web && npm install --no-audit --no-fund --silent && npm run build)

echo "Mazzacha CRM: http://localhost:5080  (остановить: Ctrl+C)"
exec dotnet run --project src/MazzachaCRM.Api -c Release --no-launch-profile -- "$@"
