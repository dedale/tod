using Moq;
using NUnit.Framework;
using System.Diagnostics;
using Tod.Git;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BaselineBranchTests
{
    private readonly BranchName _mainBranch = new("main");
    private readonly JobName _rootJob = new("MyJob");
    private readonly JobName _testJob1 = new("MyTestJob1");
    private readonly JobName _testJob2 = new("MyTestJob2");

    private readonly BranchName _devBranch = new("dev");
    private readonly JobName _devJob = new("DevJob");

    private StoreMocks.BuildStoreMocks DevBranchMocks(out BaselineBranch devBaseBranch, out RootBuild devRootBuild)
    {
        var devTestJob = new JobName("DevTestJob");
        var devMocks = StoreMocks.New()
            .WithBaselineStore(_devBranch, _devJob, out var devBaselineStore)
            .WithNewRootBuilds(_devJob)
            .WithTestobs(devTestJob);
        devBaseBranch = new BaselineBranch(devBaselineStore);
        devBaseBranch.TryAddRoot(_devJob);
        devRootBuild = RandomData.NextRootBuild(jobName: _devJob.Value, commits: 3, testJobNames: [devTestJob.Value]);
        devBaseBranch.TryAdd(devRootBuild);
        return devMocks;
    }

    [Test]
    public void TryAdd_RootBuildTwice_OnlyFirstTime()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);

            baselineBranch.TryAddRoot(_rootJob);

            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value);
            Assert.That(baselineBranch.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(baselineBranch.RootBuilds[0], Has.Count.EqualTo(0));
            Assert.That(baselineBranch.TestBuilds, Has.Count.EqualTo(0));

            var added = baselineBranch.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            Assert.That(baselineBranch.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(baselineBranch.RootBuilds[0], Has.Count.EqualTo(1));
            Assert.That(baselineBranch.RootBuilds[0].JobName.Value, Is.EqualTo(_rootJob.Value));
            Assert.That(baselineBranch.RootBuilds[0].Contains(rootBuild.BuildNumber), Is.True);
            Assert.That(baselineBranch.TestBuilds, Has.Count.EqualTo(2));
            Assert.That(baselineBranch.TestBuilds[0].JobName.Value, Is.EqualTo(_testJob1.Value));
            Assert.That(baselineBranch.TestBuilds[0], Has.Count.EqualTo(0));
            Assert.That(baselineBranch.TestBuilds[1].JobName.Value, Is.EqualTo(_testJob2.Value));
            Assert.That(baselineBranch.TestBuilds[1], Has.Count.EqualTo(0));

            added = baselineBranch.TryAdd(rootBuild);
            Assert.That(added, Is.False);
        }
    }

    [Test]
    public void Serialization_Works() // with root builds only for now
    {
        using (Assert.EnterMultipleScope())
        {
            using var temp = new TempDirectory();
            var baselineStore = new BaselineStore(_mainBranch, temp.Path);
            var baselineBranch = new BaselineBranch(baselineStore);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value);
            var added = baselineBranch.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            var clone = new BaselineBranch(new BaselineStore(_mainBranch, temp.Path));
            Assert.That(clone!.BranchName, Is.EqualTo(baselineBranch.BranchName));
            Assert.That(clone.RootBuilds, Has.Count.EqualTo(baselineBranch.RootBuilds.Count));
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithoutBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithRootJobs(_rootJob);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);

            var found = baselineBranch.TryFindRootBuildByCommit(RandomData.NextSha1(), _rootJob, out var foundRootBuild);
            Assert.That(found, Is.False);
            Assert.That(foundRootBuild, Is.Null);
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithOneBuild_ReturnsBuild()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var commits = 3;
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: commits);
            var added = baselineBranch.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            for (var c = 0; c < commits; c++)
            {
                var found = baselineBranch.TryFindRootBuildByCommit(rootBuild.Commits[c].Sha1, _rootJob, out var foundRootBuild);
                Assert.That(found, Is.True);
                Debug.Assert(foundRootBuild is not null);
                Assert.That(foundRootBuild.Reference, Is.EqualTo(rootBuild.Reference));
            }
            Assert.That(baselineBranch.TryFindRootBuildByCommit(RandomData.NextSha1(), _rootJob, out _), Is.False);
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithMultipleBuilds_ReturnsBuild()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var builds = 5;
            var commitsPerBuild = 3;
            var rootBuilds = new List<RootBuild>();
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, buildNumber: b + 1, commits: commitsPerBuild);
                var added = baselineBranch.TryAdd(rootBuild);
                Assert.That(added, Is.True);
                rootBuilds.Add(rootBuild);
            }
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = rootBuilds[b];
                for (var c = 0; c < commitsPerBuild; c++)
                {
                    var found = baselineBranch.TryFindRootBuildByCommit(rootBuild.Commits[c].Sha1, _rootJob, out var foundRootBuild);
                    Assert.That(found, Is.True);
                    Debug.Assert(foundRootBuild is not null);
                    Assert.That(foundRootBuild.Reference, Is.EqualTo(rootBuild.Reference));
                }
            }
            Assert.That(baselineBranch.TryFindRootBuildByCommit(RandomData.NextSha1(), _rootJob, out _), Is.False);
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithMultipleFailedBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var builds = 5;
            var commitsPerBuild = 3;
            var rootBuilds = new List<RootBuild>();
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, buildNumber: b + 1, isSuccessful: false, commits: commitsPerBuild);
                var added = baselineBranch.TryAdd(rootBuild);
                Assert.That(added, Is.True);
                rootBuilds.Add(rootBuild);
            }
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = rootBuilds[b];
                for (var c = 0; c < commitsPerBuild; c++)
                {
                    Assert.That(baselineBranch.TryFindRootBuildByCommit(rootBuild.Commits[c].Sha1, _rootJob, out _), Is.False);
                }
            }
            Assert.That(baselineBranch.TryFindRootBuildByCommit(RandomData.NextSha1(), _rootJob, out _), Is.False);
        }
    }

    [Test]
    public void RemoveTest_WithTestJob_RemovesAllBuilds()
    {
        var testJobName = new JobName("MyTestJob");
        var otherTestJobName = new JobName("OtherTestJob");
        using var mocks = StoreMocks.New()
            .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
            .WithNewTestBuilds(testJobName)
            .WithNewTestBuilds(otherTestJobName)
            .WithRemoved(testJobName);
        var onDemandBuilds = new BaselineBranch(baselineStore);
        onDemandBuilds.TryAdd(RandomData.NextTestBuild(testJobName: testJobName.Value));
        onDemandBuilds.TryAdd(RandomData.NextTestBuild(testJobName: otherTestJobName.Value));
        Assert.That(onDemandBuilds.TestBuilds, Has.Count.EqualTo(2));
        Assert.That(onDemandBuilds.TestBuilds.Select(c => c.JobName), Is.EquivalentTo([testJobName, otherTestJobName]));
        onDemandBuilds.RemoveTest(testJobName);
        Assert.That(onDemandBuilds.TestBuilds.Select(c => c.JobName), Is.EquivalentTo([otherTestJobName]));
    }

    [Test]
    public void RemoveTest_WithStore_RemovesFromStore()
    {
        using var temp = new TempDirectory();
        var testJobName = new JobName("MyTestJob");
        var baselineStore = new BaselineStore(_mainBranch, temp.Path);
        var baselineBranch = new BaselineBranch(baselineStore);
        baselineBranch.TryAdd(RandomData.NextTestBuild(testJobName: testJobName.Value));
        Assert.That(baselineBranch.TestBuilds, Has.Count.EqualTo(1));
        baselineBranch.RemoveTest(testJobName);

        var newStore = new BaselineStore(_mainBranch, temp.Path);
        var newBaseline = new BaselineBranch(newStore);
        Assert.That(newBaseline.TestBuilds, Is.Empty);
    }

    [Test]
    public void TryAdd_TestBuildTwice_OnlyFirstTime()
    {
        var testJobName = new JobName("MyTestJob");

        using var mocks = StoreMocks.New()
            .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
            .WithNewTestBuilds(testJobName);

        var baselineBranch = new BaselineBranch(baselineStore);
        baselineBranch.TryAddRoot(_rootJob);

        var testBuild = RandomData.NextTestBuild(testJobName: testJobName.Value);
        var added = baselineBranch.TryAdd(testBuild);
        Assert.That(added, Is.True);

        added = baselineBranch.TryAdd(testBuild);
        Assert.That(added, Is.False);
    }

    [Test]
    public void TryFindTestBuild_WithoutBuilds_ReturnsNone()
    {
        using var mocks = StoreMocks.New()
            .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore);

        var baselineBranch = new BaselineBranch(baselineStore);
        baselineBranch.TryAddRoot(_rootJob);
        Assert.That(baselineBranch.TryFindTestBuild(new("MyTestJob"), new BuildReference(_rootJob, 42), out var foundTestBuild), Is.False);
        Assert.That(foundTestBuild, Is.Null);
    }

    [Test]
    public void TryFindTestBuild_WithOldRootBuild_ReturnsOnlyLatest()
    {
        using (Assert.EnterMultipleScope())
        {
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewTestBuilds(testJobName);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var rootBuild = new BuildReference(_rootJob, RandomData.NextBuildNumber);
            var testBuildNumber = RandomData.NextBuildNumber;
            // Add a test build for root build
            var testBuild = RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild);
            Assert.That(baselineBranch.TryAdd(testBuild), Is.True);
            // Try to find test build for an older root build - should return none
            rootBuild = rootBuild.Next();
            Assert.That(baselineBranch.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.False);
            Assert.That(foundTestBuild, Is.Null);
            // Add a test build for the newer root build
            testBuildNumber++;
            testBuild = RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild);
            Assert.That(baselineBranch.TryAdd(testBuild), Is.True);
            // Try to find test build for the newer root build - should return the newer one
            Assert.That(baselineBranch.TryFindTestBuild(testJobName, rootBuild, out foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(testBuild.Reference));
        }
    }

    [Test]
    public void TryFindTestBuild_WithNewerRootBuild_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewTestBuilds(testJobName);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);

            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(_rootJob, buildNumber);
            var testBuild = RandomData.NextTestBuild(rootBuild: rootBuild.Next());
            Assert.That(baselineBranch.TryAdd(testBuild), Is.True);
            Assert.That(baselineBranch.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(testBuild.Reference));
        }
    }

    [Test]
    public void TryFindTestBuild_WithLowerAndGreaterRootBuilds_ConsiderOnlyLatest()
    {
        using (Assert.EnterMultipleScope())
        {
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewTestBuilds(testJobName);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var rootBuild = new BuildReference(_rootJob, RandomData.NextBuildNumber);

            // Add an old test build for root build N-1
            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(rootBuild.JobName, rootBuild.BuildNumber - 1));
            Assert.That(baselineBranch.TryAdd(oldTestBuild), Is.True);
            // Add a new test build for root build N+1
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(baselineBranch.TryAdd(newTtestBuild), Is.True);

            // Search for test build for root build N - should find the newer one (N+1), not the older one (N-1)
            Assert.That(baselineBranch.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(newTtestBuild.Reference));
        }
    }

    [Test]
    public void TryFindTestBuild_WithManyRootBuildJobNames_IgnoreOther()
    {
        using (Assert.EnterMultipleScope())
        {
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewTestBuilds(testJobName);

            var oldRootJob = new JobName("OldJob");
            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(_rootJob, buildNumber);

            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(oldRootJob, RandomData.NextBuildNumber));
            Assert.That(baselineBranch.TryAdd(oldTestBuild), Is.True);
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(baselineBranch.TryAdd(newTtestBuild), Is.True);
            Assert.That(baselineBranch.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(newTtestBuild.Reference));
        }
    }

    [Test]
    public void TryFindRefCommit_EmptyCommitArray_ThrowsNotSupportedException()
    {
        using var mocks = StoreMocks.New()
            .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore);

        var baselineBranch = new BaselineBranch(baselineStore);
        baselineBranch.TryAddRoot(_rootJob);
        var baselineBranches = new[] { baselineBranch };
        var commits = Array.Empty<Sha1>();

        Assert.That(baselineBranches.TryFindRefCommit(commits, [_rootJob], _mainBranch, out _), Is.False);
    }

    [Test]
    public void TryFindRefCommit_FirstCommitInBranchHistory_ThrowsNotSupportedException()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);

            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            baselineBranch.TryAdd(rootBuild);

            var baselineBranches = new[] { baselineBranch };
            var commits = new[] { rootBuild.Commits[0].Sha1, RandomData.NextSha1() };

            Assert.That(
                () => baselineBranches.TryFindRefCommit(commits, [_rootJob], _mainBranch, out _),
                Throws.TypeOf<NotSupportedException>()
                    .With.Message.EqualTo("No local commits to test for job MyJob"));
        }
    }

    [Test]
    public void TryFindRefCommit_WithSpecificBranch_FindsRefCommit()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            baselineBranch.TryAdd(rootBuild);

            var baselineBranches = new[] { baselineBranch };
            var localCommit = RandomData.NextSha1();
            var refCommit = rootBuild.Commits[1].Sha1;
            var commits = new[] { localCommit, refCommit };

            var result = baselineBranches.TryFindRefCommit(commits, [_rootJob], _mainBranch, out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]

    public void TryFindRefCommit_WithManyRootJobs_FindsRefCommit()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName1 = new JobName("MyJob1");
            var jobName2 = new JobName("MyJob2");

            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, [jobName1, jobName2], out var baselineStore)
                .WithNewRootBuilds(jobName1)
                .WithNewRootBuilds(jobName2)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(jobName1);
            baselineBranch.TryAddRoot(jobName2);

            var rootBuild1 = RandomData.NextRootBuild(jobName: jobName1.Value, commits: 3);
            baselineBranch.TryAdd(rootBuild1);

            var rootBuild2 = RandomData.NextRootBuild(jobName: jobName2.Value, commits: 3);
            rootBuild2 = new RootBuild(
                rootBuild2.JobName,
                rootBuild2.Id,
                rootBuild2.BuildNumber,
                rootBuild2.StartTimeUtc,
                rootBuild2.EndTimeUtc,
                rootBuild2.IsSuccessful,
                rootBuild1.Commits,
                rootBuild2.Scheduled);
            baselineBranch.TryAdd(rootBuild2);

            var baselineBranches = new[] { baselineBranch };
            var localCommit = RandomData.NextSha1();
            var refCommit = rootBuild2.Commits[2].Sha1;
            var commits = new[] { localCommit, refCommit };
            var result = baselineBranches.TryFindRefCommit(commits, [jobName1, jobName2], _mainBranch, out var foundRefCommit);
            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]
    public void TryFindRefCommit_WithSpecificBranch_BranchNotFound_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            baselineBranch.TryAdd(rootBuild);

            var baselineBranches = new[] { baselineBranch };
            var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1() };

            var result = baselineBranches.TryFindRefCommit(commits, [_rootJob], new BranchName("nonexistent"), out var foundRefCommit);

            Assert.That(result, Is.False);
            Assert.That(foundRefCommit, Is.Null);
        }
    }

    [Test]
    public void TryFindRefCommit_WithSpecificBranch_RefCommitNotFound_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            baselineBranch.TryAdd(rootBuild);

            var baselineBranches = new[] { baselineBranch };
            var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1(), RandomData.NextSha1() };

            var result = baselineBranches.TryFindRefCommit(commits, [_rootJob], _mainBranch, out var foundRefCommit);

            Assert.That(result, Is.False);
            Assert.That(foundRefCommit, Is.Null);
        }
    }

    [Test]
    public void TryGuessBranch_FindsRefCommitInFirstMatchingBranch()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootFilters = new[] { "Job" };
            var mainJob = new JobName("MainJob");

            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, mainJob, out var baselineStore)
                .WithNewRootBuilds(mainJob)
                .WithTestobs(_testJob1, _testJob2);

            var mainBranchRef = new BaselineBranch(baselineStore);
            mainBranchRef.TryAddRoot(mainJob);
            var mainRootBuild = RandomData.NextRootBuild(jobName: mainJob.Value, commits: 3);
            mainBranchRef.TryAdd(mainRootBuild);

            using var devMocks = DevBranchMocks(out var devBranchRef, out var devRootBuild);

            var baselineBranches = new[] { mainBranchRef, devBranchRef };
            var localCommit = RandomData.NextSha1();
            var refCommit = mainRootBuild.Commits[1].Sha1;
            var commits = new[] { localCommit, refCommit };

            var onDemandJob = new JobName("CustomJob");
            var expectedRootDiffs = new[] { new JobDiff("chain", mainJob, onDemandJob) };
            var filterManager = new Mock<IFilterManager>(MockBehavior.Strict);
            filterManager.Setup(f => f.GetRootDiffs(rootFilters, mainBranchRef.BranchName)).Returns(expectedRootDiffs);

            var result = baselineBranches.TryGuessBranch(commits, rootFilters, filterManager.Object, out var rootDiffs, out var foundBranch, out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(rootDiffs, Is.EquivalentTo(expectedRootDiffs));
            Assert.That(foundBranch, Is.EqualTo(_mainBranch));
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]
    public void TryGuessBranch_NoMatchingBranch_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootFilters = new[] { "Job" };
            var mainJob = new JobName("MainJob");

            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, mainJob, out var baselineStore)
                .WithNewRootBuilds(mainJob)
                .WithTestobs(_testJob1, _testJob2);

            var mainBranchRef = new BaselineBranch(baselineStore);
            mainBranchRef.TryAddRoot(mainJob);
            var mainRootBuild = RandomData.NextRootBuild(jobName: mainJob.Value, commits: 3);
            mainBranchRef.TryAdd(mainRootBuild);

            var baselineBranches = new[] { mainBranchRef };
            var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1(), RandomData.NextSha1() };

            var onDemandJob = new JobName("CustomJob");
            var expectedRootDiffs = new[] { new JobDiff("chain", mainJob, onDemandJob) };
            var filterManager = new Mock<IFilterManager>(MockBehavior.Strict);
            filterManager.Setup(f => f.GetRootDiffs(rootFilters, mainBranchRef.BranchName)).Returns(expectedRootDiffs);

            var result = baselineBranches.TryGuessBranch(commits, rootFilters, filterManager.Object, out var rootDiffs, out var foundBranch, out var foundRefCommit);

            Assert.That(result, Is.False);
            Assert.That(foundBranch, Is.Null);
            Assert.That(foundRefCommit, Is.Null);
        }
    }

    [Test]
    public void TryFindRefCommit_MultipleCommitsInHistory_FindsFirstMatch()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 5);
            baselineBranch.TryAdd(rootBuild);

            var baselineBranches = new[] { baselineBranch };
            var localCommit1 = RandomData.NextSha1();
            var localCommit2 = RandomData.NextSha1();
            var refCommit1 = rootBuild.Commits[1].Sha1;
            var refCommit2 = rootBuild.Commits[2].Sha1;
            var commits = new[] { localCommit1, localCommit2, refCommit1, refCommit2 };

            var result = baselineBranches.TryFindRefCommit(commits, [_rootJob], _mainBranch, out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit1));
        }
    }

    [Test]
    public void TryFindRefCommit_MultipleBranches_FindsCorrectBranch()
    {
        using (Assert.EnterMultipleScope())
        {
            var mainJob = new JobName("MainJob");

            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, mainJob, out var baselineStore)
                .WithNewRootBuilds(mainJob)
                .WithTestobs(_testJob1, _testJob2);

            var mainBranchRef = new BaselineBranch(baselineStore);
            mainBranchRef.TryAddRoot(mainJob);
            var mainRootBuild = RandomData.NextRootBuild(jobName: mainJob.Value, commits: 3);
            mainBranchRef.TryAdd(mainRootBuild);

            using var devMocks = DevBranchMocks(out var devBranchRef, out var devRootBuild);

            var baselineBranches = new[] { mainBranchRef, devBranchRef };
            var localCommit = RandomData.NextSha1();
            var refCommit = devRootBuild.Commits[1].Sha1;
            var commits = new[] { localCommit, refCommit };

            // Should find dev branch when not specified
            var result = baselineBranches.TryFindRefCommit(commits, [_devJob], _devBranch, out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]
    public void TryFindRefCommit_SingleLocalCommit_FindsRefCommit()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithBaselineStore(_mainBranch, _rootJob, out var baselineStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var baselineBranch = new BaselineBranch(baselineStore);
            baselineBranch.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            baselineBranch.TryAdd(rootBuild);

            var baselineBranches = new[] { baselineBranch };
            var localCommit = RandomData.NextSha1();
            var refCommit = rootBuild.Commits[0].Sha1;
            var commits = new[] { localCommit, refCommit };

            var result = baselineBranches.TryFindRefCommit(commits, [_rootJob], _mainBranch, out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]
    public void TryFindRefCommit_EmptyBranchReferences_ReturnsFalse()
    {
        var baselineBranches = Array.Empty<BaselineBranch>();
        var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1() };

        var result = baselineBranches.TryFindRefCommit(commits, [new JobName("UnknownJob")], _mainBranch, out var foundRefCommit);

        Assert.That(result, Is.False);
        Assert.That(foundRefCommit, Is.Null);
    }
}
