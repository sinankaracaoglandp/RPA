namespace RPA.Infrastructure.Services;

using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Entities;
using RPA.Domain.Enums;
using RPA.Infrastructure.Persistence;
using BusinessException = RPA.Domain.Exceptions.BusinessException;

/// <summary>
/// Studio'dan gelen "Run" isteğini Agent'ın mevcut kuyruk yürütme yoluna bağlar.
/// Draft workflow versiyonu, QueueItem.Payload içinde AgentJobPayloadParser formatıyla taşınır.
/// </summary>
public sealed class WorkflowRunService
{
    public const string StudioRunQueueName = "StudioRun";

    private readonly RpaDbContext _db;

    public WorkflowRunService(RpaDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<WorkflowRunRequestResult> EnqueueDraftAsync(
        Guid workflowId,
        IReadOnlyDictionary<string, object?>? arguments = null,
        CancellationToken cancellationToken = default)
    {
        var workflow = await _db.Workflows
            .FirstOrDefaultAsync(w => w.Id == workflowId && !w.IsDeleted, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BusinessException($"Workflow bulunamadı: {workflowId}");

        var draft = await _db.WorkflowVersions
            .FirstOrDefaultAsync(v =>
                v.WorkflowId == workflowId &&
                v.Status == ComponentStatus.Draft &&
                !v.IsDeleted,
                cancellationToken)
            .ConfigureAwait(false)
            ?? throw new BusinessException($"Workflow taslağı bulunamadı: {workflowId}");

        var queue = await GetOrCreateStudioQueueAsync(workflow.ProjectId, cancellationToken)
            .ConfigureAwait(false);

        var item = new QueueItem
        {
            Id = Guid.NewGuid(),
            QueueId = queue.Id,
            IdempotencyKey = $"studio-run:{workflowId}:{Guid.NewGuid()}",
            Status = QueueItemStatus.New,
            Payload = BuildPayload(draft, arguments ?? new Dictionary<string, object?>()),
        };

        await _db.QueueItems.AddAsync(item, cancellationToken).ConfigureAwait(false);
        await _db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new WorkflowRunRequestResult(item.Id, queue.Id, item.Status.ToString());
    }

    private async Task<Queue> GetOrCreateStudioQueueAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var queue = await _db.Queues
            .FirstOrDefaultAsync(q =>
                q.Name == StudioRunQueueName &&
                !q.IsDeleted,
                cancellationToken)
            .ConfigureAwait(false);

        if (queue is not null)
        {
            return queue;
        }

        queue = new Queue
        {
            Id = Guid.NewGuid(),
            ProjectId = projectId,
            Name = StudioRunQueueName,
            MaxRetries = 0,
            RequireIdempotency = true,
        };
        await _db.Queues.AddAsync(queue, cancellationToken).ConfigureAwait(false);
        return queue;
    }

    private static string BuildPayload(
        WorkflowVersion draft,
        IReadOnlyDictionary<string, object?> arguments)
    {
        using var definition = ParseDraftDefinition(draft);
        return JsonSerializer.Serialize(new
        {
            workflowVersionId = draft.Id,
            version = draft.Version,
            environmentId = draft.EnvironmentId,
            jsonDefinition = definition.RootElement,
            arguments,
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }

    private static JsonDocument ParseDraftDefinition(WorkflowVersion draft)
    {
        try
        {
            return JsonDocument.Parse(draft.JsonDefinition);
        }
        catch (JsonException ex)
        {
            throw new BusinessException($"Workflow taslağı geçersiz JSON içeriyor: {draft.WorkflowId}", ex);
        }
    }
}

public sealed record WorkflowRunRequestResult(Guid QueueItemId, Guid QueueId, string Status);
