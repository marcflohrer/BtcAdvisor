#!/usr/bin/env bash
set -euo pipefail

# --- Config (UTC) ---
FROM_UTC_ISO="2025-05-12T11:00:00Z"
TO_UTC_ISO="2025-05-12T16:00:00Z"

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
    echo "WARN: .env nicht gefunden."
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
# MSTR (Yahoo Finance, 15m)
# Hinweis: Yahoo liefert 15m-Kerzen nur für die letzten ~60 Tage.
# Für ältere Zeiträume muss interval=1d verwendet werden.
# =========================
YAHOO_BASE="https://query1.finance.yahoo.com"
FROM_UNIX=$(( $(iso_to_ms "${FROM_UTC_ISO}") / 1000 ))
TO_UNIX=$(( $(iso_to_ms "${TO_UTC_ISO}") / 1000 ))

echo "[*] Hole MSTR 15m von ${FROM_UTC_ISO} bis ${TO_UTC_ISO} (UTC) von Yahoo Finance…"
curl -fsSL -H "User-Agent: Mozilla/5.0 (compatible; BtcAdvisor/1.0)" \
  "${YAHOO_BASE}/v8/finance/chart/MSTR?interval=15m&period1=${FROM_UNIX}&period2=${TO_UNIX}&includePrePost=false" \
| jq -r '
  .chart.result[0] as $r |
  [range(0; ($r.timestamp|length)) as $i |
    select($r.timestamp[$i] != null and $r.indicators.quote[0].close[$i] != null) |
    {
      t: $r.timestamp[$i],
      o: $r.indicators.quote[0].open[$i],
      h: $r.indicators.quote[0].high[$i],
      l: $r.indicators.quote[0].low[$i],
      c: $r.indicators.quote[0].close[$i],
      v: ($r.indicators.quote[0].volume[$i] // 0),
      openTime: ($r.timestamp[$i] * 1000),
      openTimeIso: ($r.timestamp[$i] | todateiso8601),
      symbol: "MSTR", interval: "15m", source: "yahoo"
    }
  ]' > "$MSTR_FILE"

echo "[+] Gespeichert: ${MSTR_FILE}"
echo "[✓] Fertig."
