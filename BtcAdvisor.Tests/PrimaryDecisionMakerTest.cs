using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests;

/// <summary>
/// Integration tests for <see cref="PrimaryDecisionMaker"/> in LTF-only mode.
/// All test cases use long timeframe oscillator statistics (2h/4h/12h/1d/1w).
/// Legacy short-timeframe conditions (5m–2h) have been removed from the decision pipeline.
/// </summary>
[TestFixture]
public class PrimaryDecisionMakerTest
{
    [SetUp]
    public void Setup() { }

    // ── LTF Buy: strong bullish confluence across all 5 LTF ranges ──────────────
    // IsAligned (new): buy > sell (no per-range trend gate)
    // alignedRanges=5 ≥4, alignedAnchorRanges=2 ≥2, conflictingAnchorRanges=0
    // trendCountSum=5 ≥3 → LongTimeframeMultiTrendBullish fires
    [TestCase(
        /*osci 2h*/  4,0,1,0,  /*osci 4h*/  4,0,1,0,  /*osci 12h*/ 3,0,1,0,
        /*osci 1d*/  4,0,0,1,  /*osci 1w*/  4,0,0,1,
        "Buy", TestName = "LTF-Buy: MultiTrend confluence all 5 ranges")]

    // ── LTF Buy: anchor conflict blocks confluence, but linear score (43) > 18 ──
    // alignedRanges=4 ≥4, alignedAnchorRanges=1 <2 → IsBullish=false (1w aligned but 1d conflicts)
    // LongTimeframeLinearBullish fires (threshold=18.0)
    [TestCase(
        /*osci 2h*/  5,0,2,0,  /*osci 4h*/  5,0,2,0,  /*osci 12h*/ 5,0,2,0,
        /*osci 1d*/  0,5,1,0,  /*osci 1w*/  5,0,0,0,
        "Buy", TestName = "LTF-Buy: Linear only, anchor conflict on 1d")]

    // ── LTF Sell: strong bearish confluence across all 5 LTF ranges ─────────────
    // alignedRanges=5 ≥3, alignedAnchorRanges=2 ≥0, conflictingAnchorRanges=0, trendCountSum=5 ≥2
    // IsBearish=true, trendCountSum=5 ≥3 → LongTimeframeMultiTrendBearish fires
    [TestCase(
        /*osci 2h*/  0,4,1,0,  /*osci 4h*/  0,4,1,0,  /*osci 12h*/ 0,3,1,0,
        /*osci 1d*/  0,4,0,1,  /*osci 1w*/  0,4,0,1,
        "Sell", TestName = "LTF-Sell: MultiTrend confluence all 5 ranges")]

    // ── LTF Sell: bullish anchor blocks bearish confluence, linear score (43) > 20
    // 1d bullish → conflictingAnchorRanges=1 (bearish view) > 0 → IsBearish=false
    // LongTimeframeLinearBearish fires (threshold=20.0)
    [TestCase(
        /*osci 2h*/  0,5,2,0,  /*osci 4h*/  0,5,2,0,  /*osci 12h*/ 0,5,2,0,
        /*osci 1d*/  5,0,1,0,  /*osci 1w*/  0,5,0,0,
        "Sell", TestName = "LTF-Sell: Linear only, anchor conflict on 1d")]

    // ── Wait: neutral LTF data — buy=sell, no trend ──────────────────────────────
    // alignedRanges=0, linearBullishScore=7.5 <18, linearBearishScore=7.5 <20
    [TestCase(
        /*osci 2h*/  3,3,0,0,  /*osci 4h*/  3,3,0,0,  /*osci 12h*/ 3,3,0,0,
        /*osci 1d*/  3,3,0,0,  /*osci 1w*/  3,3,0,0,
        "Wait", TestName = "LTF-Wait: neutral equal buy/sell, no trend")]

