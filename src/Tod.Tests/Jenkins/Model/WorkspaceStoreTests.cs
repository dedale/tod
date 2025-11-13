using Microsoft.VisualStudio.TestPlatform.ObjectModel;
using NUnit.Framework;
using System.Diagnostics.Metrics;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class WorkspaceStoreTests
{
    private static readonly BranchName _mainBranch = new("main");
    private static readonly BranchName _prodBranch = new("prod");
    private static readonly JobName _mainRootJob = new("MAIN-build");
    private static readonly JobName _prodRootJob = new("PROD-build");
    private static readonly JobName _onDemandRootJob = new("CUSTOM-build");

    [Test]
    public void Constructor_WithFile_Throws()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "store.json");
        File.WriteAllText(path, "{}");
        Assert.That(() => new WorkspaceStore(path),
            Throws.ArgumentException.And.Message.EqualTo($"The path '{path}' is a file, but a directory is expected."));
    }

    [Test]
    public void GetReferenceStore_ManyTimes_ReturnsSameInstance()
    {
        using var temp = new TempDirectory();
        var store = new WorkspaceStore(temp.Path);
        var referenceStore1 = store.GetReferenceStore(_mainBranch);
        var referenceStore2 = store.GetReferenceStore(_mainBranch);
        Assert.That(referenceStore1, Is.SameAs(referenceStore2));
        var referenceStore3 = store.GetReferenceStore(_prodBranch);
        Assert.That(referenceStore1, Is.Not.SameAs(referenceStore3));
    }

    private sealed class SampleStore(IReferenceStore mainReferenceStore, IReferenceStore prodReferenceStore, IOnDemandStore onDemandStore)
    {
        public IReferenceStore MainReferenceStore { get; } = mainReferenceStore;
        public IReferenceStore ProdReferenceStore { get; } = prodReferenceStore;
        public IOnDemandStore OnDemandStore { get; } = onDemandStore;
    }

    private sealed class Sample : IDisposable
    {
        private readonly TempDirectory _temp = new();

        internal static List<JobName> GetTestJobs(int domainCount, string prefix)
        {
            var testJobs = new List<JobName>();
            foreach (var tests in new[] { "dev-tests", "integration-tests" })
            {
                foreach (var framework in new[] { "net6", "net8" })
                {
                    for (var i = 0; i < domainCount; i++)
                    {
                        var testJob = new JobName($"{prefix}-DOMAIN{i}-{tests}-{framework}");
                        testJobs.Add(testJob);
                    }
                }
            }
            return testJobs;
        }

        public Sample(SampleStore sampleStore, int domainCount, int rootBuilds, List<JobName> mainTestJobs, List<JobName> prodTestJobs)
        {
            MainBranchReference = new BranchReference(sampleStore.MainReferenceStore);
            ProdBranchReference = new BranchReference(sampleStore.ProdReferenceStore);
            var branchReferences = new List<BranchReference> { MainBranchReference, ProdBranchReference };
            var onDemandBuilds = new OnDemandBuilds(sampleStore.OnDemandStore);
            var onDemandRequests = new OnDemandRequests(_temp.Path);
            var workspace = new Workspace(branchReferences, onDemandBuilds, onDemandRequests);

            foreach (var branchReference in branchReferences)
            {
                var rootJob = branchReference.BranchName == _mainBranch ? _mainRootJob : _prodRootJob;
                var testJobs = branchReference.BranchName == _mainBranch ? mainTestJobs : prodTestJobs;

                var testFirstNumbers = testJobs.ToDictionary(job => job, _ => RandomData.NextBuildNumber);

                var rootFirstNumber = RandomData.NextBuildNumber;
                Enumerable.Range(0, rootBuilds).ToList().ForEach(i =>
                {
                    var rootBuild = new RootBuild(
                        rootJob,
                        Guid.NewGuid().ToString(),
                        rootFirstNumber + i,
                        DateTime.UtcNow.AddHours(-1),
                        DateTime.UtcNow,
                        true,
                        [RandomData.NextSha1()],
                        [.. testJobs]
                    );
                    branchReference.TryAdd(rootBuild);

                    foreach (var job in testJobs)
                    {
                        var testBuild = new TestBuild(
                            job,
                            Guid.NewGuid().ToString(),
                            testFirstNumbers[job] + i,
                            DateTime.UtcNow.AddHours(-1),
                            DateTime.UtcNow,
                            true,
                            rootBuild.Reference,
                            []
                        );
                        branchReference.TryAdd(testBuild);
                    }
                });
            }
        }

        public BranchReference MainBranchReference { get; }
        public BranchReference ProdBranchReference { get; }

        public void Dispose()
        {
            _temp.Dispose();
        }
    }

    private sealed record StoreMetrics(int LoadCount, int TotalLoaded, int SaveCount, int TotalSaved);

    private static T Listen<T>(Func<T> action, out StoreMetrics metrics)
    {
        int loadCount = 0;
        int totalLoaded = 0;
        int saveCount = 0;
        int totalSaved = 0;
        var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            switch (instrument.Name)
            {
                case "builds_loaded":
                case "builds_saved":
                    listener.EnableMeasurementEvents(instrument);
                    break;
            }
        };
        listener.SetMeasurementEventCallback<int>((instrument, measurement, tags, state) =>
        {
            switch (instrument.Name)
            {
                case "builds_loaded":
                    loadCount++;
                    totalLoaded += measurement;
                    break;
                case "builds_saved":
                    saveCount++;
                    totalSaved += measurement;
                    break;
            }
        });
        listener.Start();

        var result = action();

        listener.RecordObservableInstruments();

        metrics = new StoreMetrics(loadCount, totalLoaded, saveCount, totalSaved);

        return result;
    }

    [Test]
    public void Telemetry_SavingManyBuilds()
    {
        var domainCount = 20;
        var rootBuilds = 50;

        var mainTestJobs = Sample.GetTestJobs(domainCount, "MAIN");
        var prodTestJobs = Sample.GetTestJobs(domainCount, "PROD");

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _mainRootJob, out var mainReferenceStore)
            .WithNewRootBuilds(_mainRootJob)
            .WithNewTestBuilds([.. mainTestJobs])
            .WithReferenceStore(_prodBranch, _prodRootJob, out var prodReferenceStore)
            .WithNewRootBuilds(_prodRootJob)
            .WithNewTestBuilds([.. prodTestJobs])
            .WithOnDemandStore(_onDemandRootJob, out var onDemandStore);

        var sampleStore = new SampleStore(mainReferenceStore, prodReferenceStore, onDemandStore);

        using var sample = Listen(() => new Sample(sampleStore, domainCount, rootBuilds, mainTestJobs, prodTestJobs), out var metrics);

        using (Assert.EnterMultipleScope())
        {
            var testJobCount = mainTestJobs.Count;
            var mainBranchReference = sample.MainBranchReference;
            var prodBranchReference = sample.ProdBranchReference;

            Assert.That(mainBranchReference.RootBuilds.Single(), Has.Count.EqualTo(rootBuilds));
            Assert.That(mainBranchReference.TestBuilds, Has.Count.EqualTo(testJobCount));
            foreach (var builds in mainBranchReference.TestBuilds)
            {
                Assert.That(builds, Has.Count.EqualTo(rootBuilds));
            }
            Assert.That(prodBranchReference.RootBuilds.Single(), Has.Count.EqualTo(rootBuilds));
            Assert.That(prodBranchReference.TestBuilds, Has.Count.EqualTo(testJobCount));
            foreach (var builds in prodBranchReference.TestBuilds)
            {
                Assert.That(builds, Has.Count.EqualTo(rootBuilds));
            }

            var jobCount = 2 * (1 + testJobCount);
            Assert.That(metrics.LoadCount, Is.EqualTo(jobCount));
            Assert.That(metrics.TotalLoaded, Is.EqualTo(0));
            Assert.That(metrics.SaveCount, Is.EqualTo(jobCount * rootBuilds));
            Assert.That(metrics.TotalSaved, Is.EqualTo(103275 * 2));
        }
    }

    [Test]
    public void Telemetry_LoadingFewBuilds()
    {
        var domainCount = 20;
        var rootBuilds = 50;

        var mainTestJobs = Sample.GetTestJobs(domainCount, "MAIN");
        var prodTestJobs = Sample.GetTestJobs(domainCount, "PROD");

        var mainReferenceStore = new InMemoryReferenceStore(_mainBranch);
        var prodReferenceStore = new InMemoryReferenceStore(_prodBranch);
        var onDemandStore = new InMemoryOnDemandStore();
        var sampleStore = new SampleStore(mainReferenceStore, prodReferenceStore, onDemandStore);

        using var sample = new Sample(sampleStore, domainCount, rootBuilds, mainTestJobs, prodTestJobs);

        Listen(() =>
        {
            using var temp = new TempDirectory();

            var mainBranchReference = new BranchReference(mainReferenceStore);
            var prodBranchReference = new BranchReference(prodReferenceStore);
            var branchReferences = new List<BranchReference> { mainBranchReference, prodBranchReference };
            var onDemandBuilds = new OnDemandBuilds(onDemandStore);
            var onDemandRequests = new OnDemandRequests(temp.Path);
            var workspace = new Workspace(branchReferences, onDemandBuilds, onDemandRequests);

            var refRootBuilds = mainBranchReference.RootBuilds.GetOrAdd(_mainRootJob);
            var refRootBuild = refRootBuilds.Skip(10).First();

            var domain = 17;
            var testJobs = new[]
            {
                new JobName($"MAIN-DOMAIN{domain}-dev-tests-net6"),
                new JobName($"MAIN-DOMAIN{domain}-dev-tests-net8"),
                new JobName($"MAIN-DOMAIN{domain}-integration-tests-net6"),
                new JobName($"MAIN-DOMAIN{domain}-integration-tests-net8"),
            };

            foreach (var testJob in testJobs)
            {
                mainBranchReference.TryFindTestBuild(testJob, refRootBuild.Reference, out var refTestBuild);
            }

            return 0;

        }, out var metrics);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(metrics.LoadCount, Is.EqualTo(5));
            Assert.That(metrics.TotalLoaded, Is.EqualTo(250));
            Assert.That(metrics.SaveCount, Is.EqualTo(0));
            Assert.That(metrics.TotalSaved, Is.EqualTo(0));
        }

    }
}
