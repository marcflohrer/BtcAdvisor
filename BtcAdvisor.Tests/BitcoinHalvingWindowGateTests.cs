using BtcAdvisor.Constants;

namespace BtcAdvisor.Tests;

/// <summary>
/// Tests for <see cref="BitcoinConstants.IsWithinPostHalvingWindow"/>, the
/// post-halving time gate used by the CycleTop sell zone to suppress
/// premature DistributionSell triggers outside the historical 12–18 month
/// post-halving cycle-top window.
/// </summary>
[TestFixture]
public class BitcoinHalvingWindowGateTests
{
    // Known halvings (UTC):
    // 2012-11-28, 2016-07-09, 2020-05-11, 2024-04-19

    [Test]
    public void IsWithinPostHalvingWindow_BeforeAnyHalving_FailsOpenTrue()
    {
        // No halving exists prior to 2010 → gate is fail-open (true).
        var asOf = new DateTime(2010, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(asOf, 12, 24), Is.True);
    }

    [Test]
    public void IsWithinPostHalvingWindow_BothBoundsZero_AlwaysTrue()
    {
        // Gate disabled when both bounds are 0.
        var asOf = new DateTime(2024, 3, 14, 0, 0, 0, DateTimeKind.Utc); // 1 month BEFORE 2024 halving
        Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(asOf, 0, 0), Is.True);
    }

    [Test]
    public void IsWithinPostHalvingWindow_March2024_BlockedByMin12()
    {
        // 2024-03-14: 46 months past 2020-05-11 halving → outside [12, 24].
        // This is the March 2024 false-top scenario the gate is designed to suppress.
        var asOf = new DateTime(2024, 3, 14, 0, 0, 0, DateTimeKind.Utc);
        Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(asOf, 12, 24), Is.False);
    }

    [Test]
    public void IsWithinPostHalvingWindow_OneMonthAfterHalving_BlockedByMin()
    {
        // 2024-05-19: ~1 month after 2024-04-19 halving → below min=12.
        var asOf = new DateTime(2024, 5, 19, 12, 0, 0, DateTimeKind.Utc);
        Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(asOf, 12, 24), Is.False);
    }

    [Test]
    public void IsWithinPostHalvingWindow_15MonthsAfterHalving_Allowed()
    {
        // 2025-07-19: ~15 months after 2024-04-19 halving → inside [12, 24].
        var asOf = new DateTime(2025, 7, 19, 12, 0, 0, DateTimeKind.Utc);
        Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(asOf, 12, 24), Is.True);
    }

    [Test]
    public void IsWithinPostHalvingWindow_36MonthsAfterHalving_BlockedByMax()
    {
        // 2027-04-19: 36 months past 2024 halving → above max=24.
        var asOf = new DateTime(2027, 4, 19, 12, 0, 0, DateTimeKind.Utc);
        Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(asOf, 12, 24), Is.False);
    }

    [Test]
    public void IsWithinPostHalvingWindow_OnlyMaxBound_LowerSideAlwaysAllowed()
    {
        // Min=0 disables lower bound; date just after halving is allowed.
        var asOf = new DateTime(2024, 5, 1, 0, 0, 0, DateTimeKind.Utc);
        Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(asOf, 0, 24), Is.True);
    }

    [Test]
    public void IsWithinPostHalvingWindow_OnlyMinBound_UpperSideAlwaysAllowed()
    {
        // Max=0 disables upper bound; 36 months post-halving still allowed.
        var asOf = new DateTime(2027, 4, 19, 12, 0, 0, DateTimeKind.Utc);
        Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(asOf, 12, 0), Is.True);
    }

    [Test]
    public void IsWithinPostHalvingWindow_HistoricalCycleTops_AllAllowed()
    {
        // Historical cycle tops fell inside [12, 18] months post-halving.
        var top2013 = new DateTime(2013, 12, 4,  0, 0, 0, DateTimeKind.Utc); // ~12 m after 2012-11-28
        var top2017 = new DateTime(2017, 12, 17, 0, 0, 0, DateTimeKind.Utc); // ~17 m after 2016-07-09
        var top2021 = new DateTime(2021, 11, 10, 0, 0, 0, DateTimeKind.Utc); // ~18 m after 2020-05-11
        using (Assert.EnterMultipleScope())
        {
            Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(top2013, 12, 24), Is.True);
            Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(top2017, 12, 24), Is.True);
            Assert.That(BitcoinConstants.IsWithinPostHalvingWindow(top2021, 12, 24), Is.True);
        }
    }
}
