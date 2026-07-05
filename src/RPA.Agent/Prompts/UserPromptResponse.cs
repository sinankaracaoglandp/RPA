namespace RPA.Agent.Prompts;

/// <summary>Kullanıcının UserPrompt modalına verdiği cevap (veya zaman aşımı sonucu).</summary>
public sealed class UserPromptResponse
{
    public UserPromptResponse(Guid requestId, bool confirmed, string? text = null, string? selectedChoice = null)
    {
        RequestId = requestId;
        Confirmed = confirmed;
        Text = text;
        SelectedChoice = selectedChoice;
    }

    public Guid RequestId { get; }

    /// <summary>Confirmation türü için onay; Text/Choice türlerinde "iptal edilmedi" anlamına gelir.</summary>
    public bool Confirmed { get; }

    public string? Text { get; }
    public string? SelectedChoice { get; }
}
