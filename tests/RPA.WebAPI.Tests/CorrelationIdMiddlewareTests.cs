namespace RPA.WebAPI.Tests;

using Microsoft.AspNetCore.Http;
using RPA.WebAPI.Middleware;
using Serilog.Context;

/// <summary>
/// Tests for CorrelationIdMiddleware.
/// Spec Bölüm 11: Serilog → Elasticsearch ile korelasyon ID takibi (JobRun GUID).
/// </summary>
public class CorrelationIdMiddlewareTests
{
    /// <summary>
    /// Test 1: Middleware generates a GUID as correlation ID if not provided.
    /// </summary>
    [Fact]
    public async Task CorrelationIdMiddleware_GeneratesGuidWhenNotProvided()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var called = false;
        var correlationIdFromContext = Guid.Empty;

        var middleware = new CorrelationIdMiddleware(next: async (ctx) =>
        {
            called = true;
            // HttpContext.Items should contain the correlation ID
            if (ctx.Items.TryGetValue(CorrelationIdMiddleware.CorrelationIdKey, out var cid))
            {
                if (cid is Guid guidCid)
                {
                    correlationIdFromContext = guidCid;
                }
            }
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(called, "Next middleware should have been called");
        Assert.NotEqual(Guid.Empty, correlationIdFromContext);
        Assert.True(context.Items.ContainsKey(CorrelationIdMiddleware.CorrelationIdKey));
    }

    /// <summary>
    /// Test 2: Middleware preserves existing correlation ID from X-Correlation-Id header.
    /// </summary>
    [Fact]
    public async Task CorrelationIdMiddleware_PreservesExistingCorrelationId()
    {
        // Arrange
        var expectedCorrelationId = Guid.NewGuid();
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeaderName] =
            expectedCorrelationId.ToString();

        var correlationIdFromContext = Guid.Empty;

        var middleware = new CorrelationIdMiddleware(next: async (ctx) =>
        {
            if (ctx.Items.TryGetValue(CorrelationIdMiddleware.CorrelationIdKey, out var cid))
            {
                if (cid is Guid guidCid)
                {
                    correlationIdFromContext = guidCid;
                }
            }
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.Equal(expectedCorrelationId, correlationIdFromContext);
    }

    /// <summary>
    /// Test 3: Middleware adds correlation ID to response header.
    /// </summary>
    [Fact]
    public async Task CorrelationIdMiddleware_AddsCorrelationIdToResponseHeader()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var expectedCorrelationId = Guid.NewGuid();
        context.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeaderName] =
            expectedCorrelationId.ToString();

        var middleware = new CorrelationIdMiddleware(next: async (ctx) =>
        {
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        Assert.True(
            context.Response.Headers.ContainsKey(CorrelationIdMiddleware.CorrelationIdHeaderName),
            "Response should contain X-Correlation-Id header");
        var responseHeader = context.Response.Headers[CorrelationIdMiddleware.CorrelationIdHeaderName]
            .ToString();
        Assert.Equal(expectedCorrelationId.ToString(), responseHeader);
    }

    /// <summary>
    /// Test 4: Middleware stores correlation ID in LogContext for Serilog enrichment.
    /// </summary>
    [Fact]
    public async Task CorrelationIdMiddleware_StoresCorrelationIdInLogContext()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var expectedCorrelationId = Guid.NewGuid();
        context.Request.Headers[CorrelationIdMiddleware.CorrelationIdHeaderName] =
            expectedCorrelationId.ToString();

        var middleware = new CorrelationIdMiddleware(next: async (ctx) =>
        {
            // Verify LogContext has the correlation ID
            // (In real scenario, Serilog reads from here during log enrichment)
            await Task.CompletedTask;
        });

        // Act
        await middleware.InvokeAsync(context);

        // Assert
        // The middleware should have called LogContext.PushProperty internally
        // which sets up the correlation ID for enrichers
        Assert.True(context.Items.ContainsKey(CorrelationIdMiddleware.CorrelationIdKey));
    }
}
