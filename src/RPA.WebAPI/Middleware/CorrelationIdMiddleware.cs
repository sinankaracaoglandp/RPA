namespace RPA.WebAPI.Middleware;

using Microsoft.AspNetCore.Http;
using Serilog.Context;

/// <summary>
/// Middleware that ensures each HTTP request has a unique correlation ID for distributed logging.
/// Spec Bölüm 11: Serilog → Elasticsearch korelasyon ID takibi (JobRun GUID).
///
/// The middleware:
/// 1. Checks for an existing X-Correlation-Id header in the request.
/// 2. If not provided, generates a new GUID.
/// 3. Stores the correlation ID in Serilog LogContext for access throughout the request.
/// 4. Adds the correlation ID to the response header.
/// </summary>
public class CorrelationIdMiddleware
{
    public const string CorrelationIdKey = "CorrelationId";
    public const string CorrelationIdHeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    /// <summary>
    /// Invokes the middleware to set correlation ID on the request and response.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Extract existing correlation ID from request header or generate new one.
        var correlationId = ExtractOrGenerateCorrelationId(context);

        // Store in Serilog LogContext for access by enricher throughout the request.
        using (LogContext.PushProperty(CorrelationIdKey, correlationId))
        {
            // Also store in HttpContext.Items for direct access if needed.
            context.Items[CorrelationIdKey] = correlationId;

            // Add correlation ID to response header (set immediately for testing/direct access)
            if (!context.Response.HasStarted)
            {
                context.Response.Headers[CorrelationIdHeaderName] = correlationId.ToString();
            }
            else
            {
                // If response has already started, use OnStarting callback
                context.Response.OnStarting(() =>
                {
                    if (!context.Response.Headers.ContainsKey(CorrelationIdHeaderName))
                    {
                        context.Response.Headers[CorrelationIdHeaderName] = correlationId.ToString();
                    }
                    return Task.CompletedTask;
                });
            }

            await _next(context);
        }
    }

    /// <summary>
    /// Extracts correlation ID from request header, or generates a new GUID if not present.
    /// </summary>
    private static Guid ExtractOrGenerateCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(CorrelationIdHeaderName, out var headerValue))
        {
            if (Guid.TryParse(headerValue.ToString(), out var parsedId))
            {
                return parsedId;
            }
        }

        return Guid.NewGuid();
    }
}
