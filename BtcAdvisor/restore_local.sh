#!/bin/bash

# Prüfen, ob eine Backup-Datei angegeben wurde
if [ $# -eq 0 ]; then
    echo "Verwendung: $0 <backup-datei.gz>"
    echo "Beispiel: $0 backups/backup_2024-10-24_12-00-00.sql.gz"
    exit 1
fi

BACKUP_FILE="$1"

# Prüfen, ob die Backup-Datei existiert
if [ ! -f "$BACKUP_FILE" ]; then
    echo "Fehler: Backup-Datei '$BACKUP_FILE' nicht gefunden." >&2
    exit 1
fi

# Prüfen, ob der Docker-Container läuft
if [ "$(docker inspect -f '{{.State.Running}}' btcadvisor-postgres-db 2>/dev/null)" != "true" ]; then
    echo "Fehler: Der Container 'btcadvisor-postgres-db' läuft nicht. Restore abgebrochen." >&2
    exit 1
fi

echo "Starte Restore aus: $BACKUP_FILE"

# Backup entpacken und wiederherstellen
gunzip -c "$BACKUP_FILE" | docker exec -i btcadvisor-postgres-db psql -U sa -d postgres -w

if [ $? -eq 0 ]; then
    echo "Restore erfolgreich abgeschlossen."
else
    echo "Fehler: Restore fehlgeschlagen." >&2
    exit 1
fi
