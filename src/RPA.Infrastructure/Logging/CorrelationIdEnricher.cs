namespace RPA.Infrastructure.Logging;

using Serilog.Core;
using Serilog.Events;

/// <summary>
/// Serilog enricher that adds the correlation ID to every log event.
/// Spec Bölüm 11: Her log'a correlation_id property'sini ekler; Elasticsearch'de sorgulanabilir.
///
/// This enricher reads the correlation ID from the LogContext (set by middleware)
/// and adds it as a property to each log event, enabling end-to-end request tracing.
///
/// The correlation ID is set by CorrelationIdMiddleware (WebAPI layer) via LogContext.PushProperty.
/// </summary>
public class CorrelationIdEnricher : ILogEventEnricher
{
    private const string CorrelationIdKey = "CorrelationId";
    private const string CorrelationIdPropertyName = "correlation_id";

    /// <summary>
    /// Enriches the log event with the correlation ID from the LogContext.
    /// </summary>
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Serilog's LogContext exposes pushed properties directly on the LogEvent
        // once the pipeline is configured with Enrich.FromLogContext() (see Serilog
        // setup in RPA.WebAPI). Rename the ambient "CorrelationId" property (pushed by
        // CorrelationIdMiddleware via LogContext.PushProperty) to the snake_case name
        // expected by Elasticsearch queries.
        if (logEvent.Properties.TryGetValue(CorrelationIdKey, out var correlationId))
        {
            var property = propertyFactory.CreateProperty(CorrelationIdPropertyName, correlationId);
            logEvent.AddPropertyIfAbsent(property);
        }
    }
}
