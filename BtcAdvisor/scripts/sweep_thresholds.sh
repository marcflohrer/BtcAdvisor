#!/usr/bin/env bash
# Threshold sweep for DistributionTopFilter parameters.
# Runs the backtest for each parameter combination and prints a ranked CSV.
# Usage: bash scripts/sweep_thresholds.sh [baseUrl] [startDate] [endDate]

BASE_URL="http://localhost:6003"
START="${2:-2024-09-08T18:00:00Z}"
END="${3:-2026-05-03T23:59:59Z}"

MOMENTUM_RATIOS=(0.0 0.20 0.25 0.35 0.50)
TROUGH_DRAWDOWNS=(5.0 8.0 10.0 15.0)
TROUGH_PROXIMITIES=(3.0 5.0 8.0 12.0)

echo "momentumBrakeMaxRatio,troughMinDrawdownPct,troughRecoveryProximityPct,totalProfitPct,buyAndHoldPct,delta,totalBuys,totalSells,winRatePct,maxLossPct,maxGainPct"

for ratio in "${MOMENTUM_RATIOS[@]}"; do
  for drawdown in "${TROUGH_DRAWDOWNS[@]}"; do
    for proximity in "${TROUGH_PROXIMITIES[@]}"; do
      response=$(curl -sS -G "${BASE_URL}/Backtest" \
      url="${BASE_URL}/Backtest?startDate=${START}&endDate=${END}&persist=false&momentumBrakeMaxRatio=${ratio}&troughMinDrawdownPercent=${drawdown}&troughRecoveryProximityPercent=${proximity}"
      response=$(curl -sS --max-time 180 "$url" 2>&1)

      if [ -z "$response" ] || echo "$response" | grep -q '"error"'; then
        echo "${ratio},${drawdown},${proximity},ERROR,ERROR,-999,0,0,0,0,0"
        continue
      fi

      profit=$(echo "$response" | grep -o '"totalProfitPercent":[^,}]*' | cut -d: -f2)
      bah=$(echo "$response" | grep -o '"buyAndHoldPercent":[^,}]*' | cut -d: -f2)
      buys=$(echo "$response" | grep -o '"totalBuys":[^,}]*' | cut -d: -f2)
      sells=$(echo "$response" | grep -o '"totalSells":[^,}]*' | cut -d: -f2)
      winrate=$(echo "$response" | grep -o '"winRatePercent":[^,}]*' | cut -d: -f2)
      maxloss=$(echo "$response" | grep -o '"maxLossPercent":[^,}]*' | cut -d: -f2)
      maxgain=$(echo "$response" | grep -o '"maxGainPercent":[^,}]*' | cut -d: -f2)

      [ -n "$profit" ] && [ -n "$bah" ] && delta=$(awk "BEGIN {printf \"%.2f\", $profit - $bah}") || delta="-999"

      echo "${ratio},${drawdown},${proximity},${profit},${bah},${delta},${buys},${sells},${winrate},${maxloss},${maxgain}"
    done
  done
done
