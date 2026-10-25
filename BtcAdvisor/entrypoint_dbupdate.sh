#!/bin/bash

set -e

mkdir -p postgres/data

>&2 echo "Running entrypoint_dbupdate.sh"
until PATH="$PATH:/root/.dotnet/tools"; do
>&2 echo "Setting up env variables..."
sleep 1
done

>&2 echo "Running entrypoint_dbupdate.sh :: PATH IS SET"
dotnet tool install --global dotnet-ef

>&2 echo "Running entrypoint_dbupdate.sh :: EF IS INSTALLED"
ConnectionStringName="ConnectionStrings:DefaultConnection"
dotnet user-secrets init && dotnet user-secrets set "$ConnectionStringName" "$1" --project .

>&2 echo "Running entrypoint_dbupdate.sh :: SECRETS SET UP"
>&2 echo "Connection String: $1"

dotnet build

>&2 echo "Running: dotnet ef database update"
dotnet ef database update --context BtcAdvisor.Repository.ApplicationDbContext

>&2 echo "Database update completed successfully"
