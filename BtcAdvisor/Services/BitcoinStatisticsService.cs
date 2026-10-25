using BtcAdvisor.Constants;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Indicators;
using BtcAdvisor.Services.Models;
using BtcAdvisor.Controllers.Models;
using System.Text;

namespace BtcAdvisor.Services;

public class BitcoinStatisticsService (IBitcoinIndicatorService bitcoinIndicatorService,
    IIndicatorRegistry indicatorRegistry) : IBitcoinStatisticsService
{
    private readonly IBitcoinIndicatorService _bitcoinIndicatorService = bitcoinIndicatorService;
    private readonly IIndicatorRegistry _indicatorRegistry = indicatorRegistry;

    public async Task<BitcoinStatistics> GetStatistics (DateTime dateTime, bool isOszillator, string timeSpan, CancellationToken cancellationToken)
    {
        var indicatorResults = await _bitcoinIndicatorService.GetFormattedIndicatorData(dateTime,
                                                                                        timeSpan,
                                                                                        "price",
                                                                                        null,
                                                                                        cancellationToken) 
            ?? throw new InvalidDataException("GetFormattedIndicatorData returned null.");

        var dateTimeResult = indicatorResults.DateTime;

        var signalCounterMap = new Dictionary<Signal, int>();

        // Iterate over enum values using a foreach loop
        foreach (Signal value in Enum.GetValues<Signal>())
        {
            signalCounterMap.TryAdd(value, 0);
        }
        var currentPrice = indicatorResults.SetPrice();
        
        var debugBuy = new StringBuilder();
        var debugSell = new StringBuilder();
        var debugTrend = new StringBuilder();

        foreach (var indicatorSpec in BitcoinConstants.IndicatorList().Where(i => i.IsOszillator == isOszillator))
        {
            try
            {
                if (indicatorSpec.Name == "price")
                {
                    continue;
                }

                indicatorResults = await _bitcoinIndicatorService.GetFormattedIndicatorData(dateTime,
                                                                        timeSpan,
                                                                        indicatorSpec.Name,
                                                                        indicatorSpec.Period,
                                                                        cancellationToken);
                if(indicatorResults is null)
                {
                    Console.WriteLine($"--> No indicators found for {indicatorSpec.Name} {timeSpan} {dateTime.ToString()}");
                    continue;
                }

                var indicatorService = _indicatorRegistry.GetIndicatorService(indicatorSpec.Name);
                if(indicatorService.HasPeriod())
                {
                    indicatorResults!.SetPeriod((int)indicatorSpec.Period!);
                }

                var signalResult = indicatorService.Evaluate(indicatorResults);
                signalCounterMap[signalResult]++;

                UpdateDebugStrings(signalResult, indicatorSpec, ref debugBuy, ref debugSell, ref debugTrend);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating statistics for {indicatorSpec.Name} {ex.Message}-{ex.StackTrace}");
            }
        }

        var debugString = BuildDebugString(debugBuy.ToString(), debugSell.ToString(), debugTrend.ToString());

        return new BitcoinStatistics
        {
            Price = currentPrice,
            DebugString = debugString,
            DateTime = dateTimeResult,
            TimeRangeStatistics =
            [
                new BitcoinTimeRangeStatistics
                {
                    TimespanHours = BitcoinConstants.MapPeriodToMinutes[timeSpan],
                    IndicatorSellCount = signalCounterMap[Signal.Sell],
                    IndicatorBuyCount = signalCounterMap[Signal.Buy],
                    IndicatorNeutralCount = signalCounterMap[Signal.Neutral],
                    IndicatorStrongTrendCount = signalCounterMap[Signal.StrongTrend],
                    IndicatorMediumTrendCount = signalCounterMap[Signal.MediumTrend],
                    IndicatorWeakTrendCount = signalCounterMap[Signal.WeakTrend],
                    IndicatorNoTrendCount = signalCounterMap[Signal.NoTrend],
                    IndicatorNotFound = signalCounterMap[Signal.None],
                    IsOscillatorIndicatorGroup = isOszillator,
                }
            ]
        };
    }
   
    private static void UpdateDebugStrings(Signal result, IndicatorSpec spec, ref StringBuilder buy, ref StringBuilder sell, ref StringBuilder trend)
    {
        if (result == Signal.Buy)
        {
            buy.Append($"/{spec.Name}");
        }
        else if (result == Signal.Sell)
        {
            sell.Append($"/{spec.Name}");
        }
        if (result == Signal.WeakTrend || result == Signal.MediumTrend || result == Signal.StrongTrend)
        {
            trend.Append($"{spec.Name}::{result}");
        }
    }

    private static string BuildDebugString(string buy, string sell, string trend)
    {
        var debugString = string.Empty;
        if (!string.IsNullOrEmpty(buy))
        {
            debugString += $"//Kauf: {buy}";
        }
        if (!string.IsNullOrEmpty(sell))
        {
            debugString += $"// Verkauf: {sell}";
        }
        if (!string.IsNullOrEmpty(trend))
        {
            debugString += $"// Trend: {trend}";
        }
        return debugString;
    }
}
