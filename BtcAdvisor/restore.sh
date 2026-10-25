#!/bin/bash

# Lade die Umgebungsvariablen aus der .env-Datei
set -a
source .env
set +a

# Prüfen, ob die Backup-Datei existiert
if [ ! -f "$1" ]; then
    echo "Fehler: Backup-Datei $1 nicht gefunden." >&2
    exit 1
fi

#adding a check to ensure the container is running before attempting the restore.
if ! docker ps | grep -q btcadvisor-postgres-db; then
    echo "Fehler: Der Container btcadvisor-postgres-db läuft nicht." >&2
    exit 1
fi
# check if user sa and database postgres exists and have the correct permissions
if ! docker exec -i btcadvisor-postgres-db psql -U sa -d postgres -c "\du" | grep -q sa; then
    echo "Fehler: Der Benutzer sa existiert nicht oder hat keine Berechtigungen." >&2
    exit 1
fi

# adding error handling to detect if the restore fails:
if ! gunzip -c "$1" | docker exec -i btcadvisor-postgres-db psql -U sa -d postgres; then
    echo "Fehler: Wiederherstellung des Backups fehlgeschlagen." >&2
    exit 1
fi

# Check the exit status of the previous command before printing the success message.
if [ $? -eq 0 ]; then
    echo "Backup erfolgreich wiederhergestellt: $1"
else
    echo "Fehler: Wiederherstellung des Backups fehlgeschlagen." >&2
    exit 1
fi

