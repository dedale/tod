using Moq;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

internal class StoreMocks : IDisposable
{
    private readonly List<Mock> _mocks = [];

    public static StoreMocks New()
    {
        return new StoreMocks();
    }

    public BuildStoreMocks WithReferenceStore(BranchName branch, JobName rootJob, out IReferenceStore referenceStore)
    {
        return WithReferenceStore(branch, [rootJob], out referenceStore);
    }

    public BuildStoreMocks WithReferenceStore(BranchName branch, JobName[] rootJobs, out IReferenceStore referenceStore)
    {
        var mockReferenceStore = new Mock<IReferenceStore>(MockBehavior.Strict);
        var rootStore = new Mock<IByJobNameStore>(MockBehavior.Strict);
        var testStore = new Mock<IByJobNameStore>(MockBehavior.Strict);
        mockReferenceStore.Setup(x => x.Branch).Returns(branch);
        mockReferenceStore.Setup(x => x.RootStore).Returns(rootStore.Object);
        mockReferenceStore.Setup(x => x.TestStore).Returns(testStore.Object);
        rootStore.Setup(x => x.JobNames).Returns(rootJobs);
        testStore.Setup(x => x.JobNames).Returns([]);
        _mocks.Add(mockReferenceStore);
        _mocks.Add(rootStore);
        _mocks.Add(testStore);
        referenceStore = mockReferenceStore.Object;
        return new BuildStoreMocks(BuildBranch.Create(branch), rootStore, testStore);
    }

    internal sealed class BuildStoreMocks(BuildBranch buildBranch, Mock<IByJobNameStore> rootStore, Mock<IByJobNameStore> testStore) : StoreMocks
    {
        public BuildStoreMocks WithRootJobs(JobName job)
        {
            rootStore.Setup(s => s.BuildBranch).Returns(buildBranch);
            rootStore.Setup(s => s.Load(job, It.IsAny<Func<JobName, BuildCollection<RootBuild>.InnerCollection.Serializable>>()))
                .Returns((JobName j, Func<JobName, BuildCollection<RootBuild>.InnerCollection.Serializable> f) => f(j));
            return this;
        }

        public BuildStoreMocks WithNewRootBuilds(JobName job)
        {
            rootStore.Setup(s => s.Save(job, It.IsAny<BuildCollection<RootBuild>.InnerCollection.Serializable>()));
            return WithRootJobs(job);
        }
        
        public BuildStoreMocks WithTestobs(params JobName[] jobs)
        {
            testStore.Setup(s => s.BuildBranch).Returns(buildBranch);
            foreach (var job in jobs)
            {
                testStore.Setup(s => s.Load(job, It.IsAny<Func<JobName, BuildCollection<TestBuild>.InnerCollection.Serializable>>()))
                    .Returns((JobName j, Func<JobName, BuildCollection<TestBuild>.InnerCollection.Serializable> f) => f(j));
                testStore.Setup(x => x.Add(job));
            }
            return this;
        }

        public BuildStoreMocks WithNewTestBuilds(JobName job)
        {
            testStore.Setup(s => s.Save(job, It.IsAny<BuildCollection<TestBuild>.InnerCollection.Serializable>()));
            return WithTestobs(job);
        }

        public BuildStoreMocks WithNewTestBuilds(JobName[] jobs)
        {
            foreach (var job in jobs)
            {
                testStore.Setup(s => s.Save(job, It.IsAny<BuildCollection<TestBuild>.InnerCollection.Serializable>()));
            }
            return WithTestobs(jobs);
        }
    }

    public BuildStoreMocks WithOnDemandStore(JobName rootJob, out IOnDemandStore onDemandStore)
    {
        return WithOnDemandStore([rootJob], out onDemandStore);
    }

    public BuildStoreMocks WithOnDemandStore(JobName[] rootJobs, out IOnDemandStore onDemandStore)
    {
        var mockOnDemandStore = new Mock<IOnDemandStore>(MockBehavior.Strict);
        var rootStore = new Mock<IByJobNameStore>(MockBehavior.Strict);
        var testStore = new Mock<IByJobNameStore>(MockBehavior.Strict);
        mockOnDemandStore.Setup(x => x.RootStore).Returns(rootStore.Object);
        mockOnDemandStore.Setup(x => x.TestStore).Returns(testStore.Object);
        rootStore.Setup(x => x.JobNames).Returns(rootJobs);
        testStore.Setup(x => x.JobNames).Returns([]);
        _mocks.Add(mockOnDemandStore);
        _mocks.Add(rootStore);
        _mocks.Add(testStore);
        onDemandStore = mockOnDemandStore.Object;
        return new BuildStoreMocks(BuildBranch.OnDemand, rootStore, testStore);
    }

    public void Dispose()
    {
        _mocks.ForEach(m => m.VerifyAll());
    }
}
