#!/bin/bash
# BtcAdvisor Watchdog - prüft Gesundheit und startet bei Bedarf neu
# Wird via crontab aufgerufen (z.B. alle 5 Minuten)

set -euo pipefail

# --- Konfiguration ---
PROJECT_DIR="/Volumes/T9/Dokumente/GitHub/BtcAdvisor/BtcAdvisor"
LOG_FILE="${PROJECT_DIR}/data/logs/watchdog.log"
LOCK_FILE="/tmp/btcadvisor-watchdog.lock"
HEALTH_URL="http://localhost:6777/health"
START_SCRIPT="${PROJECT_DIR}/start.sh"

# PATH für macOS cron (Docker Desktop installiert docker oft hier)
export PATH="/usr/local/bin:/usr/bin:/bin:/usr/sbin:/sbin:/opt/homebrew/bin:$HOME/.docker/bin:${PATH}"

# --- Hilfsfunktionen ---
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "${LOG_FILE}"
}

cleanup() {
    rm -f "${LOCK_FILE}"
}
trap cleanup EXIT

# --- Hauptlogik ---
mkdir -p "$(dirname "${LOG_FILE}")"

# Verhindere parallele Ausführung
if [ -f "${LOCK_FILE}" ]; then
    log "Watchdog läuft bereits - überspringe diesen Durchlauf"
    exit 0
fi
echo $$ > "${LOCK_FILE}"

# 1. Prüfe ob Docker läuft
if ! docker info >/dev/null 2>&1; then
    log "FEHLER: Docker-Daemon nicht erreichbar"
    exit 1
fi

cd "${PROJECT_DIR}"

# 2. Prüfe ob beide Container laufen
WORKER_RUNNING=$(docker inspect -f '{{.State.Running}}' btcadvisor-worker 2>/dev/null || echo "false")
DB_RUNNING=$(docker inspect -f '{{.State.Running}}' btcadvisor-postgres-db 2>/dev/null || echo "false")

if [ "${WORKER_RUNNING}" != "true" ] || [ "${DB_RUNNING}" != "true" ]; then
    log "FEHLER: Container nicht laufen (worker=${WORKER_RUNNING}, db=${DB_RUNNING}) - starte neu..."
    bash "${START_SCRIPT}" >> "${LOG_FILE}" 2>&1
    log "Neustart abgeschlossen"
    exit 0
fi

# 3. Prüfe Container-Health-Status
WORKER_HEALTH=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' btcadvisor-worker 2>/dev/null || echo "unknown")
DB_HEALTH=$(docker inspect -f '{{if .State.Health}}{{.State.Health.Status}}{{else}}none{{end}}' btcadvisor-postgres-db 2>/dev/null || echo "unknown")

if [ "${DB_HEALTH}" = "unhealthy" ]; then
    log "FEHLER: Datenbank-Container unhealthy - starte neu..."
    bash "${START_SCRIPT}" >> "${LOG_FILE}" 2>&1
    log "Neustart abgeschlossen"
    exit 0
fi

# 4. Prüfe HTTP Health-Endpoint
HTTP_CODE=$(curl -s -o /dev/null -w "%{http_code}" --max-time 10 "${HEALTH_URL}" 2>/dev/null || echo "000")

if [ "${HTTP_CODE}" != "200" ]; then
    log "FEHLER: Health-Endpoint antwortet nicht korrekt (HTTP ${HTTP_CODE}) - starte neu..."
    bash "${START_SCRIPT}" >> "${LOG_FILE}" 2>&1
    log "Neustart abgeschlossen"
    exit 0
fi

# Alles gesund
log "OK: Alle Prüfungen bestanden (worker=healthy, db=${DB_HEALTH}, http=${HTTP_CODE})"
exit 0
