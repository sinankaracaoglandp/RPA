namespace RPA.Agent.Tests.UISpy;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RPA.Agent;
using RPA.Agent.UISpy;
using RPA.Infrastructure.UISpy;

/// <summary>
/// SAP "hedef göster" (🎯) kablolaması. Regresyon kaynağı: çözücü olarak
/// <see cref="NullSapGuiElementResolver"/> kayıtlıydı — her noktada <c>null</c> döndüğü için picker
/// SAP alanlarında hiçbir zaman element üretmiyor, kullanıcı <c>elementId</c>'yi elle yazmak zorunda
/// kalıyordu. Stub'a sessizce geri dönüş bu testle kalıcı olarak engellenir.
/// </summary>
public class SapPickerWiringTests
{
    private static ServiceProvider BuildAttendedAgentServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAgentCore(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Agent:OrchestratorUrl"] = "http://localhost:5000",
                // UI Spy yalnızca Attended modda kaydedilir.
                ["Agent:Mode"] = "Attended",
            })
            .Build());
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddAgentCore_Attended_RegistersRealSapResolver_NotStub()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // UI Spy yalnızca Windows'ta kaydedilir.
        }

        using var provider = BuildAttendedAgentServices();

        var resolver = provider.GetService<ISapGuiElementResolver>();

        Assert.NotNull(resolver);
        Assert.IsNotType<NullSapGuiElementResolver>(resolver);
        Assert.IsType<ComSapGuiElementResolver>(resolver);
    }

    [Fact]
    public void AddAgentCore_Attended_ResolvesSapPickerWithWindowManager()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var provider = BuildAttendedAgentServices();

        // Picker'ın tek-ekran davranışı (öndeki pencereyi küçült/geri getir) masaüstü picker'ıyla
        // aynı deneyimi verir; bağımlılığı çözülemezse 🎯 SAP'ta hiç başlamaz.
        Assert.NotNull(provider.GetService<IPickerWindowManager>());
        Assert.IsType<SapGuiSinglePicker>(provider.GetService<ISapGuiSinglePicker>());
    }
}
