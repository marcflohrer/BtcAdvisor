using Newtonsoft.Json;
using BtcAdvisor.Services.Models;
using BtcAdvisor.Services.Models.Extensions;

namespace BtcAdvisor.Tests;

[TestFixture]
public class ApiResponseExtensionsTest
{
    [SetUp]
    public void Setup ()
    { }

    [Test]
    public async Task ToIndicatorDatum_ShouldWork ()
    {
        // Arrange
        var jsonString = await File.ReadAllTextAsync ("./apiResponses.json");
        var apiResponse = JsonConvert.DeserializeObject<ApiResponse> (jsonString);

        // Act
        var indicatorDatum = apiResponse!.ToIndicatorDatum ();

        // Assert
        Assert.That(indicatorDatum, Is.Not.EqualTo(null));
        Console.WriteLine (System.Text.Json.JsonSerializer.Serialize (indicatorDatum));
        Assert.That(indicatorDatum!.IndicatorResults, Has.Count.GreaterThan(1));
    }
}
