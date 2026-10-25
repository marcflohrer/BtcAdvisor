]633;E;echo '#!/usr/bin/env bash';34088c56-6770-4415-b761-6257e5124176]633;C#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
WORKER_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
ENV_FILE="$WORKER_DIR/.env"

if [[ $# -lt 1 ]]; then
  echo "Usage: $(basename "$0") \"your message\""
  exit 1
fi

MESSAGE="$*"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "Error: .env file not found at $ENV_FILE"
  exit 1
fi

# Read key=value from .env without executing arbitrary code.
read_env_value() {
  local key="$1"
  local line
  line=$(grep -E "^${key}=" "$ENV_FILE" | tail -n1 || true)
  if [[ -z "$line" ]]; then
    echo ""
    return 0
  fi

  local value="${line#*=}"
  value="${value%\"}"
  value="${value#\"}"
  echo "$value"
}

BOT_TOKEN="$(read_env_value "TELEGRAM_BOT_SECRET")"
CHAT_IDS_RAW="$(read_env_value "TELEGRAM_CHAT_IDS")"

if [[ -z "$BOT_TOKEN" ]]; then
  echo "Error: TELEGRAM_BOT_SECRET is missing in .env"
  exit 1
fi

if [[ -z "$CHAT_IDS_RAW" ]]; then
  echo "Error: TELEGRAM_CHAT_IDS is missing in .env"
  exit 1
fi

# Support comma/semicolon/space separated chat IDs.
CHAT_IDS_CLEAN="${CHAT_IDS_RAW//;/,}"
CHAT_IDS_CLEAN="${CHAT_IDS_CLEAN// /,}"
CHAT_IDS_CLEAN="${CHAT_IDS_CLEAN//\t/,}"

sent=0
failed=0

IFS=',' read -r -a CHAT_IDS <<< "$CHAT_IDS_CLEAN"
for chat_id in "${CHAT_IDS[@]}"; do
  chat_id="$(echo "$chat_id" | xargs)"
  if [[ -z "$chat_id" ]]; then
    continue
  fi

  http_code=$(curl -sS -o /tmp/telegram_send_response.json -w "%{http_code}" \
    -X POST "https://api.telegram.org/bot${BOT_TOKEN}/sendMessage" \
    -H "Content-Type: application/x-www-form-urlencoded" \
    --data-urlencode "chat_id=${chat_id}" \
    --data-urlencode "text=${MESSAGE}")

  if [[ "$http_code" == "200" ]]; then
    sent=$((sent + 1))
    echo "Sent to chat_id=${chat_id}"
  else
    failed=$((failed + 1))
    echo "Failed for chat_id=${chat_id} (HTTP ${http_code})"
  fi
done

echo "Done. sent=${sent}, failed=${failed}"

if [[ $sent -eq 0 ]]; then
  exit 1
fi
