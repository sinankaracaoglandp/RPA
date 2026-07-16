namespace RPA.Infrastructure.Workflow;

using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;
using RPA.Domain.Entities;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow.Model;
using BusinessException = RPA.Domain.Exceptions.BusinessException;
using SystemException = RPA.Domain.Exceptions.SystemException;
using DomainLogLevel = RPA.Domain.Interfaces.LogLevel;

/// <summary>
/// Workflow/Component JSON'ını çalıştıran state machine (Spec Bölüm 5.2).
/// JSON → graf → topolojik doğrulama (döngü tespiti) → akış-güdümlü yürütme.
/// Kontrol akışı node'ları (if/forEach/while/tryCatch/assign/log/...) yerleşiktir;
/// <c>activity</c> node'ları <see cref="IActivityFactory"/> üzerinden çözülür.
/// </summary>
public sealed class BaseRunner : IWorkflowRunner
{
    private const int MaxWhileIterations = 1_000_000;

    private readonly WorkflowValidator _validator;
    private readonly ActivityCatalog _catalog;
    private readonly IActivityFactory _activityFactory;
    private readonly ILogger<BaseRunner> _logger;
    private readonly ICredentialVault? _vault;
    private readonly ICheckpointManager _checkpointManager;
    private readonly IWorkflowExecutionObserver? _observer;

    /// <summary>componentCall node'ları için: (componentId, version) → component JSON çözümleyici.</summary>
    private readonly Func<string, string?, string?>? _componentResolver;

