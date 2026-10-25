using System.Net;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using BtcAdvisor.Models;
using BtcAdvisor.Services;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Tests;

[TestFixture]
public class MarketDataApiWrapperTests
{
    private Mock<IHttpClientFactory> _mockClientFactory;
    private Mock<IDateTimeProvider> _mockDateTimeProvider;
    private Mock<HttpMessageHandler> _mockMessageHandler;
    private HttpClient _httpClient;
    private ApiSettings _apiSettings;

    [SetUp]
    public void SetUp()
    {
        _mockClientFactory = new Mock<IHttpClientFactory>();
        _mockMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockMessageHandler.Object)
        {
            BaseAddress = new Uri("http://test-api.com/") // Beliebige Basis-URL
        };
        _apiSettings = new ApiSettings { MarketDataAppApiKey = "test-api-key" };
        _mockDateTimeProvider = new Mock<IDateTimeProvider>();
        _mockClientFactory.Setup(x => x.CreateClient("MARKETDATA_APP"))
            .Returns(_httpClient);
    }

    [Test]
    public async Task GetStockPrice_ReturnsData_WhenApiCallIsSuccessful()
    {
        // Arrange
        var apiTimestamp = 1761648300;
        var expectedResponse = GetExampleResponse(apiTimestamp);
        var jsonResponse = JsonConvert.SerializeObject(expectedResponse);
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };
        _mockDateTimeProvider.Setup(mock => mock.GetUtcNow()).Returns(DateTimeOffset.FromUnixTimeSeconds(apiTimestamp + 1799).DateTime);
        _mockMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        var wrapper = new MarketDataApiWrapper(_mockClientFactory.Object, Options.Create(_apiSettings), _mockDateTimeProvider.Object);

        // Act
        var result = await wrapper.GetStockPrice("MSTR", DateTime.UtcNow, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result.S, Is.EqualTo("ok"));
            Assert.That(result.T, Has.Count.EqualTo(1));
            Assert.That(result.T[0], Is.EqualTo(1761648300));
        }
    }

    [Test]
    public async Task GetStockPrice_ReturnsNull_WhenApiResponseIsTooOld()
    {
        // Arrange
        var apiTimestamp = 1761648300;
        var expectedResponse = GetExampleResponse(apiTimestamp);
        var jsonResponse = JsonConvert.SerializeObject(expectedResponse);
        var responseMessage = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse)
        };
        _mockDateTimeProvider.Setup(mock => mock.GetUtcNow()).Returns(DateTimeOffset.FromUnixTimeSeconds(apiTimestamp + 1801).DateTime);
        _mockMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(responseMessage);

        var wrapper = new MarketDataApiWrapper(_mockClientFactory.Object, Options.Create(_apiSettings), _mockDateTimeProvider.Object);

        // Act
        var result = await wrapper.GetStockPrice("MSTR", DateTime.UtcNow, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
    }

    // Beispiel-Antwort als Objekt
    private static MarketDataApiResponse GetExampleResponse(int timestamp)
    {
        return new MarketDataApiResponse
        {
            S = "ok",
            T = [timestamp],
            O = [295.01],
            H = [295.05],
            L = [295.01],
            C = [295.05],
            V = [5460]
        };
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
    }
}
