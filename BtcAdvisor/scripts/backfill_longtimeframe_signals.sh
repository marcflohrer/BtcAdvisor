#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:6003}"
START_DATE="${1:-2017-08-17T00:00:00Z}"
END_DATE="${2:-$(date -u +%Y-%m-%dT%H:%M:%SZ)}"
CHUNK_DAYS="${CHUNK_DAYS:-14}"
RETRIES_PER_CHUNK="${RETRIES_PER_CHUNK:-4}"
SLEEP_BETWEEN_CHUNKS_SECONDS="${SLEEP_BETWEEN_CHUNKS_SECONDS:-1}"

python3 - "$BASE_URL" "$START_DATE" "$END_DATE" "$CHUNK_DAYS" "$RETRIES_PER_CHUNK" "$SLEEP_BETWEEN_CHUNKS_SECONDS" <<'PY'
import datetime as dt
import sys
import time
import urllib.parse
import urllib.request

base_url, start_date, end_date, chunk_days, retries_per_chunk, sleep_between_chunks_seconds = sys.argv[1:7]
chunk_days_int = int(chunk_days)
retries_per_chunk_int = int(retries_per_chunk)
sleep_between_chunks_seconds_int = int(sleep_between_chunks_seconds)

def parse_utc(value: str) -> dt.datetime:
    return dt.datetime.fromisoformat(value.replace("Z", "+00:00")).astimezone(dt.timezone.utc)

def fmt_utc(value: dt.datetime) -> str:
    return value.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")

current_start = parse_utc(start_date)
final_end = parse_utc(end_date)

print("Starting long-timeframe signal backfill")
print(f"Base URL: {base_url}")
print(f"Range: {fmt_utc(current_start)} -> {fmt_utc(final_end)}")
print(f"Chunk size (days): {chunk_days_int}")
print(f"Retries per chunk: {retries_per_chunk_int}")

while current_start < final_end:
    next_end = min(current_start + dt.timedelta(days=chunk_days_int), final_end)
    start_str = fmt_utc(current_start)
    end_str = fmt_utc(next_end)
    print(f"Backfilling chunk: {start_str} -> {end_str}")

    query = urllib.parse.urlencode({"startDate": start_str, "endDate": end_str})
    url = f"{base_url}/rerun?{query}"
    req = urllib.request.Request(url, method="POST")

    success = False
    for attempt in range(1, retries_per_chunk_int + 1):
        try:
            with urllib.request.urlopen(req, timeout=3600) as response:
                body = response.read().decode("utf-8", errors="replace")
                print(f"Chunk done: {body}")
                success = True
                break
        except urllib.error.HTTPError as exc:
            error_body = exc.read().decode("utf-8", errors="replace")
            print(f"Chunk failed ({exc.code}) attempt {attempt}/{retries_per_chunk_int}: {error_body}")
            if attempt == retries_per_chunk_int:
                sys.exit(1)
            time.sleep(2)
        except Exception as exc:  # noqa: BLE001
            print(f"Chunk failed (transport) attempt {attempt}/{retries_per_chunk_int}: {exc}")
            if attempt == retries_per_chunk_int:
                sys.exit(1)
            time.sleep(2)

    if not success:
        sys.exit(1)

    current_start = next_end
    if sleep_between_chunks_seconds_int > 0:
        time.sleep(sleep_between_chunks_seconds_int)

print("Backfill finished.")
PY
