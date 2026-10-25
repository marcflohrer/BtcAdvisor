#!/bin/bash

# Load environment variables from .env file
if [ -f .env ]; then
    source .env
else
    echo ".env file not found."
    exit 1
fi

# Ensure start.sh is executable and run it
chmod +x start.sh
./start.sh || { echo "Startup script failed"; exit 1; }

# Wait for the database to be ready before applying migrations
echo "Waiting for the database to be ready..."
sleep 10  # Consider a more robust wait/check here

./start.sh

# Generate a timestamp
TIMESTAMP=$(date +"%Y%m%d%H%M%S")

# Apply EF Core migrations with a timestamp
dotnet ef migrations add Migration_$TIMESTAMP
dotnet ef database update
