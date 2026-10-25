using System.Globalization;
using System.Text.Json;

namespace BtcAdvisor.Services;

public sealed class HistoricalBinanceCandleService : IHistoricalBinanceCandleService
{
    private readonly HttpClient _httpClient = new() { BaseAddress = new Uri("https://api.binance.com") };

    public async Task<IReadOnlyList<OhlcvCandle>> GetCandlesRangeAsync(string interval, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken)
    {
        var normalizedStart = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);
        var normalizedEnd = DateTime.SpecifyKind(endUtc, DateTimeKind.Utc);
        if (normalizedEnd < normalizedStart)
        {
            return [];
        }

        var results = new List<OhlcvCandle>(2048);
        const int limit = 1000;
        long startTimeMs = new DateTimeOffset(normalizedStart).ToUnixTimeMilliseconds();
        long endTimeMs = new DateTimeOffset(normalizedEnd).ToUnixTimeMilliseconds();

        while (startTimeMs <= endTimeMs)
        {
            var url = $"/api/v3/klines?symbol=BTCUSDT&interval={interval}&startTime={startTimeMs}&endTime={endTimeMs}&limit={limit}";
            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            var payload = await response.Content.ReadAsStringAsync(cancellationToken);
            var rows = JsonSerializer.Deserialize<JsonElement[][]>(payload);
            if (rows is null || rows.Length == 0)
            {
                break;
            }

            foreach (var row in rows)
            {
                var candle = new OhlcvCandle(
                    OpenTime: DateTimeOffset.FromUnixTimeMilliseconds(row[0].GetInt64()).UtcDateTime,
                    Open: ParseDouble(row[1]),
                    High: ParseDouble(row[2]),
                    Low: ParseDouble(row[3]),
                    Close: ParseDouble(row[4]),
                    Volume: ParseDouble(row[5]),
                    CloseTime: DateTimeOffset.FromUnixTimeMilliseconds(row[6].GetInt64()).UtcDateTime);

                if (candle.CloseTime >= normalizedStart && candle.CloseTime <= normalizedEnd)
                {
                    results.Add(candle);
                }
            }

            if (rows.Length < limit)
            {
                break;
            }

            startTimeMs = rows[^1][6].GetInt64() + 1;
        }

        return results
            .GroupBy(c => c.CloseTime)
            .Select(g => g.Last())
            .OrderBy(c => c.CloseTime)
            .ToList();
    }

    private static double ParseDouble(JsonElement element)
        => double.Parse(element.GetString() ?? "NaN", CultureInfo.InvariantCulture);
}