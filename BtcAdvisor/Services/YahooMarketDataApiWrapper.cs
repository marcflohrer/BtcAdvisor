using Newtonsoft.Json;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Services;

public class YahooMarketDataApiWrapper : IMarketDataApiWrapper
{
    private readonly HttpClient _client;
    private readonly IDateTimeProvider _dateTimeProvider;

    // Konstruktor, der einen HttpClient injiziert bekommt.
    public YahooMarketDataApiWrapper(
        IHttpClientFactory clientFactory,
        IDateTimeProvider dateTimeProvider)
    {
        _client = clientFactory.CreateClient("YAHOO_FINANCE");
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<MarketDataApiResponse?> GetStockPrice (string symbol, DateTime endTime, CancellationToken cancellationToken)
    {
        const int candleMinutes = 15;
        var startTime = endTime.AddMinutes(candleMinutes * (-1));
        var startUnix = new DateTimeOffset(DateTime.SpecifyKind(startTime, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var endUnix = new DateTimeOffset(DateTime.SpecifyKind(endTime, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var url = $"v8/finance/chart/{Uri.EscapeDataString(symbol)}?interval=15m&range=5d&includePrePost=true";

        while (true) // Wiederhole die Anfrage, bis sie erfolgreich ist.
        {
            try
            {
                Console.WriteLine($"Requesting {url}");
                var response = await _client.GetAsync(url, cancellationToken);
                Console.WriteLine($"Response status {response.StatusCode}");
                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
                    var chart = JsonConvert.DeserializeObject<YahooChartResponse>(jsonResponse);
                    var result = chart?.Chart?.Result?.FirstOrDefault();
                    if (result == null)
                    {
                        Console.WriteLine("Yahoo Finance returned no chart result");
                        return null;
                    }

                    var apiResponse = ToMarketDataApiResponse(result, startUnix, endUnix);
                    var utcNow = _dateTimeProvider.GetUtcNow();
                    // Prüfen, ob der Markt geschlossen sein könnte
                    if (IsMarketClosed(apiResponse?.T ?? [], candleMinutes, utcNow))
                    {
                        Console.WriteLine("Market is closed");
                        return null; // Markt ist geschlossen, keine gültigen Daten
                    }

                    return apiResponse;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    int waitTime = 60; // Standardmäßige Wartezeit auf 60 Sekunden gesetzt
                    if (response.Headers.RetryAfter?.Delta != null)
                    {
                        waitTime = (int)response.Headers.RetryAfter.Delta.Value.TotalSeconds;
                    }
                    else if (response.Headers.RetryAfter?.Date != null)
                    {
                        waitTime = (int)(response.Headers.RetryAfter.Date.Value - DateTime.UtcNow).TotalSeconds;
                    }
                    await Task.Delay(waitTime * 1000, cancellationToken); // Warte die angegebene Zeit
                }
                else
                {
                    throw new HttpRequestException($"An error occurred while requesting data: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"An error occurred: {ex.Message}");
                await Task.Delay(60000, cancellationToken); // Warte 60 Sekunden vor dem nächsten Versuch
            }
        }
    }

    // Mappt die Yahoo-Chart-Antwort auf das bestehende MarketDataApiResponse-Modell
    // und filtert auf das angefragte Zeitfenster [startUnix, endUnix].
    private static MarketDataApiResponse ToMarketDataApiResponse(YahooChartResult result, long startUnix, long endUnix)
    {
        var quote = result.Indicators?.Quote?.FirstOrDefault();
        var timestamps = result.Timestamp ?? [];
        var response = new MarketDataApiResponse
        {
            S = "ok",
            T = [],
            O = [],
            H = [],
            L = [],
            C = [],
            V = []
        };
        for (var i = 0; i < timestamps.Count; i++)
        {
            var t = timestamps[i];
            if (t < startUnix || t > endUnix) continue;
            var open = quote?.Open is null ? null : SafeAt(quote.Open, i);
            var high = quote?.High is null ? null : SafeAt(quote.High, i);
            var low = quote?.Low is null ? null : SafeAt(quote.Low, i);
            var close = quote?.Close is null ? null : SafeAt(quote.Close, i);
            if (open == null || high == null || low == null || close == null) continue;
            response.T.Add(t);
            response.O.Add(open.Value);
            response.H.Add(high.Value);
            response.L.Add(low.Value);
            response.C.Add(close.Value);
            response.V.Add(quote?.Volume is null ? 0 : SafeAt(quote.Volume, i) ?? 0);
        }
        return response;
    }

    private static T? SafeAt<T>(List<T?> list, int index) where T : struct
        => index < list.Count ? list[index] : null;

    private static bool IsMarketClosed(List<long> timestamps, int candleMinutes, DateTime utcNow)
    {
        if (timestamps == null || timestamps.Count == 0)
        {
            // Keine Daten, möglicherweise geschlossen
            return true;
        }
        var maxTimestamp = timestamps.Max(t => t);
        var maxDateTime = DateTimeOffset.FromUnixTimeSeconds(maxTimestamp).DateTime;
        var utcNowOffset = (utcNow - maxDateTime).TotalMinutes;

        // Angenommen, der Markt ist geschlossen, wenn die neuesten Daten mehr als candleMinutes Minuten alt sind
        return utcNowOffset > 2 * candleMinutes;
    }

    private class YahooChartResponse
    {
        [JsonProperty("chart")] public YahooChart? Chart { get; set; }
    }

    private class YahooChart
    {
        [JsonProperty("result")] public List<YahooChartResult>? Result { get; set; }
    }

    private class YahooChartResult
    {
        [JsonProperty("timestamp")] public List<long>? Timestamp { get; set; }
        [JsonProperty("indicators")] public YahooIndicators? Indicators { get; set; }
    }

    private class YahooIndicators
    {
        [JsonProperty("quote")] public List<YahooQuote>? Quote { get; set; }
    }

    private class YahooQuote
    {
        [JsonProperty("open")] public List<double?>? Open { get; set; }
        [JsonProperty("high")] public List<double?>? High { get; set; }
        [JsonProperty("low")] public List<double?>? Low { get; set; }
        [JsonProperty("close")] public List<double?>? Close { get; set; }
        [JsonProperty("volume")] public List<long?>? Volume { get; set; }
    }
}
