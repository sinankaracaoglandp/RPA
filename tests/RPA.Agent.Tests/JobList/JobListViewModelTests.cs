namespace RPA.Agent.Tests.JobList;

using RPA.Agent.JobList;

public class JobListViewModelTests
{
    [Fact]
    public void AddOrUpdate_Snapshotta_Gorunur_Ve_Changed_Tetiklenir()
    {
        var vm = new JobListViewModel();
        var raised = 0;
        vm.Changed += () => raised++;
        var jobId = Guid.NewGuid();

        vm.AddOrUpdate(new JobListItem(jobId, "Fatura İşleme", DateTime.UtcNow));

        var snapshot = vm.GetSnapshot();
        Assert.Single(snapshot);
        Assert.Equal(jobId, snapshot[0].JobId);
        Assert.Equal(1, raised);
    }

    [Fact]
    public void UpdateStep_Mevcut_Isin_Adimini_Gunceller()
    {
        var vm = new JobListViewModel();
        var jobId = Guid.NewGuid();
        vm.AddOrUpdate(new JobListItem(jobId, "Fatura İşleme", DateTime.UtcNow));

        vm.UpdateStep(jobId, "SAP'ye bağlanılıyor");

        Assert.Equal("SAP'ye bağlanılıyor", vm.GetSnapshot().Single().CurrentStep);
    }

    [Fact]
    public void Complete_Durumu_Basarili_Veya_Basarisiz_Olarak_Isaretler()
    {
        var vm = new JobListViewModel();
        var jobId = Guid.NewGuid();
        vm.AddOrUpdate(new JobListItem(jobId, "Fatura İşleme", DateTime.UtcNow));

        vm.Complete(jobId, success: false);

        Assert.Equal(JobListStatus.Failed, vm.GetSnapshot().Single().Status);
    }

    [Fact]
    public void Remove_Olmayan_Is_Icin_Changed_Tetiklemez()
    {
        var vm = new JobListViewModel();
        var raised = 0;
        vm.Changed += () => raised++;

        vm.Remove(Guid.NewGuid());

        Assert.Equal(0, raised);
    }

    [Fact]
    public void Remove_Var_Olan_Isi_Listeden_Cikarir()
    {
        var vm = new JobListViewModel();
        var jobId = Guid.NewGuid();
        vm.AddOrUpdate(new JobListItem(jobId, "Fatura İşleme", DateTime.UtcNow));

        vm.Remove(jobId);

        Assert.Empty(vm.GetSnapshot());
    }
}
