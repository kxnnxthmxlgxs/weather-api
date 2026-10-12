using System.Security.Cryptography;
using System.Text;

namespace WeatherApi.Auth;

public class ApiKeyEndpointFilter : IEndpointFilter
{
    private readonly IConfiguration _config;

    public ApiKeyEndpointFilter(IConfiguration config)
    {
        _config = config;
    }

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        // 1. Check if the header exists
        if (!context.HttpContext.Request.Headers.TryGetValue("X-API-Key", out var extractedApiKey))
        {
            return Results.Problem(
                title: "Unauthorized",
                detail: "API Key is missing. Please provide an 'X-API-Key' header.",
                statusCode: StatusCodes.Status401Unauthorized
            );
        }

        // 2. Get the expected key from our user secrets / environment variables
        var expectedApiKey = _config["ClientApiKey"]; 
        if (string.IsNullOrEmpty(expectedApiKey))
        {
            return Results.Problem(title: "Server Configuration Error", statusCode: StatusCodes.Status500InternalServerError);
        }

        // 3. Prevent Timing Attacks using FixedTimeEquals
        var expectedKeyBytes = Encoding.UTF8.GetBytes(expectedApiKey);
        var providedKeyBytes = Encoding.UTF8.GetBytes(extractedApiKey.ToString());

        if (expectedKeyBytes.Length != providedKeyBytes.Length || 
            !CryptographicOperations.FixedTimeEquals(expectedKeyBytes, providedKeyBytes))
        {
            return Results.Problem(
                title: "Unauthorized",
                detail: "Invalid API Key.",
                statusCode: StatusCodes.Status401Unauthorized
            );
        }

        // 4. Key is valid, continue to the actual endpoint
        return await next(context);
    }
}