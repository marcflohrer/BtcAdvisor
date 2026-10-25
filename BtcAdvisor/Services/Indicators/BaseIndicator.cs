using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Indicators;

public static class BaseIndicator
{
    public static string GetCurrentIndicatorName(IndicatorEvaluationInput indicatorEvaluationInput, string indicatorId, bool usePeriod)
    {
        return GetIndicatorName(indicatorEvaluationInput, indicatorId, usePeriod, true);
    }

    public static string GetPreviousIndicatorName(IndicatorEvaluationInput indicatorEvaluationInput, string indicatorId, bool usePeriod)
    {
        return GetIndicatorName(indicatorEvaluationInput, indicatorId, usePeriod, false);
    }

    public static IndicatorInputDatum GetCurrentMap(IndicatorEvaluationInput indicatorEvaluationInput, string indicatorId, bool usePeriod = false)
    {
        var indicatorNameCurrent = GetCurrentIndicatorName(indicatorEvaluationInput, indicatorId, usePeriod);
        if(!indicatorEvaluationInput.Current.TryGetValue(indicatorNameCurrent, out var indicatorMapCurrent))
        {
            throw new NotImplementedException($"No indicator {indicatorId} found in 'current' list {string.Join(',', indicatorEvaluationInput.Current.Keys)}");
        }
        return indicatorMapCurrent;
    }

    public static IndicatorInputDatum GetPreviousMap(IndicatorEvaluationInput indicatorEvaluationInput, string indicatorId, bool usePeriod = false)
    {
        var indicatorNamePrevious = GetPreviousIndicatorName(indicatorEvaluationInput, indicatorId, usePeriod);
        if(!indicatorEvaluationInput.Previous.TryGetValue(indicatorNamePrevious, out var indicatorMapPrevious))
        {
            throw new NotImplementedException($"No indicator {indicatorId} found in 'previous' list {string.Join(',', indicatorEvaluationInput.Previous.Keys)}");
        }
        return indicatorMapPrevious;
    }

    public static void GetMaps(IndicatorEvaluationInput indicatorEvaluationInput,
                                 string IndicatorId,
                                 out IndicatorInputDatum? indicatorMapCurrent,
                                 out IndicatorInputDatum? indicatorMapPrevious,
                                 bool usePeriod = false)
    {
        if (indicatorEvaluationInput.Current == null)
        {
            Console.WriteLine($"ERROR: Property 'Current' is null! --> {IndicatorId}");
        }

        indicatorMapCurrent = GetCurrentMap(indicatorEvaluationInput, IndicatorId, usePeriod);
        indicatorMapPrevious = indicatorEvaluationInput.Previous == null
            ? null
            : GetPreviousMap(indicatorEvaluationInput, IndicatorId, usePeriod);
    }

    public static double SetPrice(this IndicatorEvaluationInput indicatorEvaluationInput)
    {
        var priceKey = indicatorEvaluationInput.Current.Keys.SingleOrDefault(k => k.Contains("price"));
        if (priceKey is null)
        {
            Console.WriteLine($"ERROR: Missing key that contains price in dictionary. Known values are: '{string.Join(',', indicatorEvaluationInput.Current.Keys)}'");
            throw new InvalidDataException($"Missing key that contains price in dictionary. Known values are: {string.Join(',', indicatorEvaluationInput.Current.Keys)}");
        }
        if (!indicatorEvaluationInput.Current.TryGetValue(priceKey, out var priceIndicatorResult))
        {
            Console.WriteLine($"ERROR: Missing key price in dictionary. Known values are: {string.Join(',', indicatorEvaluationInput.Current.Keys)}");
            throw new InvalidDataException($"Missing key price in dictionary. Known values are: {string.Join(',', indicatorEvaluationInput.Current.Keys)}");
        }

        indicatorEvaluationInput.Price = priceIndicatorResult!.GetValue("value");
        return indicatorEvaluationInput.Price;
    }

    public static void SetPeriod(this IndicatorEvaluationInput indicatorEvaluationInput, int period)
    {
        indicatorEvaluationInput.Period = period;
    }

    private static int GetPeriod(string apiId)
    {
        var tokens = apiId.Split('_');
        return !int.TryParse(tokens[tokens.Length-2], out var period)
                ? throw new InvalidDataException($"""Parsing apiId for period failed: {apiId} --> trying to case {apiId.Split('_')[^2]} to int""")
                : period;
    }

    private static string GetIndicatorName(IndicatorEvaluationInput indicatorEvaluationInput, string indicatorId, bool usePeriod, bool isCurrent)
    {
        var methodName = isCurrent ? nameof(GetCurrentIndicatorName) : nameof(GetPreviousIndicatorName);
        var periodString = usePeriod ? $"{indicatorEvaluationInput.Period}_" : "";
        var query = $"_{indicatorId}_{periodString}";
        
        var keys = (isCurrent ? indicatorEvaluationInput.Current : indicatorEvaluationInput.Previous).Keys.ToList();
        try
        {
            var result = keys.FirstOrDefault(ind => ind.Contains(query));
            if (result != null && keys.Count(ind => ind.Contains(query)) == 1)
            {
                return result;
            }
            else
            {
                Console.WriteLine($"ERROR: {methodName} '{indicatorId}' '{usePeriod}' '{periodString}' Expected a unique match for query {query} but found {result} in this set {string.Join(',', keys)}");
                throw new InvalidDataException($"{methodName} '{indicatorId}' '{usePeriod}' '{periodString}' Expected a unique match for query {query} but found {result} in this set {string.Join(',', keys)}.");
            }
        }
        catch(Exception ex)
        {
            Console.WriteLine($"ERROR: {methodName} '{indicatorId}' '{usePeriod}' '{periodString}': Unexpected error for {query} in list {string.Join(',', keys)} {ex.Message}{ex.StackTrace}");
            throw;
        }
    }
}

