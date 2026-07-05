namespace RPA.Agent.Tests.Hub;

using Microsoft.Extensions.Logging.Abstractions;
using RPA.Agent.Hub;
using RPA.Agent.JobList;

public class JobEventRouterTests
{
    private static JobEventRouter Create(JobListViewModel vm) => new(vm, NullLogger<JobEventRouter>.Instance);

    [Fact]
    public void Started_Olayi_Job_Listesine_Yeni_Satir_Ekler()
    {
        var vm = new JobListViewModel();
        var router = Create(vm);
        var jobId = Guid.NewGuid();

        router.Handle(new JobEventDto { JobId = jobId, WorkflowName = "Fatura İşleme", EventType = "Started" });

        var item = Assert.Single(vm.GetSnapshot());
        Assert.Equal(jobId, item.JobId);
        Assert.Equal("Fatura İşleme", item.WorkflowName);
    }

    [Fact]
    public void StepChanged_Olayi_Geçerli_Adimi_Gunceller()
    {
        var vm = new JobListViewModel();
        var router = Create(vm);
        var jobId = Guid.NewGuid();
        router.Handle(new JobEventDto { JobId = jobId, WorkflowName = "X", EventType = "Started" });

        router.Handle(new JobEventDto { JobId = jobId, EventType = "StepChanged", CurrentStep = "SAP'ye bağlanılıyor" });

        Assert.Equal("SAP'ye bağlanılıyor", vm.GetSnapshot().Single().CurrentStep);
    }

    [Fact]
    public void Completed_Ve_Failed_Durumu_Dogru_Isaretler()
    {
        var vm = new JobListViewModel();
        var router = Create(vm);
        var jobId = Guid.NewGuid();
        router.Handle(new JobEventDto { JobId = jobId, WorkflowName = "X", EventType = "Started" });

        router.Handle(new JobEventDto { JobId = jobId, EventType = "Failed" });

        Assert.Equal(JobListStatus.Failed, vm.GetSnapshot().Single().Status);
    }

    [Fact]
    public void Bilinmeyen_Olay_Turu_Istisna_Firlatmaz()
    {
        var vm = new JobListViewModel();
        var router = Create(vm);

        var ex = Record.Exception(() => router.Handle(new JobEventDto { JobId = Guid.NewGuid(), EventType = "Unknown" }));

        Assert.Null(ex);
    }
}
