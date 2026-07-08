namespace RPA.Infrastructure.Tests.Workflow;

using Microsoft.Extensions.DependencyInjection;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Workflow;
using Xunit;

/// <summary>
/// Katalog kapsama güvencesi (Paket A): her aktivitenin özellik formu üretilebilir
/// olmalı — input'lar tanımlı ve tipleri Studio GenericPropertyComponent'in
/// desteklediği kümede. Yeni aktivite eklendiğinde bu testler otomatik kapsar.
/// </summary>
public class ActivityRegistryCoverageTests
{
    /// <summary>Studio generic editörünün form alanına eşleyebildiği tipler.</summary>
    private static readonly HashSet<string> SupportedInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "int", "number", "decimal", "bool", "boolean",
        "JSON", "DataTable", "Credential",
    };

    /// <summary>Bilinçli olarak input'suz aktiviteler (parametre gerektirmez).</summary>
    private static readonly HashSet<string> KnownInputlessActivities = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sap.Nco.Rollback",   // BAPI_TRANSACTION_ROLLBACK — parametresiz
        "Sap.Gui.Screenshot", // yalnız çıktı üretir
    };

    [Fact]
    public void EveryActivity_HasInputs_OrIsKnownInputless()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var missing = catalog.Values
            .Where(a => a.Inputs.Count == 0 && !KnownInputlessActivities.Contains(a.ActivityId))
            .Select(a => a.ActivityId)
            .ToList();

        Assert.True(missing.Count == 0,
            $"Input tanımı olmayan aktiviteler (bilinçliyse KnownInputlessActivities'e ekleyin): {string.Join(", ", missing)}");
    }

    [Fact]
    public void EveryInput_UsesASupportedType()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var offenders = catalog.Values
            .SelectMany(a => a.Inputs.Select(i => (a.ActivityId, i.Name, i.Type)))
            .Where(x => !SupportedInputTypes.Contains(x.Type))
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Desteklenmeyen input tipi: {string.Join(", ", offenders.Select(o => $"{o.ActivityId}.{o.Name}:{o.Type}"))}");
    }

    [Fact]
    public void EveryActivity_HasDisplayNameAndCategory()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var offenders = catalog.Values
            .Where(a => string.IsNullOrWhiteSpace(a.DisplayName)
                     || a.DisplayName == a.ActivityId
                     || string.IsNullOrWhiteSpace(a.Category))
            .Select(a => a.ActivityId)
            .ToList();

        Assert.True(offenders.Count == 0,
            $"DisplayName/Category eksik: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void SapGuiSelectorInputs_HaveSapPickerKind()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var expected = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sap.Gui.Click"] = ["elementId"],
            ["Sap.Gui.SetText"] = ["elementId"],
            ["Sap.Gui.GetText"] = ["elementId"],
            ["Sap.Gui.SelectTab"] = ["elementId"],
            ["Sap.Gui.GridRead"] = ["gridElementId"],
        };

        var missing = expected
            .SelectMany(activity => activity.Value.Select(inputName => (activityId: activity.Key, inputName)))
            .Where(x =>
            {
                var input = catalog[x.activityId].Inputs.First(i => i.Name == x.inputName);
                return !string.Equals(input.PickerKind, "sap", StringComparison.OrdinalIgnoreCase);
            })
            .Select(x => $"{x.activityId}.{x.inputName}")
            .ToList();

        Assert.True(missing.Count == 0,
            $"SAP picker metadata eksik: {string.Join(", ", missing)}");
    }

    [Fact]
    public void CredentialInputs_DoNotHavePickerKind()
    {
        var catalog = ActivityRegistry.BuildCatalog();
        var offenders = catalog.Values
            .SelectMany(a => a.Inputs.Select(i => (a.ActivityId, Input: i)))
            .Where(x => string.Equals(x.Input.Type, "Credential", StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrWhiteSpace(x.Input.PickerKind))
            .Select(x => $"{x.ActivityId}.{x.Input.Name}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"Credential alanlarina picker baglanamaz: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void WebActivities_CatalogEntries_HaveExecutableImplementations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowServices();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IActivityFactory>();
        var catalog = ActivityRegistry.BuildCatalog();

        var missing = catalog.Values
            .Where(a => a.Category == ActivityRegistry.CatWeb)
            .Select(a => a.ActivityId)
            .Where(activityId => factory.CreateActivity(activityId) is null)
            .ToList();

        Assert.True(missing.Count == 0,
            $"Implementasyonu kayitli olmayan Web aktiviteleri: {string.Join(", ", missing)}");
    }
}
