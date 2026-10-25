using Newtonsoft.Json;
using BtcAdvisor.Services.Models;
using Microsoft.Extensions.Options;
using BtcAdvisor.Models;

namespace BtcAdvisor.Services;

public class MarketDataApiWrapper : IMarketDataApiWrapper
{
    private readonly HttpClient _client;
    private readonly IDateTimeProvider _dateTimeProvider;

    // Konstruktor, der einen HttpClient injiziert bekommt.
    public MarketDataApiWrapper(
        IHttpClientFactory clientFactory,
        IOptions<ApiSettings> apiSettings,
        IDateTimeProvider dateTimeProvider)
    {
        _client = clientFactory.CreateClient("MARKETDATA_APP");
        _client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiSettings.Value.MarketDataAppApiKey);
        _dateTimeProvider = dateTimeProvider;
    }

    public async Task<MarketDataApiResponse?> GetStockPrice (string symbol, DateTime endTime, CancellationToken cancellationToken)
    {
        var candleMinutes = 15;
        var startTime = endTime.AddMinutes(candleMinutes * (-1));
        var url = $"v1/stocks/candles/{candleMinutes}/{symbol}/?start_time={startTime:yyyy-MM-ddTHH:mm:ssZ}&end_time={endTime:yyyy-MM-ddTHH:mm:ssZ}&extended=true&adjustsplits=true";

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
                    Console.WriteLine($"Response content {jsonResponse}");
                    var apiResponse = JsonConvert.DeserializeObject<MarketDataApiResponse>(jsonResponse);
                    var utcNow = _dateTimeProvider.GetUtcNow();
                    // Prüfen, ob der Markt geschlossen sein könnte
                    if (IsMarketClosed(apiResponse?.T ?? [DateTime.MinValue.ToUnixTime()], candleMinutes, utcNow))
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
}
