using System.Net;
using Moq;
using Moq.Protected;
using Newtonsoft.Json;
using BtcAdvisor.Services;

namespace BtcAdvisor.Tests;

[TestFixture]
public class YahooMarketDataApiWrapperTests
{
    private Mock<IHttpClientFactory> _mockClientFactory = null!;
    private Mock<IDateTimeProvider> _mockDateTimeProvider = null!;
    private Mock<HttpMessageHandler> _mockMessageHandler = null!;
    private HttpClient _httpClient = null!;

    [SetUp]
    public void SetUp()
    {
        _mockClientFactory = new Mock<IHttpClientFactory>();
        _mockMessageHandler = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_mockMessageHandler.Object)
        {
            BaseAddress = new Uri("https://query1.finance.yahoo.com/") // Beliebige Basis-URL
        };
        _mockDateTimeProvider = new Mock<IDateTimeProvider>();
        _mockClientFactory.Setup(x => x.CreateClient("YAHOO_FINANCE"))
            .Returns(_httpClient);
    }

    [Test]
    public async Task GetStockPrice_ReturnsData_WhenApiCallIsSuccessful()
    {
        // Arrange
        var apiTimestamp = 1761648300L;
        var jsonResponse = GetYahooJson(apiTimestamp);
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

        var wrapper = new YahooMarketDataApiWrapper(_mockClientFactory.Object, _mockDateTimeProvider.Object);
        var endTime = DateTimeOffset.FromUnixTimeSeconds(apiTimestamp + 600).UtcDateTime;

        // Act
        var result = await wrapper.GetStockPrice("MSTR", endTime, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Not.Null);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(result!.S, Is.EqualTo("ok"));
            Assert.That(result.T, Has.Count.EqualTo(1));
            Assert.That(result.T[0], Is.EqualTo(1761648300));
            Assert.That(result.C[0], Is.EqualTo(295.05));
            Assert.That(result.V[0], Is.EqualTo(5460));
        }
    }

    [Test]
    public async Task GetStockPrice_ReturnsNull_WhenApiResponseIsTooOld()
    {
        // Arrange
        var apiTimestamp = 1761648300L;
        var jsonResponse = GetYahooJson(apiTimestamp);
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

        var wrapper = new YahooMarketDataApiWrapper(_mockClientFactory.Object, _mockDateTimeProvider.Object);
        var endTime = DateTimeOffset.FromUnixTimeSeconds(apiTimestamp + 600).UtcDateTime;

        // Act
        var result = await wrapper.GetStockPrice("MSTR", endTime, CancellationToken.None);

        // Assert
        Assert.That(result, Is.Null);
    }

    // Beispiel-Antwort im Yahoo-Chart-Format
    private static string GetYahooJson(long timestamp)
    {
        return JsonConvert.SerializeObject(new
        {
            chart = new
            {
                result = new[]
                {
                    new
                    {
                        timestamp = new[] { timestamp },
                        indicators = new
                        {
                            quote = new[]
                            {
                                new
                                {
                                    open = new double?[] { 295.01 },
                                    high = new double?[] { 295.05 },
                                    low = new double?[] { 295.01 },
                                    close = new double?[] { 295.05 },
                                    volume = new long?[] { 5460 }
                                }
                            }
                        }
                    }
                },
                error = (object?)null
            }
        });
    }

    [TearDown]
    public void TearDown()
    {
        _httpClient.Dispose();
    }
}
