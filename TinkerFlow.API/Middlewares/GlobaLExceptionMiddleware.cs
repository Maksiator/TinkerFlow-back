using System.Net;
using System.Text.Json;

namespace TinkerFlow.API.Middlewares;

public class GlobaLExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobaLExceptionMiddleware> _logger;

    public GlobaLExceptionMiddleware(RequestDelegate next, ILogger<GlobaLExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }
    
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Wystapił nieoczekiwany błąd podczas przetwarzania żądania.");
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;

        var response = new
        {
            message = "Wystąpił nieoczekiwany błąd. Prosimy spróbować ponownie później."
        };

        var json = JsonSerializer.Serialize(response);
        return context.Response.WriteAsync(json);
    }
}