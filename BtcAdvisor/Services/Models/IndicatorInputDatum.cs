namespace BtcAdvisor.Repository.Models;

public class IndicatorInputDatum
{
    public string? Indicator { get; set; } = string.Empty;
    public string? IndicatorId { get; set; } = string.Empty;
    public string IndicatorType { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.MinValue;
    public Dictionary<string, double> Values { get; set; } // Speichert alle relevanten Werte eines Indikatoren

    public IndicatorInputDatum ()
    {
        Values = [];
    }

    // Methode zum Hinzufügen oder Aktualisieren von Werten
    public void AddOrUpdateValue (string key, double value)
    {
        if (!Values.TryAdd (key, value))
        {
            Values[key] = value;
        }
    }

    // Methode zum Abrufen eines Wertes mit Fehlerbehandlung
    public double GetValue (string key)
    {
        if (Values.TryGetValue (key, out double value))
        {
            return value;
        }
        else
        {
            throw new KeyNotFoundException ($"Der Schlüssel '{key}' wurde im Indikatordatum nicht gefunden. Bekannte Schlüssel sind {string.Join(',', Values.Keys)}");
        }
    }
}