    public BaseRunner(
        WorkflowValidator validator,
        ActivityCatalog catalog,
        IActivityFactory activityFactory,
        ILogger<BaseRunner> logger,
        ICredentialVault? vault = null,
        Func<string, string?, string?>? componentResolver = null,
        ICheckpointManager? checkpointManager = null,
        IWorkflowExecutionObserver? observer = null)
    {
        _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _activityFactory = activityFactory ?? throw new ArgumentNullException(nameof(activityFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _vault = vault;
        _componentResolver = componentResolver;
        _checkpointManager = checkpointManager ?? new CheckpointManager();
        _observer = observer;
    }

    /// <inheritdoc />
    public async Task<WorkflowExecutionResult> ExecuteAsync(
        WorkflowVersion workflowVersion,
        Dictionary<string, object?> arguments,
        Guid jobRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowVersion);
        var stopwatch = Stopwatch.StartNew();

        var validation = _validator.ValidateWorkflowJson(workflowVersion.JsonDefinition);
        if (!validation.IsValid)
        {
            var message = "Workflow JSON şema doğrulaması başarısız: " +
                          string.Join(" | ", validation.Errors);
            _logger.LogError("JobRun {JobRunId} — {Message}", jobRunId, message);
            return Fail(new SystemException(message), stopwatch);
        }

        WorkflowDefinition def;
        try
        {
            def = WorkflowDefinition.Parse(workflowVersion.JsonDefinition);
        }
        catch (Exception ex)
        {
            return Fail(new SystemException($"Workflow ayrıştırılamadı: {ex.Message}", ex), stopwatch);
        }

        return await ExecuteDefinitionAsync(
            def, arguments ?? new(), jobRunId, isolatedScope: null, stopwatch, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<WorkflowExecutionResult> ResumeAsync(
        WorkflowVersion workflowVersion,
        Dictionary<string, object?> arguments,
        string checkpointData,
        Guid jobRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflowVersion);
        // Boş/whitespace checkpoint = kayıtlı durum yok → baştan çalıştır (Deserialize null döner).
        var stopwatch = Stopwatch.StartNew();

        var validation = _validator.ValidateWorkflowJson(workflowVersion.JsonDefinition);
        if (!validation.IsValid)
        {
            var message = "Workflow JSON şema doğrulaması başarısız: " +
                          string.Join(" | ", validation.Errors);
            _logger.LogError("JobRun {JobRunId} — {Message}", jobRunId, message);
            return Fail(new SystemException(message), stopwatch);
        }

        WorkflowDefinition def;
        try
        {
            def = WorkflowDefinition.Parse(workflowVersion.JsonDefinition);
        }
        catch (Exception ex)
        {
            return Fail(new SystemException($"Workflow ayrıştırılamadı: {ex.Message}", ex), stopwatch);
        }

        var checkpoint = _checkpointManager.Deserialize(checkpointData);
        if (checkpoint is null)
        {
            _logger.LogInformation(
                "JobRun {JobRunId} — geçerli checkpoint bulunamadı, baştan çalıştırılıyor.", jobRunId);
            return await ExecuteDefinitionAsync(
                def, arguments ?? new(), jobRunId, isolatedScope: null, stopwatch, cancellationToken);
        }

        var resumeEntryNodeId = ResolveResumeEntryNodeId(def, checkpoint.LastCheckpointNodeId);
        _logger.LogInformation(
            "JobRun {JobRunId} — checkpoint node '{CheckpointNode}' sonrasından devam ediliyor (giriş: {ResumeNode}).",
            jobRunId, checkpoint.LastCheckpointNodeId, resumeEntryNodeId);

        return await ExecuteDefinitionAsync(
            def, arguments ?? new(), jobRunId, isolatedScope: null, stopwatch, cancellationToken,
            resumeEntryNodeId: resumeEntryNodeId,
            resumeVariables: checkpoint.Variables);
    }

    /// <summary>Checkpoint node'undan sonraki (varsayılan port) node ID'sini bulur — o node
    /// tekrar çalıştırılacak ilk node'dur; checkpoint'e kadar olan her şey atlanır.</summary>
    private static string? ResolveResumeEntryNodeId(WorkflowDefinition def, string? checkpointNodeId)
    {
        if (string.IsNullOrEmpty(checkpointNodeId))
        {
            return FindEntryNode(def);
        }
        return GetNext(def, checkpointNodeId, "success", "out");
    }

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>> InvokeComponentAsync(
        ComponentVersion componentVersion,
        Dictionary<string, object?> inputs,
        Guid jobRunId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(componentVersion);

        var validation = _validator.ValidateWorkflowJson(componentVersion.JsonDefinition);
        if (!validation.IsValid)
        {
            throw new SystemException(
                "Component JSON şema doğrulaması başarısız: " + string.Join(" | ", validation.Errors));
        }

        var def = WorkflowDefinition.Parse(componentVersion.JsonDefinition);

        // Component daima izole (taze) scope'ta çalışır — global sızıntı yok.
        var result = await ExecuteDefinitionAsync(
            def, inputs ?? new(), jobRunId, isolatedScope: new VariableScope(),
            Stopwatch.StartNew(), cancellationToken);

        if (!result.Success && result.Exception is not null)
        {
            throw result.Exception;
        }
        return result.Outputs;
    }

    // ----------------------------------------------------------------------
    // Çekirdek yürütme
    // ----------------------------------------------------------------------

    private async Task<WorkflowExecutionResult> ExecuteDefinitionAsync(
        WorkflowDefinition def,
        Dictionary<string, object?> arguments,
        Guid jobRunId,
        VariableScope? isolatedScope,
        Stopwatch stopwatch,
        CancellationToken ct,
        string? resumeEntryNodeId = null,
        IReadOnlyDictionary<string, object?>? resumeVariables = null)
    {
        if (!ValidateLoopGraph(def, out var loopError))
        {
            return Fail(new SystemException(loopError), stopwatch);
        }

        // Döngü tespiti (bağlantı grafı bir DAG olmalı; doğrulanmış loop-back kenarları hariç).
        if (HasCycle(def, out var cyclePath))
        {
            return Fail(
                new SystemException($"Workflow döngüsel bağımlılık içeriyor: {cyclePath}"), stopwatch);
        }

        var scope = isolatedScope ?? new VariableScope();
        InitializeVariables(def, arguments, scope);

        // Resume: checkpoint anındaki değişkenler, argüman varsayılanlarının üzerine yazılır
        // (çağrı zamanı argümanları en son uygulanmıştı — checkpoint bunları da ezer, çünkü
        // checkpoint yürütmenin en güncel bilinen durumudur).
        // Not: Empty dict (Count=0) should still import checkpoint state; only null means skip.
        if (resumeVariables != null)
        {
            scope.ImportGlobal(resumeVariables);
        }

        var context = new ActivityExecutionContext(scope, jobRunId, _logger, _vault);
        var state = new ExecutionState(def, scope, context, new ExpressionEvaluator(scope), ct);

        try
        {
            var entry = resumeEntryNodeId ?? FindEntryNode(def);
            if (entry is not null)
            {
                await RunSequenceAsync(entry, stopAfterId: null, state);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BusinessException bex)
        {
            _logger.LogWarning(bex, "JobRun {JobRunId} — İş kuralı istisnası: {Message}", jobRunId, bex.Message);
            return Fail(bex, stopwatch, BuildCheckpointData(state));
        }
        catch (SystemException sex)
        {
            _logger.LogError(sex, "JobRun {JobRunId} — Sistem istisnası: {Message}", jobRunId, sex.Message);
            return Fail(sex, stopwatch, BuildCheckpointData(state));
        }
        catch (Exception ex)
        {
            // Sınıflandırılmamış → sistem hatası (retry edilebilir).
            _logger.LogError(ex, "JobRun {JobRunId} — Beklenmeyen hata: {Message}", jobRunId, ex.Message);
            return Fail(new SystemException(ex.Message, ex), stopwatch, BuildCheckpointData(state));
        }

        var outputs = CollectOutputs(def, scope);
        stopwatch.Stop();
        return new WorkflowExecutionResult
        {
            Success = true,
            Outputs = outputs,
            DurationMs = stopwatch.ElapsedMilliseconds,
            CheckpointData = BuildCheckpointData(state),
        };
    }

    /// <summary>En az bir checkpoint node'u çalıştıysa serileştirilmiş durumu üretir; yoksa null.</summary>
    private string? BuildCheckpointData(ExecutionState state)
    {
        if (state.LastCheckpointNodeId is null)
        {
            return null;
        }
        return _checkpointManager.Serialize(state.LastCheckpointNodeId, state.Scope.ExportGlobal());
    }

    /// <summary>Bir düğüm dizisini akışı takip ederek çalıştırır. stopAfterId çalıştırıldıktan sonra durur.</summary>
    private async Task RunSequenceAsync(string? startId, string? stopAfterId, ExecutionState state)
    {
        var current = startId;
        var guard = 0;

        while (current is not null)
        {
            state.CancellationToken.ThrowIfCancellationRequested();

            if (++guard > MaxWhileIterations)
            {
                throw new SystemException("Yürütme adım sınırı aşıldı (olası sonsuz döngü).");
            }

            if (!state.Definition.NodesById.TryGetValue(current, out var node))
            {
                throw new SystemException($"Bağlantı hedefi node bulunamadı: '{current}'.");
            }

            var next = await ExecuteNodeAsync(node, state);

            if (stopAfterId is not null && current == stopAfterId)
            {
                break;
            }

            current = next;
        }
    }

    private async Task<string?> ExecuteNodeAsync(WorkflowNode node, ExecutionState state)
    {
        _logger.LogInformation("Node {NodeId} ({NodeType}) başlatıldı", node.Id, node.Type);
        NotifyStarted(new NodeExecutionEvent
        {
            JobRunId = state.Context.JobRunId,
            NodeId = node.Id,
            NodeType = node.Type,
            ActivityId = string.IsNullOrEmpty(node.Activity) ? null : node.Activity,
        });

        switch (node.Type)
        {
            case "assign":
                ExecuteAssign(node, state);
                return NextSequential(node, state);

            case "if":
                return ExecuteIf(node, state);

            case "forEach":
                await ExecuteForEachAsync(node, state);
                return GetLoopExit(state.Definition, node.Id);

            case "for":
                await ExecuteForAsync(node, state);
                return GetLoopExit(state.Definition, node.Id);

            case "while":
                await ExecuteWhileAsync(node, state);
                return GetLoopExit(state.Definition, node.Id);

            case "tryCatch":
                await ExecuteTryCatchAsync(node, state);
                return NextSequential(node, state);

            case "log":
                ExecuteLog(node, state);
                return NextSequential(node, state);

            case "delay":
                await Task.Delay(node.DurationMs ?? 0, state.CancellationToken);
                return NextSequential(node, state);

            case "checkpoint":
                ExecuteCheckpoint(node, state);
                return NextSequential(node, state);

            case "terminate":
                throw BuildTerminateException(node, state);

            case "userPrompt":
                return ExecuteUserPrompt(node, state);

            case "merge":
                // Çoklu upstream'i birleştiren geçiş node'u — durum değişikliği yok.
                return NextSequential(node, state);

            case "componentCall":
                await ExecuteComponentCallAsync(node, state);
                return NextSequential(node, state);

            case "activity":
                await ExecuteActivityAsync(node, state);
                return NextSequential(node, state);

            default:
                throw new SystemException($"Bilinmeyen node tipi: '{node.Type}' (node {node.Id}).");
        }
    }

    // ---- Node handler'ları ----

    private static void ExecuteAssign(WorkflowNode node, ExecutionState state)
    {
        if (string.IsNullOrEmpty(node.VariableName))
        {
            throw new SystemException($"assign node '{node.Id}' için variableName zorunlu.");
        }
        var value = EvaluateToken(node.Value, state);
        state.Scope.SetVariable(node.VariableName, value);
    }

    private string? ExecuteIf(WorkflowNode node, ExecutionState state)
    {
        var result = state.Evaluator.EvaluateCondition(node.Condition);
        _logger.LogInformation("Node {NodeId} koşulu {Result} olarak değerlendirildi", node.Id, result);
        var port = result ? "true" : "false";
        return GetNext(state.Definition, node.Id, port);
    }

    private async Task ExecuteForEachAsync(WorkflowNode node, ExecutionState state)
    {
        var (bodyStart, bodyEnd) = ResolveLoopBody(node, state.Definition);
        if (string.IsNullOrEmpty(bodyStart))
        {
            return;
        }

        var items = ResolveEnumerable(node.Items, state);
        var itemVar = node.ItemVariable ?? "item";

        foreach (var item in items)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            state.Scope.SetVariable(itemVar, item);
            await RunSequenceAsync(bodyStart, bodyEnd, state);
        }
    }

    private async Task ExecuteForAsync(WorkflowNode node, ExecutionState state)
    {
        var (bodyStart, bodyEnd) = ResolveLoopBody(node, state.Definition);
        if (string.IsNullOrEmpty(bodyStart))
        {
            return;
        }

        var start = node.Start ?? 0;
        var end = node.End ?? 0;
        var step = node.Step ?? 1;
        if (step == 0)
        {
            throw new SystemException($"for node '{node.Id}' step değeri sıfır olamaz.");
        }

        var indexVariable = node.IndexVariable ?? "index";
        var iterations = 0;
        for (var index = start; step > 0 ? index <= end : index >= end; index += step)
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            if (++iterations > MaxWhileIterations)
            {
                throw new SystemException($"for node '{node.Id}' iterasyon sınırını aştı.");
            }
            state.Scope.SetVariable(indexVariable, index);
            await RunSequenceAsync(bodyStart, bodyEnd, state);
        }
    }

    private async Task ExecuteWhileAsync(WorkflowNode node, ExecutionState state)
    {
        var (bodyStart, bodyEnd) = ResolveLoopBody(node, state.Definition);
        if (string.IsNullOrEmpty(bodyStart))
        {
            return;
        }

        var iterations = 0;
        while (state.Evaluator.EvaluateCondition(node.Condition))
        {
            state.CancellationToken.ThrowIfCancellationRequested();
            if (++iterations > MaxWhileIterations)
            {
                throw new SystemException($"while node '{node.Id}' iterasyon sınırını aştı.");
            }
            await RunSequenceAsync(bodyStart, bodyEnd, state);
        }
    }

    private static (string? Start, string? End) ResolveLoopBody(
        WorkflowNode node,
        WorkflowDefinition definition)
    {
        var start = definition.Connections.FirstOrDefault(c =>
            c.From == node.Id && string.Equals(c.FromPort, "body", StringComparison.OrdinalIgnoreCase))?.To
            ?? node.BodyStartNodeId;
        var end = definition.Connections.FirstOrDefault(c =>
            c.To == node.Id && string.Equals(c.ToPort, "loop-back", StringComparison.OrdinalIgnoreCase))?.From
            ?? node.BodyEndNodeId;
        return (start, end);
    }

    private async Task ExecuteTryCatchAsync(WorkflowNode node, ExecutionState state)
    {
        var tryStart = node.TryNodeId ?? node.BodyStartNodeId;

        try
        {
            if (!string.IsNullOrEmpty(tryStart))
            {
                await RunSequenceAsync(tryStart, node.BodyEndNodeId, state);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Node {NodeId} try bloğu istisnayı yakaladı: {Message}", node.Id, ex.Message);

            if (!string.IsNullOrEmpty(node.ExceptionVariable))
            {
                state.Scope.SetVariable(node.ExceptionVariable, ex.Message);
            }

            if (!string.IsNullOrEmpty(node.CatchNodeId))
            {
                await RunSequenceAsync(node.CatchNodeId, null, state);
            }
            else
            {
                throw;
            }
        }
        finally
        {
            if (!string.IsNullOrEmpty(node.FinallyNodeId))
            {
                await RunSequenceAsync(node.FinallyNodeId, null, state);
            }
        }
    }

    private void ExecuteLog(WorkflowNode node, ExecutionState state)
    {
        var message = state.Evaluator.EvaluateString(node.Message);
        var level = ParseLogLevel(node.Level);
        state.Context.Log(message, level);
    }

    private void ExecuteCheckpoint(WorkflowNode node, ExecutionState state)
    {
        var referenceKey = node.Properties.TryGetValue("referenceKey", out var rk)
            ? (string?)rk : node.Id;
        state.LastCheckpoint = referenceKey;
        // Resume noktası olarak node ID'si tutulur (referenceKey yalnızca tanılama/log içindir).
        state.LastCheckpointNodeId = node.Id;
        _logger.LogInformation("Node {NodeId} kontrol noktası kaydedildi: {Key}", node.Id, referenceKey);
    }

    private string? ExecuteUserPrompt(WorkflowNode node, ExecutionState state)
    {
        // Attended modda UI katmanı ele alır; unattended runner'da girişi sağlayamaz.
        state.Context.Log(
            $"Kullanıcı girişi istendi: {node.PromptTitle}", DomainLogLevel.Warning);
        throw new BusinessException(
            $"userPrompt node '{node.Id}' unattended modda çalıştırılamaz (kullanıcı girişi gerekli).");
    }

    private Exception BuildTerminateException(WorkflowNode node, ExecutionState state)
    {
        var message = string.IsNullOrEmpty(node.Message)
            ? "Workflow sonlandırıldı."
            : state.Evaluator.EvaluateString(node.Message);

        return string.Equals(node.ExceptionType, "Business", StringComparison.OrdinalIgnoreCase)
            ? new BusinessException(message)
            : new SystemException(message);
    }

    private async Task ExecuteActivityAsync(WorkflowNode node, ExecutionState state)
    {
        if (string.IsNullOrEmpty(node.Activity))
        {
            throw new SystemException($"activity node '{node.Id}' için activity ID zorunlu.");
        }

        var metadata = _catalog.GetActivityMetadata(node.Activity)
            ?? throw new SystemException(
                $"Aktivite katalogda bulunamadı: '{node.Activity}' (node {node.Id}).");

        var activity = _activityFactory.CreateActivity(node.Activity)
            ?? throw new SystemException(
                $"Aktivite implementasyonu kayıtlı değil: '{node.Activity}' (node {node.Id}).");

        // Node property'lerini çözerek node-local scope'a giriş değişkenleri olarak yaz.
        state.Scope.PushScope($"node-{node.Id}");
        Dictionary<string, object?> outputs;
        var resolvedInputs = new Dictionary<string, object?>();
        try
        {
            foreach (var (key, token) in node.Properties)
            {
                var value = EvaluateToken(token, state);
                state.Scope.SetLocalVariable(key, value);
                resolvedInputs[key] = value;
            }

            try
            {
                outputs = await activity.ExecuteAsync(state.Context) ?? new();
            }
            catch (BusinessException bex)
            {
                NotifyActivityError(state, node, metadata, resolvedInputs, bex.Message, isBusiness: true);
                throw;
            }
            catch (SystemException sex)
            {
                NotifyActivityError(state, node, metadata, resolvedInputs, sex.Message, isBusiness: false);
                throw;
            }
            catch (Exception ex)
            {
                NotifyActivityError(state, node, metadata, resolvedInputs, ex.Message, isBusiness: false);
                throw;
            }
        }
        finally
        {
            state.Scope.PopScope();
        }

        // Çıkışları üst scope'a yaz (downstream node'lar ve workflow Output'ları için).
        foreach (var (key, value) in outputs)
        {
            state.Scope.SetVariable(key, value);
        }

        _logger.LogInformation(
            "Node {NodeId} tamamlandı ({Activity}), çıkış anahtarları: {Keys}",
            node.Id, metadata.ActivityId, string.Join(",", outputs.Keys));

        NotifyCompleted(new NodeExecutionEvent
        {
            JobRunId = state.Context.JobRunId,
            NodeId = node.Id,
            NodeType = node.Type,
            ActivityId = metadata.ActivityId,
            Inputs = MaskAndStringify(resolvedInputs, metadata),
            Outputs = MaskAndStringify(outputs, metadata),
        });
    }

    private void NotifyActivityError(
        ExecutionState state,
        WorkflowNode node,
        ActivityMetadata metadata,
        IReadOnlyDictionary<string, object?> inputs,
        string message,
        bool isBusiness)
    {
        NotifyCompleted(new NodeExecutionEvent
        {
            JobRunId = state.Context.JobRunId,
            NodeId = node.Id,
            NodeType = node.Type,
            ActivityId = metadata.ActivityId,
            Inputs = MaskAndStringify(inputs, metadata),
            Error = message,
            IsBusinessError = isBusiness,
        });
    }

    // ---- Gözlemci (canlı konsol) yardımcıları ----

    private void NotifyStarted(NodeExecutionEvent evt)
    {
        if (_observer is null)
        {
            return;
        }
        try { _observer.OnNodeStarted(evt); }
        catch (Exception ex) { _logger.LogDebug(ex, "Gözlemci OnNodeStarted hatası (yok sayıldı)."); }
    }

    private void NotifyCompleted(NodeExecutionEvent evt)
    {
        if (_observer is null)
        {
            return;
        }
        try { _observer.OnNodeCompleted(evt); }
        catch (Exception ex) { _logger.LogDebug(ex, "Gözlemci OnNodeCompleted hatası (yok sayıldı)."); }
    }

    /// <summary>
    /// Değerleri görüntülenebilir (maskeli/kısaltılmış) string'lere çevirir. Credential tipli
    /// parametreler ve gizli görünen anahtarlar (password/secret/token/credential) asla açılmaz.
    /// </summary>
    private static Dictionary<string, string?> MaskAndStringify(
        IEnumerable<KeyValuePair<string, object?>> values, ActivityMetadata? metadata)
    {
        var credentialKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (metadata is not null)
        {
            foreach (var p in metadata.Inputs.Concat(metadata.Outputs))
            {
                if (string.Equals(p.Type, "Credential", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(p.Type, "Sensitive", StringComparison.OrdinalIgnoreCase))
                {
                    credentialKeys.Add(p.Name);
                }
            }
        }

        var result = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (key, value) in values)
        {
            result[key] = credentialKeys.Contains(key) || LooksSecret(key)
                ? "[MASKED]"
                : Preview(value);
        }
        return result;
    }

    private static bool LooksSecret(string key)
        => key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Contains("credential", StringComparison.OrdinalIgnoreCase);

    private static string? Preview(object? value)
    {
        if (value is null)
        {
            return null;
        }
        var text = value as string ?? value.ToString();
        if (text is null)
        {
            return null;
        }
        const int max = 200;
        return text.Length > max ? string.Concat(text.AsSpan(0, max), "…") : text;
    }

    private async Task ExecuteComponentCallAsync(WorkflowNode node, ExecutionState state)
    {
        if (_componentResolver is null)
        {
            throw new SystemException(
                $"componentCall node '{node.Id}' — component çözümleyici yapılandırılmamış (Task 2.4).");
        }

        var componentId = node.ComponentId ?? "";
        var json = _componentResolver(componentId, node.ComponentVersion)
            ?? throw new SystemException(
                $"Component bulunamadı: {node.ComponentId ?? "(null)"} v{node.ComponentVersion}.");

        // Giriş eşlemesi: {componentInput: workflowVariable}
        var inputs = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (compInput, wfVar) in node.InputMapping)
        {
            state.Scope.TryGetVariable(wfVar, out var v);
            inputs[compInput] = v;
        }

        var componentVersion = new ComponentVersion
        {
            ComponentId = Guid.TryParse(node.ComponentId, out var cid) ? cid : Guid.Empty,
            Version = node.ComponentVersion ?? "1.0.0",
            JsonDefinition = json,
        };

        var outputs = await InvokeComponentAsync(
            componentVersion, inputs, state.Context.JobRunId, state.CancellationToken);

        // Çıkış eşlemesi: {componentOutput: workflowVariable}
        foreach (var (compOutput, wfVar) in node.OutputMapping)
        {
            outputs.TryGetValue(compOutput, out var v);
            state.Scope.SetVariable(wfVar, v);
        }
    }

    // ----------------------------------------------------------------------
    // Yardımcılar
    // ----------------------------------------------------------------------

    private void InitializeVariables(
        WorkflowDefinition def, Dictionary<string, object?> arguments, VariableScope scope)
    {
        // 1) Bildirilen değişken varsayılanları.
        foreach (var v in def.Variables)
        {
            scope.SetGlobalVariable(v.Name, v.Default is null ? null : VariableScope.JTokenToNative(v.Default));
        }

        // 2) Giriş argümanı varsayılanları.
        foreach (var a in def.InputArguments)
        {
            if (a.Default is not null)
            {
                scope.SetGlobalVariable(a.Name, VariableScope.JTokenToNative(a.Default));
            }
        }

        // 3) Sağlanan argümanlar (varsayılanları ezer).
        foreach (var (key, value) in arguments)
        {
            scope.SetGlobalVariable(key, value);
        }
    }

    private static Dictionary<string, object?> CollectOutputs(WorkflowDefinition def, VariableScope scope)
    {
        var outputs = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (def.OutputArguments.Count == 0)
        {
            return outputs;
        }

        foreach (var arg in def.OutputArguments)
        {
            if (scope.TryGetVariable(arg.Name, out var value))
            {
                outputs[arg.Name] = value;
            }
            else if (arg.Default is not null)
            {
                outputs[arg.Name] = VariableScope.JTokenToNative(arg.Default);
            }
        }

        return outputs;
    }

    private string? NextSequential(WorkflowNode node, ExecutionState state)
        => GetNext(state.Definition, node.Id, "success", "out");

    private static string? GetLoopExit(WorkflowDefinition definition, string nodeId)
    {
        var exit = definition.Connections.FirstOrDefault(connection =>
            connection.From == nodeId
            && string.Equals(connection.FromPort, "exit", StringComparison.OrdinalIgnoreCase))?.To;
        if (exit is not null)
        {
            return exit;
        }

        var usesLoopPorts = definition.Connections.Any(connection =>
            connection.From == nodeId
            && string.Equals(connection.FromPort, "body", StringComparison.OrdinalIgnoreCase));
        return usesLoopPorts ? null : GetNext(definition, nodeId, "success", "out");
    }

    /// <summary>Belirtilen port'lardan ilk eşleşen bağlantının hedefini döndürür; yoksa herhangi bir bağlantı.</summary>
    private static string? GetNext(WorkflowDefinition def, string nodeId, params string[] preferredPorts)
    {
        var outgoing = def.Connections.Where(c => c.From == nodeId).ToList();
        if (outgoing.Count == 0)
        {
            return null;
        }

        foreach (var port in preferredPorts)
        {
            var match = outgoing.FirstOrDefault(c =>
                string.Equals(c.FromPort, port, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match.To;
            }
        }

        // Tercih edilen port yoksa: 'true'/'false' gibi dallanma portu olmayan ilk bağlantı.
        return outgoing[0].To;
    }

    private static object? EvaluateToken(JToken? token, ExecutionState state)
    {
        if (token is null)
        {
            return null;
        }
        // String değerler ifade (${...}) olabilir; diğer JSON tipleri doğrudan alınır.
        if (token.Type == JTokenType.String)
        {
            return state.Evaluator.EvaluateValue((string?)token);
        }
        return VariableScope.JTokenToNative(token);
    }

    private static IEnumerable<object?> ResolveEnumerable(string? itemsVariable, ExecutionState state)
    {
        if (string.IsNullOrEmpty(itemsVariable))
        {
            return Enumerable.Empty<object?>();
        }

        object? value;
        if (itemsVariable.Contains("{{", StringComparison.Ordinal) ||
            itemsVariable.Contains("${", StringComparison.Ordinal))
        {
            value = state.Evaluator.EvaluateValue(itemsVariable);
        }
        else if (!state.Scope.TryGetVariable(itemsVariable, out value))
        {
            return Enumerable.Empty<object?>();
        }

        if (value is null)
        {
            return Enumerable.Empty<object?>();
        }

        return value switch
        {
            JArray arr => arr.Select(t => VariableScope.JTokenToNative(t)).ToList(),
            System.Collections.IEnumerable e and not string =>
                e.Cast<object?>().Select(x => x is JToken t ? VariableScope.JTokenToNative(t) : x).ToList(),
            _ => new[] { value },
        };
    }

    private static DomainLogLevel ParseLogLevel(string? level)
        => Enum.TryParse<DomainLogLevel>(level, ignoreCase: true, out var parsed)
            ? parsed
            : DomainLogLevel.Information;

    private static WorkflowExecutionResult Fail(Exception ex, Stopwatch stopwatch, string? checkpointData = null)
    {
        stopwatch.Stop();
        return new WorkflowExecutionResult
        {
            Success = false,
            Exception = ex,
            DurationMs = stopwatch.ElapsedMilliseconds,
            CheckpointData = checkpointData,
        };
    }

    // ---- Döngü tespiti (DFS renklendirme) ----

    private static bool ValidateLoopGraph(WorkflowDefinition definition, out string error)
    {
        error = "";
        var loopTypes = new HashSet<string>(new[] { "while", "for", "forEach" }, StringComparer.OrdinalIgnoreCase);
        var loopBacks = definition.Connections
            .Where(c => string.Equals(c.ToPort, "loop-back", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var connection in loopBacks)
        {
            if (!definition.NodesById.TryGetValue(connection.To, out var owner) || !loopTypes.Contains(owner.Type))
            {
                error = $"loop-back hedefi bir loop node olmalıdır: '{connection.To}'.";
                return false;
            }
        }

        foreach (var loop in definition.Nodes.Where(node => loopTypes.Contains(node.Type)))
        {
            var body = definition.Connections.Where(c =>
                c.From == loop.Id && string.Equals(c.FromPort, "body", StringComparison.OrdinalIgnoreCase)).ToList();
            var exits = definition.Connections.Where(c =>
                c.From == loop.Id && string.Equals(c.FromPort, "exit", StringComparison.OrdinalIgnoreCase)).ToList();
            var backs = loopBacks.Where(c => c.To == loop.Id).ToList();
            var usesPorts = body.Count > 0 || exits.Count > 0 || backs.Count > 0;
            if (!usesPorts)
            {
                continue;
            }
            if (body.Count != 1 || backs.Count != 1 || exits.Count > 1)
            {
                error = $"loop node '{loop.Id}' tam bir body ve loop-back, en fazla bir exit bağlantısı taşımalıdır.";
                return false;
            }
            if (!AllPathsLeadToTarget(
                    body[0].To,
                    backs[0].From,
                    definition,
                    loop.Id,
                    new HashSet<string>(StringComparer.Ordinal),
                    new Dictionary<string, bool>(StringComparer.Ordinal)))
            {
                error = $"loop-back kaynağı '{backs[0].From}', loop '{loop.Id}' body akışına ait değildir.";
                return false;
            }
        }
        return true;
    }

    private static bool AllPathsLeadToTarget(
        string current,
        string target,
        WorkflowDefinition definition,
        string loopId,
        HashSet<string> visiting,
        Dictionary<string, bool> memo)
    {
        if (current == target)
        {
            return true;
        }
        if (memo.TryGetValue(current, out var cached))
        {
            return cached;
        }
        if (!visiting.Add(current))
        {
            return false;
        }
        var outgoing = definition.Connections.Where(c =>
            c.From == current &&
            !string.Equals(c.ToPort, "loop-back", StringComparison.OrdinalIgnoreCase) &&
            c.To != loopId).ToList();
        var result = outgoing.Count > 0 && outgoing.All(connection =>
            AllPathsLeadToTarget(connection.To, target, definition, loopId, visiting, memo));
        visiting.Remove(current);
        memo[current] = result;
        return result;
    }

    private static bool HasCycle(WorkflowDefinition def, out string cyclePath)
    {
        cyclePath = "";
        var adjacency = def.Connections
            .Where(c => !string.Equals(c.ToPort, "loop-back", StringComparison.OrdinalIgnoreCase))
            .GroupBy(c => c.From)
            .ToDictionary(g => g.Key, g => g.Select(c => c.To).ToList());

        var state = new Dictionary<string, int>(StringComparer.Ordinal); // 0=beyaz,1=gri,2=siyah
        var stack = new List<string>();

        foreach (var node in def.Nodes)
        {
            if (Visit(node.Id, adjacency, state, stack, out cyclePath))
            {
                return true;
            }
        }
        return false;
    }

    private static bool Visit(
        string nodeId,
        Dictionary<string, List<string>> adjacency,
        Dictionary<string, int> state,
        List<string> stack,
        out string cyclePath)
    {
        cyclePath = "";
        state.TryGetValue(nodeId, out var color);
        if (color == 2)
        {
            return false;
        }
        if (color == 1)
        {
            var idx = stack.IndexOf(nodeId);
            cyclePath = string.Join(" → ", stack.Skip(idx < 0 ? 0 : idx).Append(nodeId));
            return true;
        }

        state[nodeId] = 1;
        stack.Add(nodeId);

        if (adjacency.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var next in neighbors)
            {
                if (Visit(next, adjacency, state, stack, out cyclePath))
                {
                    return true;
                }
            }
        }

        stack.RemoveAt(stack.Count - 1);
        state[nodeId] = 2;
        return false;
    }

    private static string? FindEntryNode(WorkflowDefinition def)
    {
        if (def.Nodes.Count == 0)
        {
            return null;
        }

        var hasIncoming = new HashSet<string>(
            def.Connections
                .Where(c => !string.Equals(c.ToPort, "loop-back", StringComparison.OrdinalIgnoreCase))
                .Select(c => c.To),
            StringComparer.Ordinal);
        var entry = def.Nodes.FirstOrDefault(n => !hasIncoming.Contains(n.Id));
        return entry?.Id ?? def.Nodes[0].Id;
    }

    /// <summary>Tek bir yürütmenin paylaşılan durumu.</summary>
    private sealed class ExecutionState
    {
        public ExecutionState(
            WorkflowDefinition definition,
            VariableScope scope,
            ActivityExecutionContext context,
            ExpressionEvaluator evaluator,
            CancellationToken cancellationToken)
        {
            Definition = definition;
            Scope = scope;
            Context = context;
            Evaluator = evaluator;
            CancellationToken = cancellationToken;
        }

        public WorkflowDefinition Definition { get; }
        public VariableScope Scope { get; }
        public ActivityExecutionContext Context { get; }
        public ExpressionEvaluator Evaluator { get; }
        public CancellationToken CancellationToken { get; }
        public string? LastCheckpoint { get; set; }

        /// <summary>Bu yürütmede en son çalıştırılan checkpoint node'unun ID'si (resume anchor). null = hiç çalışmadı.</summary>
        public string? LastCheckpointNodeId { get; set; }
    }
}
