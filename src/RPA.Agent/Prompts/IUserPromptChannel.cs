namespace RPA.Agent.Prompts;

/// <summary>
/// Workflow motoru (UserPrompt node) tarafından çağrılan kanal. UI çatısından bağımsızdır —
/// gerçek modal pencere <see cref="UserPromptService.PromptRaised"/> olayına abone olarak devreye girer.
/// </summary>
public interface IUserPromptChannel
{
    /// <summary>
    /// İsteği yayınlar ve kullanıcı cevabını (veya zaman aşımında null) asenkron olarak bekler.
    /// </summary>
    Task<UserPromptResponse?> RequestAsync(UserPromptRequest request, CancellationToken cancellationToken = default);
}
