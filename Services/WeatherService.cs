using System.Text.Json.Nodes;
using Microsoft.Extensions.Caching.Distributed;

namespace WeatherApi.Services;

public interface IWeatherService
{
    Task<WeatherSummary?> GetWeatherAsync(string city);
}

public class WeatherService : IWeatherService
{
    private readonly HttpClient _httpClient;
    private readonly IDistributedCache _cache;
    private readonly IConfiguration _config;

    // The HttpClient is injected automatically by the Typed Client factory
    public WeatherService(HttpClient httpClient, IDistributedCache cache, IConfiguration config)
    {
        _httpClient = httpClient;
        _cache = cache;
        _config = config;
    }

    public async Task<WeatherSummary?> GetWeatherAsync(string city)
    {
        var cacheKey = $"weather_{city.ToLower()}";
        
        // 1. Check Redis Cache
        var cachedWeather = await _cache.GetStringAsync(cacheKey);
        if (!string.IsNullOrEmpty(cachedWeather))
        {
            var parsed = JsonNode.Parse(cachedWeather);
            return MapToDto(parsed);
        }

        // 2. Fetch from Visual Crossing API
        var apiKey = _config["WeatherApiKey"];
        var response = await _httpClient.GetAsync($"{city}?key={apiKey}");
        
        if (!response.IsSuccessStatusCode) return null; // Let the endpoint handle the error

        var rawJson = await response.Content.ReadAsStringAsync();

        // 3. Save to Redis Cache
        var cacheOptions = new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12) };
        await _cache.SetStringAsync(cacheKey, rawJson, cacheOptions);

        return MapToDto(JsonNode.Parse(rawJson));
    }

    // 4. Clean Mapping Logic
    private WeatherSummary MapToDto(JsonNode? parsedJson)
    {
        return new WeatherSummary(
            City: parsedJson?["address"]?.ToString() ?? "",
            Temperature: (double)(parsedJson?["currentConditions"]?["temp"] ?? 0),
            Conditions: parsedJson?["currentConditions"]?["conditions"]?.ToString() ?? "",
            Source: "Visual Crossing API"
        );
    }
}

// Our DTO is moved here
public record WeatherSummary(string City, double Temperature, string Conditions, string Source);