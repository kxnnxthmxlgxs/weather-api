using System.Net;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Moq;
using Moq.Protected;
using Xunit;
using WeatherApi.Services;

namespace WeatherApi.Tests;

public class WeatherServiceTests
{
    [Fact]
    public async Task GetWeatherAsync_CacheHit_ReturnsDataWithoutCallingApi()
    {
        // Arrange
        var cacheMock = new Mock<IDistributedCache>();
        var configMock = new Mock<IConfiguration>();
        var handlerMock = new Mock<HttpMessageHandler>(); // How we mock HttpClient

        // 1. Simulate data already existing in Redis
        var cachedJson = @"{""address"":""Durban"",""currentConditions"":{""temp"":25.0,""conditions"":""Sunny""}}";
        var cachedBytes = Encoding.UTF8.GetBytes(cachedJson);
        
        // IDistributedCache extension methods (GetStringAsync) internally call GetAsync
        cacheMock.Setup(x => x.GetAsync("weather_durban", It.IsAny<CancellationToken>()))
                 .ReturnsAsync(cachedBytes);

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var service = new WeatherService(httpClient, cacheMock.Object, configMock.Object);

        // Act
        var result = await service.GetWeatherAsync("Durban");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Durban", result.City);
        Assert.Equal(25.0, result.Temperature);
        
        // Verify HttpClient was NEVER called because we had a cache hit
        handlerMock.Protected().Verify(
            "SendAsync",
            Times.Never(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>()
        );
    }

    [Fact]
    public async Task GetWeatherAsync_CacheMiss_CallsApiAndSavesToCache()
    {
        // Arrange
        var cacheMock = new Mock<IDistributedCache>();
        var configMock = new Mock<IConfiguration>();
        configMock.Setup(c => c["WeatherApiKey"]).Returns("fake-key");

        var handlerMock = new Mock<HttpMessageHandler>();

        // 1. Simulate a Cache Miss (Redis returns null)
        cacheMock.Setup(x => x.GetAsync("weather_durban", It.IsAny<CancellationToken>()))
                 .ReturnsAsync((byte[]?)null);

        // 2. Simulate a successful Visual Crossing API Response
        var apiJson = @"{""address"":""Durban"",""currentConditions"":{""temp"":22.0,""conditions"":""Cloudy""}}";
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(apiJson)
            });

        var httpClient = new HttpClient(handlerMock.Object) { BaseAddress = new Uri("http://localhost") };
        var service = new WeatherService(httpClient, cacheMock.Object, configMock.Object);

        // Act
        var result = await service.GetWeatherAsync("Durban");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Durban", result.City);
        Assert.Equal(22.0, result.Temperature);
        
        // Verify the new data was saved to Redis
        cacheMock.Verify(x => x.SetAsync(
            "weather_durban",
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}