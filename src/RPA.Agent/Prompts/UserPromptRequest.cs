namespace RPA.Agent.Prompts;

/// <summary>
/// Workflow yürütmesi sırasında attended kullanıcıdan girdi istemek için üretilen istek
/// (Spec Bölüm 9 — UserPrompt node). <see cref="IUserPromptChannel.RequestAsync"/> bu isteği
/// yayınlar; UI (modal pencere) kullanıcıdan cevabı toplayıp <see cref="UserPromptService.Submit"/>
/// ile geri bildirir.
/// </summary>
public sealed class UserPromptRequest
{
    public UserPromptRequest(
        string title,
        string message,
        UserPromptKind kind,
        IReadOnlyList<string>? choices = null,
        TimeSpan? timeout = null)
    {
        Id = Guid.NewGuid();
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Message = message ?? throw new ArgumentNullException(nameof(message));
        Kind = kind;
        Choices = choices ?? Array.Empty<string>();
        Timeout = timeout ?? TimeSpan.FromMinutes(5); // Spec: 5 dk timeout.
    }

    public Guid Id { get; }
    public string Title { get; }
    public string Message { get; }
    public UserPromptKind Kind { get; }
    public IReadOnlyList<string> Choices { get; }
    public TimeSpan Timeout { get; }
}
