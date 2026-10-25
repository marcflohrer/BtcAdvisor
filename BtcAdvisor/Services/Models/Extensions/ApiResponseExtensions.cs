using BtcAdvisor.Repository.Models;

namespace BtcAdvisor.Services.Models.Extensions;

public static class ApiResponseExtensions
{
    public static IndicatorDatum? ToIndicatorDatum (this ApiResponse apiResponse)
    {
        var indicatorResults = new Dictionary<string, IndicatorResult> ();
        var indicatorErrors = new Dictionary<string, IndicatorError> ();
        foreach (var item in apiResponse.Data)
        {
            HandleIndicatorErrors (item, indicatorErrors);
            HandleIndicatorResults (item, indicatorResults);
        }

        return BuildIndicatorDatum (apiResponse, indicatorResults, indicatorErrors);
    }

    private static void HandleIndicatorErrors(Datum item, Dictionary<string, IndicatorError> errors)
    {
        if(errors is null || item is null)
        {
            return;
        }
        if (!errors.ContainsKey(item.Id))
        {
            errors[item.Id] = new IndicatorError { ErrorMessage = string.Join(',', item.Errors) };
        }
    }

    private static void HandleIndicatorResults (Datum item, Dictionary<string, IndicatorResult> results)
    {
        if(results is null || item is null || item.Result is null)
        {
            return;
        }
        foreach (var resultEntry in item.Result)
        {
            if (!results.TryGetValue (item.Id, out var indicatorResult))
            {
                indicatorResult = new IndicatorResult
                {
                    IndicatorId = item.Id,
                    Indicator = item.Indicator
                };
                results.Add (item.Id, indicatorResult);
            }
            UpdateIndicatorResult (indicatorResult, resultEntry.Key, resultEntry.Value);
        }
    }

    private static void UpdateIndicatorResult (IndicatorResult result, string key, double value)
    {
        switch (key)
        {
            case "value":
                result.Value = value;
                break;
            case "valueK":
                result.ValueK = value;
                break;
            case "valueD":
                result.ValueD = value;
                break;
            case "valueMACD":
                result.ValueMacd = value;
                break;
            case "valueMACDSignal":
                result.ValueMacdsignal = value;
                break;
            case "valueMACDHist":
                result.ValueMacdhist = value;
                break;
            case "conversion":
                result.Conversion = value;
                break;
            case "base":
                result.Base = value;
                break;
            case "spanA":
                result.SpanA = value;
                break;
            case "spanB":
                result.SpanB = value;
                break;
            case "currentSpanA":
                result.CurrentSpanA = value;
                break;
            case "currentSpanB":
                result.CurrentSpanB = value;
                break;
            case "laggingSpanA":
                result.LaggingSpanA = value;
                break;
            case "laggingSpanB":
                result.LaggingSpanB = value;
                break;
            case "valueFastK":
                result.ValueFastK = value;
                break;
            case "valueFastD":
                result.ValueFastD = value;
                break;
            default:
                throw new NotImplementedException ($"Indicator not implemented: {key}");
        }
    }

    private static IndicatorDatum BuildIndicatorDatum (ApiResponse response, Dictionary<string, IndicatorResult> results, Dictionary<string, IndicatorError> errors)
    {
        var datum = new IndicatorDatum ();
        foreach (var item in response.Data)
        {
            datum.ApiId = item.Id;
            datum.Indicator ??= item.Indicator;
            if (results.TryGetValue (item.Id, out var result))
            {
                datum.IndicatorResults.Add (result);
            }
            if (errors.TryGetValue (item.Id, out var error))
            {
                datum.IndicatorErrors.Add (error);
            }
        }
        return datum;
    }
}
