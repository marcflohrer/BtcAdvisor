#!/bin/bash

# Lade die Umgebungsvariablen aus der .env-Datei
set -a
source .env
set +a

# Verzeichnis für Backups erstellen
mkdir -p backups

# Prüfen, ob der Docker-Container läuft
if [ "$(docker inspect -f '{{.State.Running}}' btcadvisor-postgres-db 2>/dev/null)" != "true" ]; then
    echo "Fehler: Der Container 'btcadvisor-postgres-db' läuft nicht. Backup abgebrochen." >&2
    exit 1
fi

# Aktuelles Datum und Uhrzeit für den Dateinamen
BACKUP_DATE=$(date +%Y-%m-%d_%H-%M-%S)
BACKUP_FILE="backups/backup_${BACKUP_DATE}.sql.gz"

echo "Erstelle Backup: $BACKUP_FILE"

# Backup erstellen und direkt komprimieren
docker exec btcadvisor-postgres-db pg_dump -U sa -d postgres -w | gzip > "$BACKUP_FILE"

# Prüfen, ob die Backup-Datei erstellt wurde und nicht leer ist
if [ ! -s "$BACKUP_FILE" ]; then
    echo "Fehler: Die Backup-Datei ist leer oder wurde nicht erstellt." >&2
    exit 1
fi

# Optional: Ältere Backups lokal löschen (älter als 3 Tage)
find ./backups -type f -name "backup_*.sql.gz" -mtime +3 -delete

echo "Backup erfolgreich erstellt: $BACKUP_FILE"
