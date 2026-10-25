#!/bin/bash
. .env

mkdir -p postgres/data

sudo chmod 777 data/postgres/

# Führt den Build-Prozess synchron aus und überprüft den Exit-Status
dotnet build
if [ $? -ne 0 ]; then
    echo "dotnet build failed"
    exit 1
fi

dotnet user-secrets init 
docker compose -f docker-compose-dbscaffold.yml up --build --remove-orphans -d
echo "Scaffolding..."
docker compose logs -f

docker logs -f btcadvisor-worker &

# Warte auf das Beenden des Containers
while [ $(docker inspect -f '{{.State.Running}}' btcadvisor-worker) == "true" ]; do
    sleep 1
done

# Stoppe alle Dienste, wenn das Skript beendet wird
docker compose down
