

#!/bin/bash

set -e

mkdir -p postgres/data

>&2 echo "Running entrypoint.sh"
until PATH="$PATH:/root/.dotnet/tools"; do
>&2 echo "Setting up env variables..."
sleep 1
done

>&2 echo "Running entrypoint.sh :: PATH IS SET"
dotnet tool install --global dotnet-ef

>&2 echo "Running entrypoint.sh :: EF IS INSTALLED"
ConnectionStringName="ConnectionStrings:DefaultConnection"
dotnet user-secrets init && dotnet user-secrets set "$ConnectionStringName" "$1" --project .

>&2 echo "Running entrypoint.sh :: 0 SECRETS SET UP $0"
>&2 echo "Running entrypoint.sh :: 1 SECRETS SET UP $1"
dotnet build

now_hourly=$(date +%Y-%d-%b-%H_%M) 
>&2 echo "dotnet ef migrations add" $now_hourly"ChangeDatabase"
dotnet ef migrations add $now_hourly"ChangeDb" --context BtcAdvisor.Repository.ApplicationDbContext --output-dir data/Migrations;

>&2 echo "dotnet ef database update"
until dotnet ef database update; do
>&2 echo "SQL Server is starting up"
sleep 1
done

>&2 echo "Migration done"