    // ── Wait: LTF data present, mixed signals block all conditions ───────────────
    // Bullish: 3 non-anchor ranges aligned (2h/4h/12h), but alignedRanges=3 <4, anchors=0 <2 → IsBullish=false
    // Bearish: 2 anchor ranges (1d/1w) aligned bearish, alignedRanges=2 <3 → IsBearish=false
    // linearBullishScore=(2*6)−(1.5*6)+(1.5*2) = 12−9+3 = 6 < 18
    // linearBearishScore=6 < 20 → Wait
    [TestCase(
        /*osci 2h*/  2,0,0,0,  /*osci 4h*/  2,0,0,0,  /*osci 12h*/ 2,0,0,0,
        /*osci 1d*/  0,3,1,0,  /*osci 1w*/  0,3,1,0,
        "Wait", TestName = "LTF-Wait: bullish non-anchors, bearish anchors cancel out")]
    public void EvaluateTradeAdvice_LtfOnly_ShouldReturnCorrectAdvice(
        int osciBuy2h,  int osciSell2h,  int osciStrong2h,  int osciMedium2h,
        int osciBuy4h,  int osciSell4h,  int osciStrong4h,  int osciMedium4h,
        int osciBuy12h, int osciSell12h, int osciStrong12h, int osciMedium12h,
        int osciBuy1d,  int osciSell1d,  int osciStrong1d,  int osciMedium1d,
        int osciBuy1w,  int osciSell1w,  int osciStrong1w,  int osciMedium1w,
        string expectedAdvice)
    {
        var osciStatistics = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "2h",  Ltf(2.0,   osciBuy2h,  osciSell2h,  osciStrong2h,  osciMedium2h)  },
            { "4h",  Ltf(4.0,   osciBuy4h,  osciSell4h,  osciStrong4h,  osciMedium4h)  },
            { "12h", Ltf(12.0,  osciBuy12h, osciSell12h, osciStrong12h, osciMedium12h) },
            { "1d",  Ltf(24.0,  osciBuy1d,  osciSell1d,  osciStrong1d,  osciMedium1d)  },
            { "1w",  Ltf(168.0, osciBuy1w,  osciSell1w,  osciStrong1w,  osciMedium1w)  },
        };
        var movingAverageStatistics = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "2h",  Ltf(2.0,   0, 0, 0, 0) },
            { "4h",  Ltf(4.0,   0, 0, 0, 0) },
            { "12h", Ltf(12.0,  0, 0, 0, 0) },
            { "1d",  Ltf(24.0,  0, 0, 0, 0) },
            { "1w",  Ltf(168.0, 0, 0, 0, 0) },
        };

        var result = PrimaryDecisionMaker.EvaluateTradeAdvice(
            currentPrice: 60000,
            osciStatistics: osciStatistics,
            movingAverageStatistics: movingAverageStatistics,
            debugString: "test",
            maxDateTime: new DateTime(2024, 1, 1),
            minDateTime: new DateTime(2024, 1, 1).AddDays(-1),
            latestBuyPrice: 60000m,
            latestSellPrice: 60000m,
            microstrategyHighPrice: null,
            microstrategyLowPrice: null,
            lowestBuyAfterFireSell: null,
            highestPanicBuyAfterLowestBuyAfterFireSell: null);

        Assert.That(result.TradeAdvice, Is.EqualTo(expectedAdvice));
    }

    private static BitcoinTimeRangeStatistics Ltf(
        double timespanHours, int buy, int sell, int strongTrend, int mediumTrend) => new()
    {
        TimespanHours = timespanHours,
        IndicatorBuyCount = buy,
        IndicatorSellCount = sell,
        IndicatorStrongTrendCount = strongTrend,
        IndicatorMediumTrendCount = mediumTrend,
        IndicatorWeakTrendCount = 0,
        IndicatorNoTrendCount = 0,
        IndicatorNeutralCount = 0,
        IndicatorNotFound = 0,
        IsOscillatorIndicatorGroup = true,
    };
}
