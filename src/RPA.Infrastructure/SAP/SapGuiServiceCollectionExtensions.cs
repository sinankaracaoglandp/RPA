namespace RPA.Infrastructure.SAP;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.SAP.SapGuiElements;

/// <summary>
/// SAP GUI Scripting (fallback) kanal servislerinin DI kaydı (Task 4.1). Spec Bölüm 6.
/// </summary>
public static class SapGuiServiceCollectionExtensions
{
    public static IServiceCollection AddSapGuiChannel(this IServiceCollection services)
    {
        // Oturum üreticisi: Windows'ta gerçek SAP GUI Scripting COM; diğer ortamlarda yer tutucu.
        // (Gerçek sürüş yalnız SAP Logon kurulu + scripting etkin Windows makinede çalışır.)
        if (OperatingSystem.IsWindows())
        {
            services.AddSingleton<ISapGuiSessionFactory, ComSapGuiSessionFactory>();
        }
        else
        {
            services.AddSingleton<ISapGuiSessionFactory, StubSapGuiSessionFactory>();
        }

        // Oturum yöneticisi süreç genelinde tekil; kanal her JobRun için ayrı (scoped).
        services.AddSingleton<ISapGuiSessionManager, SapGuiSessionManager>();
        services.AddScoped<ISapGuiChannel, SapGuiChannel>();

        // Aktiviteler — runner keyed IActivity üzerinden çözer (ActivityId ile).
        services.AddKeyedTransient<IActivity, SapGuiConnectActivity>("Sap.Gui.Connect");
        services.AddKeyedTransient<IActivity, SapGuiExecuteTransactionActivity>("Sap.Gui.ExecuteTransaction");
        services.AddKeyedTransient<IActivity, SapGuiClickActivity>("Sap.Gui.Click");
        services.AddKeyedTransient<IActivity, SapGuiSetTextActivity>("Sap.Gui.SetText");
        services.AddKeyedTransient<IActivity, SapGuiGetTextActivity>("Sap.Gui.GetText");
        services.AddKeyedTransient<IActivity, SapGuiSelectTabActivity>("Sap.Gui.SelectTab");
        services.AddKeyedTransient<IActivity, SapGuiSendVKeyActivity>("Sap.Gui.SendVKey");
        services.AddKeyedTransient<IActivity, SapGuiSelectMenuActivity>("Sap.Gui.SelectMenu");
        services.AddKeyedTransient<IActivity, SapGuiGridReadActivity>("Sap.Gui.GridRead");
        services.AddKeyedTransient<IActivity, SapGuiScreenshotActivity>("Sap.Gui.Screenshot");

        return services;
    }
}
