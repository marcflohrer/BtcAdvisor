using Newtonsoft.Json;

namespace BtcAdvisor.Services.Models;

public partial class ApiResponse
{
    [JsonProperty("data")]
    public required List<Datum> Data { get; set; } = [];
}

public partial class Datum
{
    [JsonProperty("id")]
    public required string Id { get; set; }

    [JsonProperty("indicator")]
    public required string Indicator { get; set; }

    [JsonProperty("result")]
    public required Dictionary<string, double> Result { get; set; }

    [JsonProperty("errors")]
    public required List<string> Errors { get; set; }
}
