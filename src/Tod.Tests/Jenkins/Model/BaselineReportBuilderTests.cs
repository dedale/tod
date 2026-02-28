using NUnit.Framework;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class BaselineReportBuilderTests
{
    private readonly JobName _rootJob = new("MyRootJob");
    private readonly JobName _testJob1 = new("MyTestJob1");
    private readonly JobName _testJob2 = new("MyTestJob2");
    private readonly BranchName _mainBranch = new("main");

    private TempDirectory _temp;
    private BaselineBranch _baselineBranch;
    private IFlakyTests _flakyTests;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        var store = new WorkspaceStore(_temp.Path);
        var refStore = store.GetBaselineStore(_mainBranch);
        _baselineBranch = new BaselineBranch(refStore);
        var flakyStore = InMemoryFlakyStore.Default;
        _flakyTests = new FlakyTests(flakyStore);
    }

    [TearDown]
    public void TearDown()
    {
        _temp.Dispose();
    }

    [Test]
    public void Build_WithNoRootBuilds_ReturnsNull()
    {
        var rootBuilds = Array.Empty<BaselineChain>();

        var report = BaselineReportBuilder.Build(rootBuilds, "TestChain", _baselineBranch, _flakyTests);

        Assert.That(report, Is.Null);
    }

    [Test]
    public void Build_WithSingleRootBuild_CreatesReport()
    {
        var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 2, testJobNames: [_testJob1.Value]);
        var testBuildRef = new BuildReference(_testJob1, RandomData.NextBuildNumber);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuildRef.BuildNumber)
        };
        var tracking = new BaselineChain(rootBuild.Reference, rootBuild.IsSuccessful, testBuilds, false);

        _baselineBranch.TryAdd(rootBuild);
        var testBuild = new TestBuild(_testJob1, "id", testBuildRef.BuildNumber, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow, true, [rootBuild.Reference], []);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob1).TryAdd(testBuild);

        var report = BaselineReportBuilder.Build([tracking], "TestChain", _baselineBranch, _flakyTests);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.ChainName, Is.EqualTo("TestChain"));
        Assert.That(report.RootBuilds, Has.Length.EqualTo(1));
        Assert.That(report.RootBuilds[0].BuildNumber, Is.EqualTo(rootBuild.BuildNumber));
    }

    [Test]
    public void Build_WithMultipleRootBuilds_IncludesAll()
    {
        var rootBuild1 = RandomData.NextRootBuild(jobName: _rootJob.Value, buildNumber: 100, isSuccessful: false, commits: 1, testJobNames: [_testJob1.Value]);
        var rootBuild2 = RandomData.NextRootBuild(jobName: _rootJob.Value, buildNumber: 101, isSuccessful: true, commits: 1, testJobNames: [_testJob1.Value]);

        var testBuildRef1 = new BuildReference(_testJob1, 200);
        var testBuildRef2 = new BuildReference(_testJob1, 201);

        var testBuilds1 = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuildRef1.BuildNumber)
        };
        var testBuilds2 = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuildRef2.BuildNumber)
        };

        var tracking1 = new BaselineChain(rootBuild1.Reference, rootBuild1.IsSuccessful, testBuilds1, false);
        var tracking2 = new BaselineChain(rootBuild2.Reference, rootBuild2.IsSuccessful, testBuilds2, false);

        _baselineBranch.TryAdd(rootBuild1);
        _baselineBranch.TryAdd(rootBuild2);
        var testBuild1 = new TestBuild(_testJob1, "id1", testBuildRef1.BuildNumber, DateTime.UtcNow.AddMinutes(-20), DateTime.UtcNow.AddMinutes(-10), true, [rootBuild1.Reference], []);
        var testBuild2 = new TestBuild(_testJob1, "id2", testBuildRef2.BuildNumber, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow, true, [rootBuild2.Reference], []);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob1).TryAdd(testBuild1);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob1).TryAdd(testBuild2);

        var report = BaselineReportBuilder.Build([tracking1, tracking2], "TestChain", _baselineBranch, _flakyTests);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.RootBuilds, Has.Length.EqualTo(2));
        Assert.That(report.RootBuilds[0].BuildNumber, Is.EqualTo(100));
        Assert.That(report.RootBuilds[1].BuildNumber, Is.EqualTo(101));
    }

    [Test]
    public void Build_WithQueuedTestBuild_SkipsTest()
    {
        var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 1, testJobNames: [_testJob1.Value]);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1)
        };
        var tracking = new BaselineChain(rootBuild.Reference, rootBuild.IsSuccessful, testBuilds, false);

        _baselineBranch.TryAdd(rootBuild);
        var report = BaselineReportBuilder.Build([tracking], "TestChain", _baselineBranch, _flakyTests);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.TestDiffs, Is.Empty);
    }

    [Test]
    public void Build_WithFailedTests_IncludesInDiff()
    {
        var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 1, testJobNames: [_testJob1.Value]);
        var testBuildRef = new BuildReference(_testJob1, RandomData.NextBuildNumber);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuildRef.BuildNumber)
        };
        var tracking = new BaselineChain(rootBuild.Reference, rootBuild.IsSuccessful, testBuilds, false);

        var failedTests = new[]
        {
            new FailedTest("TestClass", "TestMethod", "Error details")
        };
        _baselineBranch.TryAdd(rootBuild);
        var testBuild = new TestBuild(_testJob1, "id", testBuildRef.BuildNumber, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow, false, [rootBuild.Reference], failedTests);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob1).TryAdd(testBuild);

        var report = BaselineReportBuilder.Build([tracking], "TestChain", _baselineBranch, _flakyTests);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.TestDiffs, Has.Count.EqualTo(1));
        var testResults = report.TestDiffs[_testJob1].Diff.Match(onNotComparable: _ => [], onComparable: d => d.FailedTests);
        Assert.That(testResults, Has.Length.EqualTo(1));
        Assert.That(testResults[0].Test.ClassName, Is.EqualTo("TestClass"));
    }

    [Test]
    public void Build_WithNoBaseline_TreatsAllFailuresAsNew()
    {
        var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 1, testJobNames: [_testJob1.Value]);
        var testBuildRef = new BuildReference(_testJob1, RandomData.NextBuildNumber);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuildRef.BuildNumber)
        };
        var tracking = new BaselineChain(rootBuild.Reference, rootBuild.IsSuccessful, testBuilds, false);

        var failedTests = new[]
        {
            new FailedTest("TestClass", "TestMethod1", "Error 1"),
            new FailedTest("TestClass", "TestMethod2", "Error 2")
        };
        _baselineBranch.TryAdd(rootBuild);
        var testBuild = new TestBuild(_testJob1, "id", testBuildRef.BuildNumber, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow, false, [rootBuild.Reference], failedTests);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob1).TryAdd(testBuild);

        var report = BaselineReportBuilder.Build([tracking], "TestChain", _baselineBranch, _flakyTests);

        Assert.That(report, Is.Not.Null);
        var testResults = report!.TestDiffs[_testJob1].Diff.Match(onNotComparable: _ => [], onComparable: d => d.FailedTests);
        Assert.That(testResults, Has.Length.EqualTo(2));
        Assert.That(testResults.All(t => t.Newness == Newness.New), Is.True);
    }

    [Test]
    public void Build_WithBaseline_ComparesCorrectly()
    {
        var baselineRootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, buildNumber: 90, commits: 1, testJobNames: [_testJob1.Value]);
        var baselineTestBuild = new TestBuild(
            _testJob1,
            "baseline",
            180,
            DateTime.UtcNow.AddHours(-2),
            DateTime.UtcNow.AddHours(-1),
            true,
            [baselineRootBuild.Reference],
            []
        );
        _baselineBranch.TryAdd(baselineRootBuild);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob1).TryAdd(baselineTestBuild);

        var currentRootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, buildNumber: 100, commits: 1, testJobNames: [_testJob1.Value]);
        var currentTestBuildRef = new BuildReference(_testJob1, 200);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(currentTestBuildRef.BuildNumber)
        };
        var tracking = new BaselineChain(currentRootBuild.Reference, currentRootBuild.IsSuccessful, testBuilds, false);

        var currentFailedTests = new[]
        {
            new FailedTest("TestClass", "NewTest", "New error")
        };
        var currentTestBuild = new TestBuild(
            _testJob1,
            "current",
            currentTestBuildRef.BuildNumber,
            DateTime.UtcNow.AddMinutes(-10),
            DateTime.UtcNow,
            false,
            [currentRootBuild.Reference],
            currentFailedTests
        );
        _baselineBranch.TryAdd(currentRootBuild);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob1).TryAdd(currentTestBuild);

        var report = BaselineReportBuilder.Build([tracking], "TestChain", _baselineBranch, _flakyTests);

        Assert.That(report, Is.Not.Null);
        var diff = report!.TestDiffs[_testJob1].Diff.Match(onNotComparable: _ => null, onComparable: d => (FailedTestDiff?)d);
        Assert.That(diff!.FailedTests.Count(t => t.Newness == Newness.New), Is.EqualTo(1));
    }

    [Test]
    public void Build_WithMultipleTestJobs_IncludesAllDiffs()
    {
        var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 1, testJobNames: [_testJob1.Value, _testJob2.Value]);
        var testBuildRef1 = new BuildReference(_testJob1, RandomData.NextBuildNumber);
        var testBuildRef2 = new BuildReference(_testJob2, RandomData.NextBuildNumber);
        var testBuilds = new Dictionary<JobName, BaseTestBuildReference>
        {
            [_testJob1] = BaseTestBuildReference.Create(_testJob1).DoneBaseline(testBuildRef1.BuildNumber),
            [_testJob2] = BaseTestBuildReference.Create(_testJob2).DoneBaseline(testBuildRef2.BuildNumber)
        };
        var tracking = new BaselineChain(rootBuild.Reference, rootBuild.IsSuccessful, testBuilds, false);

        _baselineBranch.TryAdd(rootBuild);
        var testBuild1 = new TestBuild(_testJob1, "id1", testBuildRef1.BuildNumber, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow, true, [rootBuild.Reference], []);
        var testBuild2 = new TestBuild(_testJob2, "id2", testBuildRef2.BuildNumber, DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow, true, [rootBuild.Reference], []);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob1).TryAdd(testBuild1);
        _baselineBranch.TestBuilds.GetOrAdd(_testJob2).TryAdd(testBuild2);

        var report = BaselineReportBuilder.Build([tracking], "TestChain", _baselineBranch, _flakyTests);

        Assert.That(report, Is.Not.Null);
        Assert.That(report!.TestDiffs, Has.Count.EqualTo(2));
        Assert.That(report.TestDiffs.ContainsKey(_testJob1), Is.True);
        Assert.That(report.TestDiffs.ContainsKey(_testJob2), Is.True);
    }
}
