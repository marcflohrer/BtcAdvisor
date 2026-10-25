using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services.Conditions;

/// <summary>
/// Pure function that translates a <see cref="MarketContextSnapshot"/> into additive
/// bonus/penalty values for the linear bullish/bearish scores. Stateless and side-effect free.
/// </summary>
public static class LinearScoreBonusCalculator
{
    /// <summary>Returns (bullishBonus, bearishBonus) given the context snapshot and options.</summary>
    public static (double BullishBonus, double BearishBonus) Compute(
        MarketContextSnapshot? snapshot,
        VolumeVolatilityOptions options)
    {
        if (snapshot is null || !options.EnableScoreBonus)
        {
            return (0d, 0d);
        }

        double bullish = 0d;
        double bearish = 0d;

        // Bollinger context: squeeze penalises both, expansion rewards both
        if (snapshot.BbWidthPercent.HasValue && snapshot.BbWidthMaPercent.HasValue && snapshot.BbWidthMaPercent.Value > 0d)
        {
            var ratio = snapshot.BbWidthPercent.Value / snapshot.BbWidthMaPercent.Value;
            if (ratio < options.BbSqueezeRatio)
            {
                bullish -= options.BbSqueezeVotePenalty;
                bearish -= options.BbSqueezeVotePenalty;
            }
            else if (ratio > options.BbExpansionRatio)
            {
                bullish += options.BbExpansionVoteWeight;
                bearish += options.BbExpansionVoteWeight;
            }
        }

        // Volume context: positive OBV slope confirms bullish, negative confirms bearish
        if (snapshot.ObvSlopePercent.HasValue)
        {
            var slope = snapshot.ObvSlopePercent.Value;
            if (slope > options.ObvBullishSlopeThresholdPercent)
            {
                bullish += options.ObvVoteWeight;
            }
            else if (slope < -options.ObvBearishSlopeThresholdPercent)
            {
                bearish += options.ObvVoteWeight;
            }
        }

        // VWAP context: reclaim is a strong bullish signal; persistent distance is a softer trend confirmation
        if (snapshot.VwapReclaimed)
        {
            bullish += options.VwapReclaimVoteWeight;
        }

        if (snapshot.VwapDistancePercent.HasValue)
        {
            var dist = snapshot.VwapDistancePercent.Value;
            if (dist > options.VwapTrendDistancePercent)
            {
                bullish += options.VwapTrendVoteWeight;
            }
            else if (dist < -options.VwapTrendDistancePercent)
            {
                bearish += options.VwapTrendVoteWeight;
            }
        }

        // Money-Flow context: oversold favours mean-reversion bullish, overbought favours bearish
        if (snapshot.Mfi.HasValue)
        {
            var mfi = snapshot.Mfi.Value;
            if (mfi < options.MfiOversoldThreshold)
            {
                bullish += options.MfiVoteWeight;
            }
            else if (mfi > options.MfiOverboughtThreshold)
            {
                bearish += options.MfiVoteWeight;
            }
        }

        return (bullish, bearish);
    }
}
