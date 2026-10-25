using BtcAdvisor.Services.Models;
using Newtonsoft.Json; // Für die Deserialisierung der JSON-Antwort

namespace BtcAdvisor.Services;

public class TradeAdviceService(HttpClient httpClient)
{
    private readonly HttpClient _httpClient = httpClient;

    public async Task<TradeAdviceDto> GetTradeAdviceAsync()
    {
        try
        {
            var httpResponseMessage = await _httpClient.GetAsync("bitcoin");
            httpResponseMessage.EnsureSuccessStatusCode(); // Wirft eine Ausnahme, wenn der HTTP-Statuscode ein Fehler ist
            var json = await httpResponseMessage.Content.ReadAsStringAsync();

            // Deserialisiere die Antwort in ein dynamisches Objekt, um einfach auf die Properties zuzugreifen
            var tradeAdviceDto = JsonConvert.DeserializeObject<TradeAdviceDto>(json) ?? throw new InvalidDataException($"Cannot parse trade advice {json}");
            return tradeAdviceDto;
        }
        catch (HttpRequestException e)
        {
            // Fehlerbehandlung oder Logging hier einfügen
            Console.Error.WriteLine($"An error occurred: {e.Message}");
            throw;
        }
    }
}
