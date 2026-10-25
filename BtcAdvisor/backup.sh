#!/bin/bash

# Lade die Umgebungsvariablen aus der .env-Datei
set -a
source .env
set +a

# Verzeichnis für Backups erstellen (falls nicht vorhanden)
mkdir -p backups

# Prüfen, ob der Docker-Container läuft
if [ "$(docker inspect -f '{{.State.Running}}' btcadvisor-postgres-db 2>/dev/null)" != "true" ]; then
    echo "Fehler: Der Container 'btcadvisor-postgres-db' läuft nicht. Backup abgebrochen." >&2
    exit 1
fi

# Aktuelles Datum und Uhrzeit für den Dateinamen (kompatibel mit macOS und Linux)
BACKUP_DATE=$(date +%Y-%m-%d_%H-%M-%S)
BACKUP_FILE="backups/backup_${BACKUP_DATE}.sql"

# Backup erstellen und direkt komprimieren (plattformübergreifend)
docker exec btcadvisor-postgres-db pg_dump -U sa -d postgres -w | gzip > "${BACKUP_FILE}.gz"

# Prüfen, ob die Backup-Datei erstellt wurde und nicht leer ist
if [ ! -s "${BACKUP_FILE}.gz" ]; then
    echo "Fehler: Die Backup-Datei ist leer oder wurde nicht erstellt." >&2
    exit 1
fi

# Prüfen, ob die SCP-Übertragung erfolgreich war
SCP_SUCCESS=false
for i in {1..3}; do  # Maximal 3 Versuche
     if scp "${BACKUP_FILE}.gz" marc@192.168.178.38:/Volumes/T9/Backups/BtcAdvisor/; then
         SCP_SUCCESS=true
         break
     else
         echo "SCP-Versuch $i fehlgeschlagen. Warte 5 Sekunden..."
         sleep 5
     fi
done

if [ "$SCP_SUCCESS" = false ]; then
   echo "Fehler: SCP-Übertragung nach 3 Versuchen fehlgeschlagen." >&2
   exit 1
fi

# Auf dem Zielserver nur die letzten 3 Backups behalten
ssh marc@192.168.178.38 "
    cd /Volumes/T9/Backups/BtcAdvisor/ && \
    ls -t backup_*.sql.gz | tail -n +4 | xargs -I {} rm -f {}
"

# Optional: Ältere Backups lokal löschen (z. B. älter als 3 Tage)
find ./backups -type f -name "backup_*.sql.gz" -mtime +3 -delete

echo "Backup erfolgreich erstellt, komprimiert und übertragen: ${BACKUP_FILE}.gz"
