using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using BtcAdvisor.Extensions;
using BtcAdvisor.Repository;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Conditions;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public class BitcoinTradeAdvisor(
    IShortSellerStateRepository shortSellerStateRepository,
    IServiceScopeFactory serviceScopeFactory,
    Microsoft.Extensions.Caching.Memory.IMemoryCache memoryCache) : IBitcoinTradeAdvisor
{
    private readonly IShortSellerStateRepository shortSellerStateRepository = shortSellerStateRepository;

    public async Task<IReadOnlyCollection<BitcoinTradeStatus>> GetBitcoinTradeAdvice (
        DateTime startHistory,
        DateTime endHistory,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? strategyHigh,
        decimal? strategyLow,
        BuyActionLog? lowestBuyAfterFireSell,
        BuyActionLog? highestPanicBuyAfterLowestBuyAfterFireSell,
        CancellationToken cancellationToken)
    {
        return await GetBitcoinTradeAdvice(
            startHistory,
            endHistory,
            latestBuyPrice,
            latestSellPrice,
            strategyHigh,
            strategyLow,
            lowestBuyAfterFireSell,
            highestPanicBuyAfterLowestBuyAfterFireSell,
            cancellationToken,
            LongTimeframeConfluenceThresholdSet.Default);
    }

    public async Task<IReadOnlyCollection<BitcoinTradeStatus>> GetBitcoinTradeAdvice (
        DateTime startHistory,
        DateTime endHistory,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? strategyHigh,
        decimal? strategyLow,
        BuyActionLog? lowestBuyAfterFireSell,
        BuyActionLog? highestPanicBuyAfterLowestBuyAfterFireSell,
        CancellationToken cancellationToken,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet)
    {
        var bitcoinTradeStatusList = new List<BitcoinTradeStatus> ();
        startHistory = startHistory.ToUtcDateTime ();
        endHistory = endHistory.ToUtcDateTime ();
        var oscillatorTask = CollectStatistics (endHistory, true, cancellationToken);
        var movingAverageTask = CollectStatistics (endHistory, false, cancellationToken);
        await Task.WhenAll (oscillatorTask, movingAverageTask);

        var oscillatorStats = oscillatorTask.Result;
        if(oscillatorStats is null)
        {
            Console.WriteLine ("oscillatorStats is null");
            return bitcoinTradeStatusList;
        }
        if(oscillatorStats.Price == 0d)
        {
            Console.WriteLine ("oscillatorStats.Price == 0d");
            return bitcoinTradeStatusList;
        }

        var movingAverageStats = movingAverageTask.Result;
        if(movingAverageStats is null)
        {
            Console.WriteLine ("movingAverageStats is null");
            return bitcoinTradeStatusList;
        }

        var maxDateTime = GetMaxDateTime (oscillatorStats, movingAverageStats);
        var minDateTime = GetMinDateTime(oscillatorStats, movingAverageStats);
        var currentPrice = Convert.ToDecimal(oscillatorStats.Price);
        var lowestBuyAfterFireSellPrice = lowestBuyAfterFireSell?.Price;
        var highestPanicBuyAfterLowestBuyAfterFireSellPrice = highestPanicBuyAfterLowestBuyAfterFireSell?.Price;

        Console.WriteLine ($"GetBitcoinTradeAdvice start {startHistory:o} end {endHistory:o} latest buy price {latestBuyPrice} current Price {currentPrice} latest sell price {latestSellPrice} lowest buy after latest fire sell {lowestBuyAfterFireSellPrice} highest Panic Buy After lowest buy {highestPanicBuyAfterLowestBuyAfterFireSellPrice}");

        if (endHistory - startHistory < TimeSpan.FromSeconds (1))
        {
            var tradeStatus = EvaluateCurrentTradeAdvice(
                oscillatorStats,
                movingAverageStats,
                maxDateTime,
                minDateTime,
                latestBuyPrice,
                latestSellPrice,
                currentPrice,
                strategyHigh,
                strategyLow,
                lowestBuyAfterFireSellPrice,
                highestPanicBuyAfterLowestBuyAfterFireSellPrice,
                confluenceThresholdSet);
            bitcoinTradeStatusList.Add (tradeStatus);
            return bitcoinTradeStatusList;
        }
        else
        {
            Console.WriteLine ($"startHistory != endHistory: {startHistory} != {endHistory}");
        }

        while (startHistory < minDateTime)
        {
            var (osciStats, movingAvgStats) = await UpdateStatistics (minDateTime, cancellationToken);
            maxDateTime = GetMaxDateTime (osciStats, movingAvgStats);
            minDateTime = GetMinDateTime (osciStats, movingAvgStats);

            var bitcoinTradeStatus = EvaluateCurrentTradeAdvice(
                osciStats,
                movingAvgStats,
                maxDateTime,
                minDateTime,
                latestBuyPrice,
                latestSellPrice,
                currentPrice,
                strategyHigh,
                strategyLow,
                lowestBuyAfterFireSellPrice,
                highestPanicBuyAfterLowestBuyAfterFireSellPrice,
                confluenceThresholdSet);
            bitcoinTradeStatusList.Add (bitcoinTradeStatus);
            Console.WriteLine ($"WHILE startHistory < minDateTime: {startHistory} < {minDateTime}");
        }

        Console.WriteLine ($"DONE startHistory >= minDateTime: {startHistory} != {endHistory}");
        return bitcoinTradeStatusList;
    }

    private static DateTime GetMaxDateTime (StatsAndPrice oscillator, StatsAndPrice movingAverage) => oscillator.MaxDateTime > movingAverage.MaxDateTime ? oscillator.MaxDateTime : movingAverage.MaxDateTime;

    private static DateTime GetMinDateTime (StatsAndPrice oscillator, StatsAndPrice movingAverage) => oscillator.MinDateTime < movingAverage.MinDateTime ? oscillator.MinDateTime : movingAverage.MinDateTime;

    private static BitcoinTradeStatus EvaluateCurrentTradeAdvice(
        StatsAndPrice oscillator,
        StatsAndPrice movingAverage,
        DateTime maxDateTime,
        DateTime minDateTime,
        decimal? latestBuyPrice,
        decimal? latestSellPrice,
        decimal? currentPrice,
        decimal? strategyHigh,
        decimal? strategyLow,
        decimal? lowestBuyAfterFireSell,
        decimal? highestPanicBuyAfterLowestBuyAfterFireSell,
        LongTimeframeConfluenceThresholdSet confluenceThresholdSet)
    {
        var tradeStatus = PrimaryDecisionMaker.EvaluateTradeAdvice (
            oscillator.Price,
            oscillator.TimeRangeStatisticsMap,
            movingAverage.TimeRangeStatisticsMap,
            $"{oscillator.DebugString} ## {movingAverage.DebugString}",
            maxDateTime,
            minDateTime,
            latestBuyPrice,
            latestSellPrice,
            strategyHigh,
            strategyLow,
            lowestBuyAfterFireSell,
            highestPanicBuyAfterLowestBuyAfterFireSell,
            confluenceThresholdSet);
        // Populate BIS (Bitcoin Indicator Statistics) snapshot at signal time
        tradeStatus.AlignedRanges = LongTimeframeConfluenceEvaluator.ComputeBullishAlignedRanges(oscillator.TimeRangeStatisticsMap);
        tradeStatus.TrendCountSum = LongTimeframeConfluenceEvaluator.ComputeTrendCountSum(oscillator.TimeRangeStatisticsMap);
        tradeStatus.LinearBullishScore = LongTimeframeConfluenceEvaluator.ComputeLinearBullishScore(oscillator.TimeRangeStatisticsMap);
        tradeStatus.LinearBearishScore = LongTimeframeConfluenceEvaluator.ComputeLinearBearishScore(oscillator.TimeRangeStatisticsMap);
        return tradeStatus;
    }

    private async Task <(StatsAndPrice Oscillator, StatsAndPrice MovingAverage)> UpdateStatistics (DateTime minDateTime, CancellationToken cancellationToken)
    {
        var oscillatorTask = CollectStatistics (minDateTime, true, cancellationToken);
        var movingAverageTask = CollectStatistics (minDateTime, false, cancellationToken);
        await Task.WhenAll (oscillatorTask, movingAverageTask);
        return (oscillatorTask.Result, movingAverageTask.Result);
    }

    private async Task<StatsAndPrice> CollectStatistics (DateTime dateTime, bool isOscillator, CancellationToken cancellationToken)
    {
        // Cache key: combination of timestamp + indicator type (independent of simulation state)
        var cacheKey = $"stats:{dateTime:o}:{isOscillator}";
        if (memoryCache.TryGetValue<StatsAndPrice>(cacheKey, out var cached) && cached is not null)
            return cached;

        var timeRanges = LongTimeframeSignalConstants.CurrentSignalProfileIsLongTimeframe
            ? new[] { "2h", "4h", "12h", "1d", "1w" }
            : new[] { "5m", "15m", "30m", "1h", "2h" };

        // Alle 5 Zeitfenster parallel abfragen – jedes in eigenem DI-Scope (eigener DbContext)
        var tasks = timeRanges.Select (async timeSpan =>
        {
            using var scope = serviceScopeFactory.CreateScope ();
            var statsService = scope.ServiceProvider.GetRequiredService<IBitcoinStatisticsService> ();
            var bitcoinStatistic = await statsService.GetStatistics (dateTime, isOscillator, timeSpan, cancellationToken);
            return (timeSpan, bitcoinStatistic);
        }).ToArray ();

        var results = await Task.WhenAll (tasks);

        var stats = new Dictionary<string, BitcoinTimeRangeStatistics> ();
        var currentPrice = 0d;
        var debugStringAppender = new StringBuilder ();
        var minDateTime = DateTime.MaxValue;
        var maxDateTime = DateTime.MinValue;
        var labels = new List<string> ();
        foreach (var (timeSpan, bitcoinStatistic) in results)
        {
            currentPrice = bitcoinStatistic.Price;
            var statistics = bitcoinStatistic.TimeRangeStatistics.Single ();
            stats.Add (timeSpan, statistics);
            debugStringAppender.Append ($"[ {timeSpan}: {bitcoinStatistic.DebugString} ]");
            labels.Add ($"[ {timeSpan}: {bitcoinStatistic.DebugString} ]");
            if (bitcoinStatistic.DateTime < minDateTime)
            {
                minDateTime = bitcoinStatistic.DateTime;
            }
            if (bitcoinStatistic.DateTime > maxDateTime)
            {
                maxDateTime = bitcoinStatistic.DateTime;
            }
        }

        var result = new StatsAndPrice
        {
            Price = currentPrice,
            TimeRangeStatisticsMap = stats,
            DebugString = debugStringAppender.ToString (),
            MinDateTime = minDateTime,
            MaxDateTime = maxDateTime,
            Labels = labels
        };

        memoryCache.Set(cacheKey, result, TimeSpan.FromHours(24));
        return result;
    }

    public async Task<List<(string TradeAdvice, decimal Price, DateTime TimeStamp)>> GetHistoryAsync(int take, CancellationToken cancellationToken) 
                => await shortSellerStateRepository.GetHistoryAsync(take, cancellationToken);
}
