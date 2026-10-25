#!/usr/bin/env python3
"""Grid parameter sweep for DistributionTopFilter thresholds - Extended timeout."""

import requests
import json
import sys
from itertools import product
from datetime import datetime

BASE_URL = "http://localhost:6003"
START = "2024-09-08T18:00:00Z"
END = "2026-05-03T23:59:59Z"

MOMENTUM_RATIOS = [0.0, 0.20, 0.25, 0.35, 0.50]
TROUGH_DRAWDOWNS = [5.0, 8.0, 10.0, 15.0]
TROUGH_PROXIMITIES = [3.0, 5.0, 8.0, 12.0]

print("momentumBrakeMaxRatio,troughMinDrawdownPct,troughRecoveryProximityPct,totalProfitPct,buyAndHoldPct,delta,totalBuys,totalSells,winRatePct,maxLossPct,maxGainPct")
sys.stdout.flush()

count = 0
for ratio, drawdown, proximity in product(MOMENTUM_RATIOS, TROUGH_DRAWDOWNS, TROUGH_PROXIMITIES):
    count += 1
    params = {
        'startDate': START,
        'endDate': END,
        'persist': 'false',
        'momentumBrakeMaxRatio': ratio,
        'troughMinDrawdownPercent': drawdown,
        'troughRecoveryProximityPercent': proximity,
    }
    
    try:
        resp = requests.get(f"{BASE_URL}/Backtest", params=params, timeout=300)
        response = resp.text
        
        if not response or resp.status_code != 200:
            print(f"{ratio},{drawdown},{proximity},ERROR_{resp.status_code},ERROR_{resp.status_code},-999,0,0,0,0,0")
            sys.stdout.flush()
            continue
        
        try:
            data = json.loads(response)
            profit = data.get('totalProfitPercent')
            bah = data.get('buyAndHoldPercent')
            buys = data.get('totalBuys', 0)
            sells = data.get('totalSells', 0)
            winrate = data.get('winRatePercent')
            maxloss = data.get('maxLossPercent')
            maxgain = data.get('maxGainPercent')
            
            delta = round(profit - bah, 2) if profit is not None and bah is not None else -999
            print(f"{ratio},{drawdown},{proximity},{profit},{bah},{delta},{buys},{sells},{winrate},{maxloss},{maxgain}")
        except Exception as e:
            print(f"{ratio},{drawdown},{proximity},JSON_ERR,JSON_ERR,-999,0,0,0,0,0")
            
    except requests.Timeout:
        print(f"{ratio},{drawdown},{proximity},TIMEOUT_300S,TIMEOUT_300S,-999,0,0,0,0,0")
    except Exception as e:
        print(f"{ratio},{drawdown},{proximity},ERROR,ERROR,-999,0,0,0,0,0")
    
    sys.stdout.flush()
    if count % 8 == 0:
        print(f"# {datetime.now().strftime('%H:%M:%S')} {count}/80 ({count*100//80}%)", file=sys.stderr)
        sys.stderr.flush()

print(f"# DONE at {datetime.now().strftime('%H:%M:%S')}", file=sys.stderr)
