using System.Text.Json.Serialization; // Import für JsonPropertyName

namespace BtcAdvisor
{
    public record BulkRequest
    {
        [JsonPropertyName ("secret")]
        public string Secret { get; set; } = string.Empty;
        public Construct Construct { get; set; } = new Construct ();
    }

    public partial class Construct
    {
        [JsonPropertyName ("exchange")]
        public string Exchange { get; set; } = string.Empty;

        [JsonPropertyName ("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName ("symbol")]
        public string Symbol { get; set; } = string.Empty;

        [JsonPropertyName ("interval")]
        public string Interval { get; set; } = string.Empty;

        [JsonPropertyName ("indicators")]
        public List<IndicatorClass> Indicators { get; set; } = [];
    }

    public partial class IndicatorClass
    {
        [JsonPropertyName ("indicator")]
        public string Indicator { get; set; } = string.Empty;

        [JsonPropertyName ("period")]
        public int? Period { get; set; } = 0;
    }
}
