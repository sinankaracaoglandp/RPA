namespace RPA.Agent.Tests.Prompts;

using Microsoft.Extensions.Logging.Abstractions;
using RPA.Agent.Prompts;

public class UserPromptServiceTests
{
    private static UserPromptService CreateService() => new(NullLogger<UserPromptService>.Instance);

    [Fact]
    public async Task Submit_Zamaninda_Cagrilirsa_RequestAsync_Cevabi_Dondurur()
    {
        var service = CreateService();
        var request = new UserPromptRequest("Başlık", "Mesaj", UserPromptKind.Text, timeout: TimeSpan.FromSeconds(5));

        UserPromptRequest? raised = null;
        service.PromptRaised += r => raised = r;

        var task = service.RequestAsync(request);
        // PromptRaised senkron olarak RequestAsync içinde tetiklenir.
        Assert.NotNull(raised);
        Assert.Equal(request.Id, raised!.Id);

        var submitted = service.Submit(new UserPromptResponse(request.Id, confirmed: true, text: "cevap"));
        Assert.True(submitted);

        var response = await task;
        Assert.NotNull(response);
        Assert.Equal("cevap", response!.Text);
    }

    [Fact]
    public async Task Zaman_Asimi_Dolunca_Null_Doner()
    {
        var service = CreateService();
        var request = new UserPromptRequest("Başlık", "Mesaj", UserPromptKind.Confirmation, timeout: TimeSpan.FromMilliseconds(50));

        var response = await service.RequestAsync(request);

        Assert.Null(response);
    }

    [Fact]
    public void Bilinmeyen_Istek_Icin_Submit_False_Doner()
    {
        var service = CreateService();
        var submitted = service.Submit(new UserPromptResponse(Guid.NewGuid(), confirmed: false));
        Assert.False(submitted);
    }

    [Fact]
    public async Task Zaman_Asimindan_Sonra_Submit_Gec_Kalirsa_Etkisizdir()
    {
        var service = CreateService();
        var request = new UserPromptRequest("Başlık", "Mesaj", UserPromptKind.Text, timeout: TimeSpan.FromMilliseconds(50));

        var response = await service.RequestAsync(request);
        Assert.Null(response);

        var submitted = service.Submit(new UserPromptResponse(request.Id, confirmed: true));
        Assert.False(submitted);
    }
}
