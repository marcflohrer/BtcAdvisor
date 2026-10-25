#!/usr/bin/env bash
set -euo pipefail

# --- Config (UTC) ---
FROM_UTC_ISO="2025-05-12T11:00:00Z"
TO_UTC_ISO="2025-05-12T16:00:00Z"

FROM_ET_ISO="2025-05-12T05:00:00-04:00"  # = 11:00:00Z
TO_ET_ISO="2025-07-18T12:00:00-04:00"    # = 16:00:00Z

OUT_DIR="testdata"
BTC_FILE="${OUT_DIR}/btcusdt_2025-05-12_11-00_utc.json"
MSTR_FILE="${OUT_DIR}/mstr_2025-05-12_11-00_utc.json"

# --- .env laden ---
load_env() {
  if [[ -f .env ]]; then
    set -a
    # shellcheck disable=SC1091
    . ./.env
    set +a
  else
    echo "WARN: .env nicht gefunden – MARKETDATA_APP_APIKEY evtl. leer."
  fi
}

# --- ISO -> ms (GNU date oder gdate) ---
date_cmd="date"
if ! $date_cmd --version >/dev/null 2>&1; then
  if command -v gdate >/dev/null 2>&1; then
    date_cmd="gdate"
  fi
fi
iso_to_ms() {
  local s
  s=$($date_cmd -u -d "$1" +%s)
  echo $(( s * 1000 ))
}

# --- Start ---
load_env
mkdir -p "$OUT_DIR"

# =========================
# BTCUSDT (Binance, 15m)
# =========================
BINANCE_BASE="https://api.binance.com"
START_MS=$(iso_to_ms "$FROM_UTC_ISO")
END_MS=$(iso_to_ms "$TO_UTC_ISO")

echo "[*] Hole BTCUSDT 15m von ${FROM_UTC_ISO} bis ${TO_UTC_ISO} (UTC) von Binance…"
curl -fsSL "${BINANCE_BASE}/api/v3/klines?symbol=BTCUSDT&interval=15m&startTime=${START_MS}&endTime=${END_MS}" \
| jq 'map({
    openTime: .[0],
    open: .[1],
    high: .[2],
    low: .[3],
    close: .[4],
    volume: .[5],
    closeTime: .[6],
    quoteAssetVolume: .[7],
    numberOfTrades: .[8],
    takerBuyBaseAssetVolume: .[9],
    takerBuyQuoteAssetVolume: .[10],
    openTimeIso: ((.[0]/1000)|todateiso8601),
    closeTimeIso: ((.[6]/1000)|todateiso8601),
    symbol: "BTCUSDT",
    interval: "15m",
    exchange: "BINANCE"
  })' > "$BTC_FILE"
echo "[+] Gespeichert: ${BTC_FILE}"

# =========================
# MSTR (marketdata.app, 15m)
# =========================
if [[ -z "${MARKETDATA_APP_APIKEY:-}" ]]; then
  echo "FEHLER: MARKETDATA_APP_APIKEY ist nicht gesetzt (siehe .env)."
  exit 1
fi

MDA_BASE="https://api.marketdata.app"
FROM_URLENC="${FROM_UTC_ISO}"
TO_URLENC="${TO_UTC_ISO}"

echo "[*] Hole MSTR 15m von ${FROM_UTC_ISO} bis ${TO_UTC_ISO} (UTC) von marketdata.app…"
TMP_RESP="$(mktemp)"
trap 'rm -f "$TMP_RESP"' EXIT

echo "[*] Hole MSTR 15m von ${FROM_ET_ISO} bis ${TO_ET_ISO} (ET/EDT) von marketdata.app…"
curl -fsSL -H "Authorization: Bearer ${MARKETDATA_APP_APIKEY}" \
  "https://api.marketdata.app/v1/stocks/candles/15/MSTR/?from=${FROM_ET_ISO}&to=${TO_ET_ISO}&extended=false" \
| jq -r '
  [range(0; (.t|length)) as $i |
    {
      t: .t[$i], o: .o[$i], h: .h[$i], l: .l[$i], c: .c[$i], v: .v[$i],
      openTime: (.t[$i] * 1000),
      openTimeIso: ((.t[$i]) | todateiso8601),
      symbol: "MSTR", interval: "15m", source: "marketdata.app"
    }
  ]' > "$MSTR_FILE"

echo "[+] Gespeichert: ${MSTR_FILE}"
echo "[✓] Fertig."
