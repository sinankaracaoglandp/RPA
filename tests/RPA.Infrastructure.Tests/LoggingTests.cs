namespace RPA.Infrastructure.Tests;

using RPA.Infrastructure.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

/// <summary>
/// Tests for CorrelationIdEnricher.
/// Spec Bölüm 11: Serilog → Elasticsearch ile korelasyon ID takibi.
///
/// Note: CorrelationIdMiddleware tests are in RPA.WebAPI.Tests since the middleware
/// is part of the web layer (CorrelationIdMiddleware.cs).
/// </summary>
public class LoggingTests
{
    /// <summary>
    /// Test 1: CorrelationIdEnricher adds correlation_id property to logs when LogEvent has CorrelationId.
    /// </summary>
    [Fact]
    public void CorrelationIdEnricher_EnrichesLogWithCorrelationId()
    {
        // Arrange
        var expectedCorrelationId = Guid.NewGuid();
        var enricher = new CorrelationIdEnricher();

        // Create a log event with CorrelationId property already added
        var logProperties = new List<LogEventProperty>
        {
            new("CorrelationId", new ScalarValue(expectedCorrelationId))
        };

        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("Test message"),
            logProperties);

        // Create a simple property factory implementation
        var propertyFactory = new SimplePropertyFactory();

        // Act
        enricher.Enrich(logEvent, propertyFactory);

        // Assert
        Assert.True(logEvent.Properties.ContainsKey("correlation_id"),
            "Log event should contain correlation_id property");
        var correlationIdProp = logEvent.Properties["correlation_id"];
        Assert.NotNull(correlationIdProp);
    }

    /// <summary>
    /// Test 2: CorrelationIdEnricher does not add correlation_id property when CorrelationId is not in LogEvent.
    /// </summary>
    [Fact]
    public void CorrelationIdEnricher_NoEnrichment_WhenCorrelationIdMissing()
    {
        // Arrange
        var enricher = new CorrelationIdEnricher();

        var logEvent = new LogEvent(
            DateTimeOffset.UtcNow,
            LogEventLevel.Information,
            null,
            new MessageTemplateParser().Parse("Test message"),
            new List<LogEventProperty>());

        var propertyFactory = new SimplePropertyFactory();

        // Act
        enricher.Enrich(logEvent, propertyFactory);

        // Assert
        Assert.False(logEvent.Properties.ContainsKey("correlation_id"),
            "Log event should NOT contain correlation_id property when CorrelationId is missing");
        Assert.False(propertyFactory.CreatePropertyCalled,
            "CreateProperty should not have been called when CorrelationId is missing");
    }

    /// <summary>
    /// Simple property factory for testing.
    /// </summary>
    private class SimplePropertyFactory : ILogEventPropertyFactory
    {
        public bool CreatePropertyCalled { get; private set; }

        public LogEventProperty CreateProperty(string name, object? value, bool destructureObjects = false)
        {
            CreatePropertyCalled = true;
            return new LogEventProperty(name, new ScalarValue(value));
        }
    }
}
