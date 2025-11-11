using Moq;
using NUnit.Framework;
using System.Diagnostics;
using Tod.Git;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BranchReferenceTests
{
    private readonly BranchName _mainBranch = new("main");
    private readonly JobName _rootJob = new("MyJob");
    private readonly JobName _testJob1 = new("MyTestJob1");
    private readonly JobName _testJob2 = new("MyTestJob2");

    private readonly BranchName _devBranch = new("dev");
    private readonly JobName _devJob = new("DevJob");

    private StoreMocks.BuildStoreMocks DevBranchMocks(out BranchReference devBranchRef, out RootBuild devRootBuild)
    {
        var devTestJob = new JobName("DevTestJob");
        var devMocks = StoreMocks.New()
            .WithReferenceStore(_devBranch, _devJob, out var devReferenceStore)
            .WithNewRootBuilds(_devJob)
            .WithTestobs(devTestJob);
        devBranchRef = new BranchReference(devReferenceStore);
        devBranchRef.TryAddRoot(_devJob);
        devRootBuild = RandomData.NextRootBuild(jobName: _devJob.Value, commits: 3, testJobNames: [devTestJob.Value]);
        devBranchRef.TryAdd(devRootBuild);
        return devMocks;
    }

    [Test]
    public void TryAdd_RootBuildTwice_OnlyFirstTime()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);

            branchReference.TryAddRoot(_rootJob);

            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value);
            Assert.That(branchReference.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(branchReference.RootBuilds[0], Has.Count.EqualTo(0));
            Assert.That(branchReference.TestBuilds, Has.Count.EqualTo(0));

            var added = branchReference.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            Assert.That(branchReference.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(branchReference.RootBuilds[0], Has.Count.EqualTo(1));
            Assert.That(branchReference.RootBuilds[0].JobName.Value, Is.EqualTo(_rootJob.Value));
            Assert.That(branchReference.RootBuilds[0].Contains(rootBuild.BuildNumber), Is.True);
            Assert.That(branchReference.TestBuilds, Has.Count.EqualTo(2));
            Assert.That(branchReference.TestBuilds[0].JobName.Value, Is.EqualTo(_testJob1.Value));
            Assert.That(branchReference.TestBuilds[0], Has.Count.EqualTo(0));
            Assert.That(branchReference.TestBuilds[1].JobName.Value, Is.EqualTo(_testJob2.Value));
            Assert.That(branchReference.TestBuilds[1], Has.Count.EqualTo(0));

            added = branchReference.TryAdd(rootBuild);
            Assert.That(added, Is.False);
        }
    }

    [Test]
    public void Serialization_Works() // with root builds only for now
    {
        using (Assert.EnterMultipleScope())
        {
            using var temp = new TempDirectory();
            var referenceStore = new ReferenceStore(_mainBranch, temp.Path);
            var branchReference = new BranchReference(referenceStore);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value);
            var added = branchReference.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            var clone = new BranchReference(new ReferenceStore(_mainBranch, temp.Path));
            Assert.That(clone!.BranchName, Is.EqualTo(branchReference.BranchName));
            Assert.That(clone.RootBuilds, Has.Count.EqualTo(branchReference.RootBuilds.Count));
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithoutBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithRootJobs(_rootJob);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);

            var found = branchReference.TryFindRootBuildByCommit(RandomData.NextSha1(), _rootJob, out var foundRootBuild);
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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var commits = 3;
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: commits);
            var added = branchReference.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            for (var c = 0; c < commits; c++)
            {
                var found = branchReference.TryFindRootBuildByCommit(rootBuild.Commits[c], _rootJob, out var foundRootBuild);
                Assert.That(found, Is.True);
                Debug.Assert(foundRootBuild is not null);
                Assert.That(foundRootBuild.Reference, Is.EqualTo(rootBuild.Reference));
            }
            Assert.That(branchReference.TryFindRootBuildByCommit(RandomData.NextSha1(), _rootJob, out _), Is.False);
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithMultipleBuilds_ReturnsBuild()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var builds = 5;
            var commitsPerBuild = 3;
            var rootBuilds = new List<RootBuild>();
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, buildNumber: b + 1, commits: commitsPerBuild);
                var added = branchReference.TryAdd(rootBuild);
                Assert.That(added, Is.True);
                rootBuilds.Add(rootBuild);
            }
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = rootBuilds[b];
                for (var c = 0; c < commitsPerBuild; c++)
                {
                    var found = branchReference.TryFindRootBuildByCommit(rootBuild.Commits[c], _rootJob, out var foundRootBuild);
                    Assert.That(found, Is.True);
                    Debug.Assert(foundRootBuild is not null);
                    Assert.That(foundRootBuild.Reference, Is.EqualTo(rootBuild.Reference));
                }
            }
            Assert.That(branchReference.TryFindRootBuildByCommit(RandomData.NextSha1(), _rootJob, out _), Is.False);
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithMultipleFailedBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var builds = 5;
            var commitsPerBuild = 3;
            var rootBuilds = new List<RootBuild>();
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, buildNumber: b + 1, isSuccessful: false, commits: commitsPerBuild);
                var added = branchReference.TryAdd(rootBuild);
                Assert.That(added, Is.True);
                rootBuilds.Add(rootBuild);
            }
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = rootBuilds[b];
                for (var c = 0; c < commitsPerBuild; c++)
                {
                    Assert.That(branchReference.TryFindRootBuildByCommit(rootBuild.Commits[c], _rootJob, out _), Is.False);
                }
            }
            Assert.That(branchReference.TryFindRootBuildByCommit(RandomData.NextSha1(), _rootJob, out _), Is.False);
        }
    }

    [Test]
    public void TryAdd_TestBuildTwice_OnlyFirstTime()
    {
        var testJobName = new JobName("MyTestJob");

        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
            .WithNewTestBuilds(testJobName);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_rootJob);

        var testBuild = RandomData.NextTestBuild(testJobName: testJobName.Value);
        var added = branchReference.TryAdd(testBuild);
        Assert.That(added, Is.True);

        added = branchReference.TryAdd(testBuild);
        Assert.That(added, Is.False);
    }

    [Test]
    public void TryFindTestBuild_WithoutBuilds_ReturnsNone()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_rootJob);
        Assert.That(branchReference.TryFindTestBuild(new("MyTestJob"), new BuildReference(_rootJob, 42), out var foundTestBuild), Is.False);
        Assert.That(foundTestBuild, Is.Null);
    }

    [Test]
    public void TryFindTestBuild_WithOldRootBuild_ReturnsOnlyLatest()
    {
        using (Assert.EnterMultipleScope())
        {
            var testJobName = new JobName("MyTestJob");

            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewTestBuilds(testJobName);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var rootBuild = new BuildReference(_rootJob, RandomData.NextBuildNumber);
            var testBuildNumber = RandomData.NextBuildNumber;
            // Add a test build for root build
            var testBuild = RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild);
            Assert.That(branchReference.TryAdd(testBuild), Is.True);
            // Try to find test build for an older root build - should return none
            rootBuild = rootBuild.Next();
            Assert.That(branchReference.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.False);
            Assert.That(foundTestBuild, Is.Null);
            // Add a test build for the newer root build
            testBuildNumber++;
            testBuild = RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild);
            Assert.That(branchReference.TryAdd(testBuild), Is.True);
            // Try to find test build for the newer root build - should return the newer one
            Assert.That(branchReference.TryFindTestBuild(testJobName, rootBuild, out foundTestBuild), Is.True);
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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewTestBuilds(testJobName);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);

            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(_rootJob, buildNumber);
            var testBuild = RandomData.NextTestBuild(rootBuild: rootBuild.Next());
            Assert.That(branchReference.TryAdd(testBuild), Is.True);
            Assert.That(branchReference.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.True);
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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewTestBuilds(testJobName);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var rootBuild = new BuildReference(_rootJob, RandomData.NextBuildNumber);

            // Add an old test build for root build N-1
            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(rootBuild.JobName, rootBuild.BuildNumber - 1));
            Assert.That(branchReference.TryAdd(oldTestBuild), Is.True);
            // Add a new test build for root build N+1
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(branchReference.TryAdd(newTtestBuild), Is.True);

            // Search for test build for root build N - should find the newer one (N+1), not the older one (N-1)
            Assert.That(branchReference.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.True);
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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewTestBuilds(testJobName);

            var oldRootJob = new JobName("OldJob");
            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(_rootJob, buildNumber);

            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(oldRootJob, RandomData.NextBuildNumber));
            Assert.That(branchReference.TryAdd(oldTestBuild), Is.True);
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(branchReference.TryAdd(newTtestBuild), Is.True);
            Assert.That(branchReference.TryFindTestBuild(testJobName, rootBuild, out var foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(newTtestBuild.Reference));
        }
    }

    [Test]
    public void TryFindRefCommit_EmptyCommitArray_ThrowsNotSupportedException()
    {
        using var mocks = StoreMocks.New()
            .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore);

        var branchReference = new BranchReference(referenceStore);
        branchReference.TryAddRoot(_rootJob);
        var branchReferences = new[] { branchReference };
        var commits = Array.Empty<Sha1>();

        Assert.That(branchReferences.TryFindRefCommit(commits, [_rootJob], _mainBranch, out _), Is.False);
    }

    [Test]
    public void TryFindRefCommit_FirstCommitInBranchHistory_ThrowsNotSupportedException()
    {
        using (Assert.EnterMultipleScope())
        {
            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);

            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var commits = new[] { rootBuild.Commits[0], RandomData.NextSha1() };

            Assert.That(
                () => branchReferences.TryFindRefCommit(commits, [_rootJob], _mainBranch, out _),
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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var localCommit = RandomData.NextSha1();
            var refCommit = rootBuild.Commits[1];
            var commits = new[] { localCommit, refCommit };

            var result = branchReferences.TryFindRefCommit(commits, [_rootJob], _mainBranch, out var foundRefCommit);

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
                .WithReferenceStore(_mainBranch, [jobName1, jobName2], out var referenceStore)
                .WithNewRootBuilds(jobName1)
                .WithNewRootBuilds(jobName2)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(jobName1);
            branchReference.TryAddRoot(jobName2);

            var rootBuild1 = RandomData.NextRootBuild(jobName: jobName1.Value, commits: 3);
            branchReference.TryAdd(rootBuild1);

            var rootBuild2 = RandomData.NextRootBuild(jobName: jobName2.Value, commits: 3);
            rootBuild2 = new RootBuild(
                rootBuild2.JobName,
                rootBuild2.Id,
                rootBuild2.BuildNumber,
                rootBuild2.StartTimeUtc,
                rootBuild2.EndTimeUtc,
                rootBuild2.IsSuccessful,
                rootBuild1.Commits,
                rootBuild2.Triggered);
            branchReference.TryAdd(rootBuild2);

            var branchReferences = new[] { branchReference };
            var localCommit = RandomData.NextSha1();
            var refCommit = rootBuild2.Commits[2];
            var commits = new[] { localCommit, refCommit };
            var result = branchReferences.TryFindRefCommit(commits, [jobName1, jobName2], _mainBranch, out var foundRefCommit);
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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            branchReference.TryAdd(rootBuild);
 
            var branchReferences = new[] { branchReference };
            var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1() };

            var result = branchReferences.TryFindRefCommit(commits, [_rootJob], new BranchName("nonexistent"), out var foundRefCommit);

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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1(), RandomData.NextSha1() };

            var result = branchReferences.TryFindRefCommit(commits, [_rootJob], _mainBranch, out var foundRefCommit);

            Assert.That(result, Is.False);
            Assert.That(foundRefCommit, Is.Null);
        }
    }

    [Test]
    public void TryGuessBranch_FindsRefCommitInFirstMatchingBranch()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootNames = new[] { new RootName("Job") };
            var mainJob = new JobName("MainJob");

            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, mainJob, out var referenceStore)
                .WithNewRootBuilds(mainJob)
                .WithTestobs(_testJob1, _testJob2);

            var mainBranchRef = new BranchReference(referenceStore);
            mainBranchRef.TryAddRoot(mainJob);
            var mainRootBuild = RandomData.NextRootBuild(jobName: mainJob.Value, commits: 3);
            mainBranchRef.TryAdd(mainRootBuild);

            using var devMocks = DevBranchMocks(out var devBranchRef, out var devRootBuild);

            var branchReferences = new[] { mainBranchRef, devBranchRef };
            var localCommit = RandomData.NextSha1();
            var refCommit = mainRootBuild.Commits[1];
            var commits = new[] { localCommit, refCommit };

            var onDemandJob = new JobName("CustomJob");
            var expectedRootDiffs = new[] { new RootDiff(mainJob, onDemandJob) };
            var filterManager = new Mock<IFilterManager>(MockBehavior.Strict);
            filterManager.Setup(f => f.GetRootDiffs(rootNames, mainBranchRef.BranchName)).Returns(expectedRootDiffs);

            var result = branchReferences.TryGuessBranch(commits, rootNames, filterManager.Object, out var rootDiffs, out var foundBranch, out var foundRefCommit);

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
            var rootNames = new[] { new RootName("Job") };
            var mainJob = new JobName("MainJob");

            using var mocks = StoreMocks.New()
                .WithReferenceStore(_mainBranch, mainJob, out var referenceStore)
                .WithNewRootBuilds(mainJob)
                .WithTestobs(_testJob1, _testJob2);

            var mainBranchRef = new BranchReference(referenceStore);
            mainBranchRef.TryAddRoot(mainJob);
            var mainRootBuild = RandomData.NextRootBuild(jobName: mainJob.Value, commits: 3);
            mainBranchRef.TryAdd(mainRootBuild);

            var branchReferences = new[] { mainBranchRef };
            var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1(), RandomData.NextSha1() };

            var onDemandJob = new JobName("CustomJob");
            var expectedRootDiffs = new[] { new RootDiff(mainJob, onDemandJob) };
            var filterManager = new Mock<IFilterManager>(MockBehavior.Strict);
            filterManager.Setup(f => f.GetRootDiffs(rootNames, mainBranchRef.BranchName)).Returns(expectedRootDiffs);

            var result = branchReferences.TryGuessBranch(commits, rootNames, filterManager.Object, out var rootDiffs, out var foundBranch, out var foundRefCommit);

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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 5);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var localCommit1 = RandomData.NextSha1();
            var localCommit2 = RandomData.NextSha1();
            var refCommit1 = rootBuild.Commits[1];
            var refCommit2 = rootBuild.Commits[2];
            var commits = new[] { localCommit1, localCommit2, refCommit1, refCommit2 };

            var result = branchReferences.TryFindRefCommit(commits, [_rootJob], _mainBranch, out var foundRefCommit);

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
                .WithReferenceStore(_mainBranch, mainJob, out var referenceStore)
                .WithNewRootBuilds(mainJob)
                .WithTestobs(_testJob1, _testJob2);

            var mainBranchRef = new BranchReference(referenceStore);
            mainBranchRef.TryAddRoot(mainJob);
            var mainRootBuild = RandomData.NextRootBuild(jobName: mainJob.Value, commits: 3);
            mainBranchRef.TryAdd(mainRootBuild);

            using var devMocks = DevBranchMocks(out var devBranchRef, out var devRootBuild);

            var branchReferences = new[] { mainBranchRef, devBranchRef };
            var localCommit = RandomData.NextSha1();
            var refCommit = devRootBuild.Commits[1];
            var commits = new[] { localCommit, refCommit };

            // Should find dev branch when not specified
            var result = branchReferences.TryFindRefCommit(commits, [_devJob], _devBranch, out var foundRefCommit);

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
                .WithReferenceStore(_mainBranch, _rootJob, out var referenceStore)
                .WithNewRootBuilds(_rootJob)
                .WithTestobs(_testJob1, _testJob2);

            var branchReference = new BranchReference(referenceStore);
            branchReference.TryAddRoot(_rootJob);
            var rootBuild = RandomData.NextRootBuild(jobName: _rootJob.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var localCommit = RandomData.NextSha1();
            var refCommit = rootBuild.Commits[0];
            var commits = new[] { localCommit, refCommit };

            var result = branchReferences.TryFindRefCommit(commits, [_rootJob], _mainBranch, out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]
    public void TryFindRefCommit_EmptyBranchReferences_ReturnsFalse()
    {
        var branchReferences = Array.Empty<BranchReference>();
        var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1() };

        var result = branchReferences.TryFindRefCommit(commits, [new JobName("UnknownJob")], _mainBranch, out var foundRefCommit);

        Assert.That(result, Is.False);
        Assert.That(foundRefCommit, Is.Null);
    }
}
