run_mode() {
  local name=$1
  local adaptiveMode=$2
  local topEnabled=$3
  local settings_file="BtcAdvisor/appsettings.json"

  echo "--- Running mode: $name ---"
  
  # 1) Update appsettings.json
  # Using a temporary file because jq doesn't support in-place editing as easily without more tools
  jq ".BearMarketFilter.AthDrawdownAdaptiveThresholdEnabled = true |
      .BearMarketFilter.AthDrawdownAdaptiveMode = \"$adaptiveMode\" |
      .BearMarketFilter.CycleTopSellZoneEnabled = $topEnabled |
      .BearMarketFilter.CycleTopSellZoneInterval = \"1d\" |
      .BearMarketFilter.CycleTopSellZoneLookbackDays = 1460 |
      .BearMarketFilter.CycleTopSellZoneMinDrawupPercent = 350.0 |
      .BearMarketFilter.CycleTopSellZoneMinAthProximityPercent = 90.0" "$settings_file" > "${settings_file}.tmp" && mv "${settings_file}.tmp" "$settings_file"

  # 2) Rebuild/restart web service
  (cd BtcAdvisor && docker compose up -d --build web) > /dev/null 2>&1
  
  # Wait a bit for the service to be healthy (simple sleep for brevity in this script)
  sleep 5

  # 3) Call backtest endpoint
  local start_date="2017-01-01"
  local end_date="2026-01-01"
  local url="http://localhost:6003/Backtest?startDate=${start_date}&endDate=${end_date}&persist=false"
  
  curl -s -X POST "$url" -o "BtcAdvisor/result_${name}.json"

  # 4) Print metrics
  if [[ -f "BtcAdvisor/result_${name}.json" ]]; then
    jq -r '
      def prod(s): reduce s as $i (1; . * $i);
      . | 
      "buys: \(.buys | length), sells: \(.sells | length), totalProfitPercent: \(.totalProfitPercent)%, winRatePercent: \(.winRatePercent)%, maxLossPercent: \(.maxLossPercent)%, maxGainPercent: \(.maxGainPercent)%",
      "compounded: \(((prod(.sells[].profitPercent / 100 + 1) - 1) * 100) | round)%",
      "Worst 5 trades:",
      (.sells | sort_by(.profitPercent) | .[:5] | .[] | "\(.timestamp)|\(.advice)|\(.profitPercent)%"),
      "Best 5 trades:",
      (.sells | sort_by(.profitPercent) | reverse | .[:5] | .[] | "\(.timestamp)|\(.advice)|\(.profitPercent)%")
    ' "BtcAdvisor/result_${name}.json"
  else
    echo "Error: Result file for $name not found."
  fi
  echo ""
}

run_mode "prevmedian_no_top" "PreviousCycleMinusMedianDelta" false
run_mode "prevmedian_with_top" "PreviousCycleMinusMedianDelta" true
run_mode "formula_with_top" "FormulaByCycle" true
