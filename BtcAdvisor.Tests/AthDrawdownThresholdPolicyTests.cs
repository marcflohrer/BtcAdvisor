using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class AthDrawdownThresholdPolicyTests
{
    [Test]
    [TestCase(2011, 1)]
    [TestCase(2012, 1)]
    [TestCase(2013, 2)]
    [TestCase(2017, 3)]
    [TestCase(2021, 4)]
    [TestCase(2026, 5)]
    [TestCase(2032, 5)]
    public void ResolveCycleNumber_MapsConfiguredCycleStarts(int year, int expectedCycle)
    {
        var cycle = AthDrawdownThresholdPolicy.ResolveCycleNumber(
            new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            [2011, 2013, 2017, 2021, 2026]);

        Assert.That(cycle, Is.EqualTo(expectedCycle));
    }

    [Test]
    public void ResolveOverrideThresholdPercent_WhenAdaptiveEnabled_UsesFormula100Minus7TimesCycle()
    {
        var options = new BearMarketFilterOptions
        {
            AthDrawdownAdaptiveThresholdEnabled = true,
            AthDrawdownAdaptiveBasePercent = 100.0,
            AthDrawdownAdaptiveStepPercent = 7.0,
            AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
            AthDrawdownAdaptiveMinThresholdPercent = 1.0,
            AthDrawdownAdaptiveMaxThresholdPercent = 99.0,
            AthDrawdownOverrideThresholdPercent = 75.0
        };

        var threshold = AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(
            options,
            new DateTime(2026, 5, 9, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(threshold, Is.EqualTo(65.0));
    }

    [Test]
    public void ResolveSuppressThresholdPercent_WhenSuppressAdaptiveDisabled_UsesStaticThreshold()
    {
        var options = new BearMarketFilterOptions
        {
            AthDrawdownSuppressAdaptiveThresholdEnabled = false,
            AthDrawdownSuppressBearishSellThresholdPercent = 40.0,
            AthDrawdownAdaptiveThresholdEnabled = true,   // override adaptive is on, suppress ignores it
            AthDrawdownAdaptiveBasePercent = 100.0,
            AthDrawdownAdaptiveStepPercent = 7.0,
            AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
            AthDrawdownAdaptiveMinThresholdPercent = 1.0,
            AthDrawdownAdaptiveMaxThresholdPercent = 99.0
        };

        // Cycle 3 (2017) formula would give 100-7*3=79; static must return 40 instead.
        var threshold = AthDrawdownThresholdPolicy.ResolveSuppressThresholdPercent(
            options,
            new DateTime(2020, 10, 11, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(threshold, Is.EqualTo(40.0));
    }

    [Test]
    public void ResolveSuppressThresholdPercent_WhenSuppressAdaptiveEnabled_UsesFormula()
    {
        var options = new BearMarketFilterOptions
        {
            AthDrawdownSuppressAdaptiveThresholdEnabled = true,
            AthDrawdownAdaptiveThresholdEnabled = true,
            AthDrawdownAdaptiveBasePercent = 100.0,
            AthDrawdownAdaptiveStepPercent = 7.0,
            AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
            AthDrawdownAdaptiveMinThresholdPercent = 1.0,
            AthDrawdownAdaptiveMaxThresholdPercent = 99.0,
            AthDrawdownSuppressBearishSellThresholdPercent = 40.0
        };

        // Cycle 3 (2017-2020): 100 - 7*3 = 79 (formula, not static 40)
        var threshold = AthDrawdownThresholdPolicy.ResolveSuppressThresholdPercent(
            options,
            new DateTime(2020, 10, 11, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(threshold, Is.EqualTo(79.0));
    }

    [Test]
    public void ResolveOverrideThresholdPercent_ClampsToConfiguredBounds()
    {
        var options = new BearMarketFilterOptions
        {
            AthDrawdownAdaptiveThresholdEnabled = true,
            AthDrawdownAdaptiveBasePercent = 100.0,
            AthDrawdownAdaptiveStepPercent = 40.0,
            AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
            AthDrawdownAdaptiveMinThresholdPercent = 55.0,
            AthDrawdownAdaptiveMaxThresholdPercent = 99.0
        };

        var threshold = AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(
            options,
            new DateTime(2026, 5, 9, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(threshold, Is.EqualTo(55.0));
    }

    [Test]
    public void ResolveOverrideThresholdPercent_WhenAdaptiveModePreviousCycleMinusDelta_UsesPreviousCycleDrawdown()
    {
        var options = new BearMarketFilterOptions
        {
            AthDrawdownAdaptiveThresholdEnabled = true,
            AthDrawdownAdaptiveMode = "PreviousCycleMinusDelta",
            AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
            AthDrawdownAdaptiveCycleDrawdownPercents = [93.0, 87.0, 84.0, 77.0, 70.0],
            AthDrawdownAdaptivePreviousCycleDeltaPercent = 7.0,
            AthDrawdownAdaptiveMinThresholdPercent = 1.0,
            AthDrawdownAdaptiveMaxThresholdPercent = 99.0,
            AthDrawdownOverrideThresholdPercent = 75.0
        };

        var threshold = AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(
            options,
            new DateTime(2021, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(threshold, Is.EqualTo(77.0));
    }

    [Test]
    public void ResolveOverrideThresholdPercent_WhenPreviousCycleMissing_FallsBackToConfiguredThreshold()
    {
        var options = new BearMarketFilterOptions
        {
            AthDrawdownAdaptiveThresholdEnabled = true,
            AthDrawdownAdaptiveMode = "PreviousCycleMinusDelta",
            AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
            AthDrawdownAdaptiveCycleDrawdownPercents = [93.0, 87.0, 84.0, 77.0, 70.0],
            AthDrawdownAdaptivePreviousCycleDeltaPercent = 7.0,
            AthDrawdownOverrideThresholdPercent = 75.0,
            AthDrawdownAdaptiveMinThresholdPercent = 1.0,
            AthDrawdownAdaptiveMaxThresholdPercent = 99.0
        };

        var threshold = AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(
            options,
            new DateTime(2011, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(threshold, Is.EqualTo(75.0));
    }

    [Test]
    public void ResolveOverrideThresholdPercent_WhenAdaptiveModePreviousCycleMinusMedianDelta_UsesMedianCompressionDelta()
    {
        var options = new BearMarketFilterOptions
        {
            AthDrawdownAdaptiveThresholdEnabled = true,
            AthDrawdownAdaptiveMode = "PreviousCycleMinusMedianDelta",
            AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
            AthDrawdownAdaptiveCycleDrawdownPercents = [93.0, 87.0, 84.0, 77.0, 70.0],
            AthDrawdownAdaptivePreviousCycleDeltaPercent = 7.0,
            AthDrawdownAdaptiveMinThresholdPercent = 1.0,
            AthDrawdownAdaptiveMaxThresholdPercent = 99.0,
            AthDrawdownOverrideThresholdPercent = 75.0
        };

        var threshold = AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(
            options,
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(threshold, Is.EqualTo(71.0));
    }

    [Test]
    public void ResolveOverrideThresholdPercent_WhenMedianDeltaHasNoSamples_UsesConfiguredDelta()
    {
        var options = new BearMarketFilterOptions
        {
            AthDrawdownAdaptiveThresholdEnabled = true,
            AthDrawdownAdaptiveMode = "PreviousCycleMinusMedianDelta",
            AthDrawdownAdaptiveCycleStartYears = [2011, 2013, 2017, 2021, 2026],
            AthDrawdownAdaptiveCycleDrawdownPercents = [93.0, 87.0, 84.0, 77.0, 70.0],
            AthDrawdownAdaptivePreviousCycleDeltaPercent = 7.0,
            AthDrawdownAdaptiveMinThresholdPercent = 1.0,
            AthDrawdownAdaptiveMaxThresholdPercent = 99.0,
            AthDrawdownOverrideThresholdPercent = 75.0
        };

        var threshold = AthDrawdownThresholdPolicy.ResolveOverrideThresholdPercent(
            options,
            new DateTime(2013, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.That(threshold, Is.EqualTo(86.0));
    }
}
