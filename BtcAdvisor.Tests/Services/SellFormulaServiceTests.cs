using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests.Services;

/// <summary>
/// Regression tests for SellFormulaService bugs:
/// - Bug: twoHourSellCount > 3 was used as a knockout (blocked sell when 2h signal was strong)
///   Fix: twoHourSellCount &lt; 1 (block sell when NO 2h signal is present)
/// - Bug: operator precedence in knockout block was ambiguous (parentheses added for clarity)
/// </summary>
[TestFixture]
public class SellFormulaServiceTests
{
    // Stats yielding signature 1 (mapped 2h: sell=4, buy=0) with enough weight to pass the formula threshold.
    // 2h sell=4 with strong trend → sell score ≈ 202 billion >> threshold 720135627
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetSignature1OscillatorStatistics(int twoHourSellCount) => new()
    {
        { "2h",  BuildStats(sellCount: 4, buyCount: 0, isStrongTrend: true) },
        { "4h",  BuildStats(sellCount: 3, buyCount: 0, isStrongTrend: true) },
        { "12h", BuildStats(sellCount: 3, buyCount: 0, isStrongTrend: true) },
        { "1d",  BuildStats(sellCount: 2, buyCount: 0, isStrongTrend: true) },
        { "1w",  BuildStats(sellCount: twoHourSellCount, buyCount: 0, isStrongTrend: true) },
    };

    // MA stats: sell=7 per timeframe ensures all > 48 (formula precondition)
    private static Dictionary<string, BitcoinTimeRangeStatistics> GetMovingAverageStatistics() => new()
    {
        { "2h",  BuildStats(sellCount: 7, buyCount: 0) },
        { "4h",  BuildStats(sellCount: 7, buyCount: 0) },
        { "12h", BuildStats(sellCount: 7, buyCount: 0) },
        { "1d",  BuildStats(sellCount: 7, buyCount: 0) },
        { "1w",  BuildStats(sellCount: 7, buyCount: 0) },
    };

    /// <summary>
    /// Regression: twoHourSellCount = 0 must block the sell signal (knockout rule: twoHourSellCount &lt; 1).
    /// Before fix: twoHourSellCount > 3 was used, so 0 would NOT trigger the knockout.
    /// </summary>
    [Test]
    public void IsSellAdvice_WhenTwoHourSellCountIsZero_ShouldReturnFalse()
    {
        var oscillatorStatistics = GetSignature1OscillatorStatistics(twoHourSellCount: 0);
        var movingAverageStatistics = GetMovingAverageStatistics();

        var result = SellFormulaService.IsSellAdvice(oscillatorStatistics, movingAverageStatistics);

        Assert.That(result, Is.False, "Sell must be blocked when there is no 2h sell signal.");
    }

    /// <summary>
    /// Regression: twoHourSellCount >= 1 must NOT be knocked out.
    /// Before fix: twoHourSellCount > 3 knockout would have blocked sell when 2h had 4+ sell signals.
    /// </summary>
    [Test]
    public void IsSellAdvice_WhenTwoHourSellCountIsOne_WithSignature_ShouldReturnTrue()
    {
        var oscillatorStatistics = GetSignature1OscillatorStatistics(twoHourSellCount: 1);
        var movingAverageStatistics = GetMovingAverageStatistics();

        var result = SellFormulaService.IsSellAdvice(oscillatorStatistics, movingAverageStatistics);

        Assert.That(result, Is.True, "Sell must fire when 2h has at least 1 sell signal and signature matches.");
    }

    /// <summary>
    /// Regression: twoHourSellCount = 4 (previously would have been knocked out by > 3 bug) must fire.
    /// </summary>
    [Test]
    public void IsSellAdvice_WhenTwoHourSellCountIsFour_WithSignature_ShouldReturnTrue()
    {
        var oscillatorStatistics = GetSignature1OscillatorStatistics(twoHourSellCount: 4);
        var movingAverageStatistics = GetMovingAverageStatistics();

        var result = SellFormulaService.IsSellAdvice(oscillatorStatistics, movingAverageStatistics);

        Assert.That(result, Is.True, "Sell must fire even when 2h has 4 sell signals (was incorrectly knocked out before fix).");
    }

    /// <summary>
    /// Without a matching signature, sell must be blocked regardless of twoHourSellCount.
    /// </summary>
    [Test]
    public void IsSellAdvice_WhenNoSignatureMatches_ShouldReturnFalse()
    {
        // mapped 2h sell=1 (not 4) → no signature
        var oscillatorStatistics = new Dictionary<string, BitcoinTimeRangeStatistics>
        {
            { "2h",  BuildStats(sellCount: 1, buyCount: 0, isStrongTrend: true) },
            { "4h",  BuildStats(sellCount: 3, buyCount: 0, isStrongTrend: true) },
            { "12h", BuildStats(sellCount: 3, buyCount: 0, isStrongTrend: true) },
            { "1d",  BuildStats(sellCount: 2, buyCount: 0, isStrongTrend: true) },
            { "1w",  BuildStats(sellCount: 2, buyCount: 0, isStrongTrend: true) },
        };
        var movingAverageStatistics = GetMovingAverageStatistics();

        var result = SellFormulaService.IsSellAdvice(oscillatorStatistics, movingAverageStatistics);

        Assert.That(result, Is.False, "Sell must be blocked when no signature pattern matches.");
    }

    private static BitcoinTimeRangeStatistics BuildStats(int sellCount, int buyCount, bool isStrongTrend = false)
        => new BitcoinTimeRangeStatistics
        {
            IndicatorSellCount = sellCount,
            IndicatorBuyCount = buyCount,
            IndicatorStrongTrendCount = isStrongTrend ? 1 : 0,
        };
}
