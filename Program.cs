using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.OpenApi; // <-- NO MORE .Models!
using WeatherApi.Services;
using WeatherApi.Auth;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();

// 1. Configure Swagger to use API Key Authentication (.NET 10 / v3 Syntax)
builder.Services.AddSwaggerGen(c =>
{
    c.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Description = "The API Key to access the API. Expected header: X-API-Key",
        Type = SecuritySchemeType.ApiKey,
        Name = "X-API-Key",
        In = ParameterLocation.Header,
        Scheme = "ApiKeyScheme"
    });
    
    c.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("ApiKey", document)] = new List<string>()
    });
});

// 2. Configure Redis Cache
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis");
});

// 3. Configure HTTP Client Factory (Typed Client Registration)
builder.Services.AddHttpClient<IWeatherService, WeatherService>(client =>
{
    client.BaseAddress = new Uri("https://weather.visualcrossing.com/VisualCrossingWebServices/rest/services/timeline/");
});

// 4. Configure Rate Limiting (Fixed Window)
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("fixed", limiterOptions =>
    {
        limiterOptions.PermitLimit = 10;
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueLimit = 0;
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRateLimiter();

// 5. Create the Weather Endpoint
app.MapGet("/api/weather/{city}", async (string city, IWeatherService weatherService) =>
{
    var weather = await weatherService.GetWeatherAsync(city);
    
    if (weather is null)
    {
        return Results.Problem(
            title: "Weather API Error",
            detail: $"Failed to fetch weather for '{city}'. Ensure the city code is valid.",
            statusCode: StatusCodes.Status400BadRequest
        );
    }

    return Results.Ok(weather);
})
.AddEndpointFilter<ApiKeyEndpointFilter>() // <-- Blocks requests lacking a valid X-API-Key header
.RequireRateLimiting("fixed")
.Produces(StatusCodes.Status200OK)
.ProducesProblem(StatusCodes.Status400BadRequest)
.ProducesProblem(StatusCodes.Status401Unauthorized)
.ProducesProblem(StatusCodes.Status429TooManyRequests)
.WithSummary("Get Weather Forecast")
.WithDescription("Fetches the current weather for a specified city code. Caches the response for 12 hours."); 

app.Run();