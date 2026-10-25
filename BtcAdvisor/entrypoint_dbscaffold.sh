#!/bin/bash

set -e

mkdir -p postgres

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

>&2 echo "dotnet ef database update"
until dotnet ef database update; do
>&2 echo "SQL Server is starting up"
   sleep 1
done

>&2 echo "SQL Server is up - starting scaffolding ..."

# Generiere Modelle in einem temporären Verzeichnis
dotnet user-secrets init && dotnet user-secrets set "${ConnectionStringName}" "Host=db;Database=postgres;Username=sa;Password=$1;SslMode=Disable"
dotnet ef dbcontext scaffold Name="${ConnectionStringName}" Npgsql.EntityFrameworkCore.PostgreSQL -o TempDbModels/Models -f --context ApplicationDbContext --context-dir TempDbModels --namespace BtcAdvisor.Repository.Models --context-namespace BtcAdvisor.Repository

mkdir -p /Repository/Generated/Models/
cp -r TempDbModels/* /Repository/Generated
rm -rf TempDbModels

>&2 echo "Scaffolding done"
