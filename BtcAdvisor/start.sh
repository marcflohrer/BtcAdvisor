#!/bin/bash
. .env

docker compose down

mkdir -p postgres/data

echo 'export DOTNET_SYSTEM_NET_DISABLEIPV6=1' >> ~/.bashrc
source ~/.bashrc

# Berechtigungen anpassen (plattformabhängig)
if [[ "$OSTYPE" == "linux-gnu"* ]]; then
    # Linux (Ubuntu)
    sudo chmod -R 777 data/postgres data/logs web/root/.aspnet/DataProtection-Keys
elif [[ "$OSTYPE" == "darwin"* ]]; then
    # macOS (kein sudo nötig, Berechtigungen anpassen)
    chmod -R 777 data/postgres data/logs web/root/.aspnet/DataProtection-Keys
else
    echo "Betriebssystem nicht unterstützt: $OSTYPE"
    exit 1
fi

# Führt den Build-Prozess synchron aus und überprüft den Exit-Status
dotnet build
if [ $? -ne 0 ]; then
    echo "dotnet build failed"
    exit 1
fi

dotnet user-secrets init 
docker compose up --build --remove-orphans -d

# Default behavior: print recent logs and terminate.
# Optional live mode: LIVE_LOG=true ./start.sh
if [[ "${LIVE_LOG:-false}" == "true" ]]; then
    docker logs -f btcadvisor-worker
else
    docker logs --tail 200 btcadvisor-worker
fi
