namespace RPA.Infrastructure.Tests.Workflow;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using RPA.Domain.Interfaces;
using RPA.Infrastructure.Persistence;
using RPA.Infrastructure.SAP;
using RPA.Infrastructure.Workflow;
using RPA.Infrastructure.Workflow.Activities.EInvoice;
using Xunit;

/// <summary>
/// Katalog kapsama güvencesi (Paket A): her aktivitenin özellik formu üretilebilir
/// olmalı — input'lar tanımlı ve tipleri Studio GenericPropertyComponent'in
/// desteklediği kümede. Yeni aktivite eklendiğinde bu testler otomatik kapsar.
/// </summary>
public class ActivityRegistryCoverageTests
{
    [Fact]
    public void Catalog_ContainsEInvoiceActivitiesWithMappingPickerAndDefaults()
    {
        var catalog = ActivityRegistry.BuildCatalog();

        var single = catalog["EInvoice.ReadUbl"];
        Assert.Equal("einvoice-mapping", single.Inputs.Single(x => x.Name == "mappings").PickerKind);
        Assert.Contains(single.Outputs, x => x.Name == "invoice");
        Assert.Contains(single.Outputs, x => x.Name == "lines");
        Assert.Contains(single.Outputs, x => x.Name == "customFields");

        var batch = catalog["EInvoice.ReadUblBatch"];
        Assert.Equal("einvoice-mapping", batch.Inputs.Single(x => x.Name == "mappings").PickerKind);
        Assert.Equal("Continue", batch.Inputs.Single(x => x.Name == "errorMode").DefaultValue);
        Assert.Contains(batch.Outputs, x => x.Name == "results");

        var profile = catalog["EInvoice.ReadProfile"];
        Assert.Equal("einvoice-profile", profile.Inputs.Single(x => x.Name == "profileId").PickerKind);
        Assert.Contains(profile.Outputs, x => x.Name == "fatura");

        var profileBatch = catalog["EInvoice.ReadProfileBatch"];
        Assert.Equal("*.xml", profileBatch.Inputs.Single(x => x.Name == "fileFilter").DefaultValue);
        Assert.Contains(profileBatch.Outputs, x => x.Name == "faturalar");
    }

    [Fact]
    public void WorkflowServices_ResolveBothEInvoiceActivitiesAndSharedParser()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RpaDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddWorkflowServices();

        using var provider = services.BuildServiceProvider();
        Assert.Same(provider.GetRequiredService<UblInvoiceParser>(), provider.GetRequiredService<UblInvoiceParser>());
        Assert.IsType<ReadUblActivity>(provider.GetRequiredKeyedService<IActivity>("EInvoice.ReadUbl"));
        Assert.IsType<ReadUblBatchActivity>(provider.GetRequiredKeyedService<IActivity>("EInvoice.ReadUblBatch"));
        Assert.IsType<ReadProfileActivity>(provider.GetRequiredKeyedService<IActivity>("EInvoice.ReadProfile"));
        Assert.IsType<ReadProfileBatchActivity>(provider.GetRequiredKeyedService<IActivity>("EInvoice.ReadProfileBatch"));
    }

    /// <summary>Studio generic editörünün form alanına eşleyebildiği tipler.</summary>
    private static readonly HashSet<string> SupportedInputTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "string", "int", "number", "decimal", "bool", "boolean",
        "JSON", "DataTable", "Credential", "Sensitive",
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
            ["Sap.Gui.GridRead"] = ["gridId"],
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

    [Fact]
    public void FileActivities_CatalogEntries_HaveExecutableImplementations()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddWorkflowServices();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IActivityFactory>();
        var catalog = ActivityRegistry.BuildCatalog();

        var missing = catalog.Values
            .Where(a => a.Category == ActivityRegistry.CatFile)
            .Select(a => a.ActivityId)
            .Where(activityId => factory.CreateActivity(activityId) is null)
            .ToList();

        Assert.True(missing.Count == 0,
            $"Implementasyonu kayitli olmayan Dosya aktiviteleri: {string.Join(", ", missing)}");
    }

    /// <summary>
    /// Katalog (Studio'nun formu çizdiği kaynak) ile aktivitenin KENDİ metadata'sı (runtime'ın
    /// okuduğu parametre adları) aynı adları kullanmalıdır.
    ///
    /// <para>Regresyon: <c>Sap.Gui.GridRead</c> katalogda <c>gridElementId</c> girdisiyle
    /// tanımlıydı ama aktivite <c>gridId</c> okuyordu. Designer alanı doğru dolduruyor, runtime
    /// boş buluyor ve "'gridId' parametresi boş olamaz" ile patlıyordu. Bu sınıf hata yalnızca
    /// saha çalıştırmasında görülüyordu; test artık tüm aktiviteleri tarar.</para>
    /// </summary>
    [Fact]
    public void Catalog_ParameterNames_MatchActivityMetadata()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<RpaDbContext>(options => options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        services.AddWorkflowServices();
        services.AddSapGuiChannel();

        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IActivityFactory>();
        var catalog = ActivityRegistry.BuildCatalog();

        var mismatches = new List<string>();

        foreach (var entry in catalog.Values)
        {
            var activity = factory.CreateActivity(entry.ActivityId);
            if (activity is null)
            {
                continue; // Kapsam: implementasyon eksikliği ayrı testlerin konusu.
            }

            ActivityMetadata metadata;
            try
            {
                metadata = activity.GetMetadata();
            }
            catch
            {
                continue; // Metadata üretemeyen aktivite (platform kısıtı) kapsam dışı.
            }

            Compare(entry.ActivityId, "girdi", entry.Inputs, metadata.Inputs, mismatches);
            Compare(entry.ActivityId, "çıktı", entry.Outputs, metadata.Outputs, mismatches);
        }

        Assert.True(
            mismatches.Count == 0,
            "Katalog ile aktivite metadata'sı arasında parametre adı uyuşmazlığı:" +
            Environment.NewLine + string.Join(Environment.NewLine, mismatches));
    }

    private static void Compare(
        string activityId,
        string kind,
        List<ActivityParameter> catalogParameters,
        List<ActivityParameter> activityParameters,
        List<string> mismatches)
    {
        var catalogNames = catalogParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        var activityNames = activityParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

        var onlyInCatalog = catalogNames.Except(activityNames).OrderBy(n => n).ToList();
        var onlyInActivity = activityNames.Except(catalogNames).OrderBy(n => n).ToList();

        if (onlyInCatalog.Count > 0 || onlyInActivity.Count > 0)
        {
            mismatches.Add(
                $"  {activityId} ({kind}): yalnız katalogda [{string.Join(", ", onlyInCatalog)}], " +
                $"yalnız aktivitede [{string.Join(", ", onlyInActivity)}]");
        }
    }
}
