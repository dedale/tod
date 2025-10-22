using Moq;
using NUnit.Framework;
using System.Diagnostics;
using System.Text.Json;
using Tod.Git;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BranchReferenceTests
{
    [Test]
    public void TryAdd_RootBuildTwice_OnlyFirstTime()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = "MyJob";
            var branchReference = new BranchReference(new("main"), new(jobName));
            var testJobName1 = "MyTestJob1";
            var testJobName2 = "MyTestJob2";
            var rootBuild = RandomData.NextRootBuild(jobName: jobName, testJobNames: [ testJobName1, testJobName2 ]);
            Assert.That(branchReference.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(branchReference.RootBuilds[0], Has.Count.EqualTo(0));
            Assert.That(branchReference.TestBuilds, Has.Count.EqualTo(0));
            var added = branchReference.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            Assert.That(branchReference.RootBuilds, Has.Count.EqualTo(1));
            Assert.That(branchReference.RootBuilds[0], Has.Count.EqualTo(1));
            Assert.That(branchReference.RootBuilds[0].JobName.Value, Is.EqualTo(jobName));
            Assert.That(branchReference.RootBuilds[0].Contains(rootBuild.BuildNumber), Is.True);
            Assert.That(branchReference.TestBuilds, Has.Count.EqualTo(2));
            Assert.That(branchReference.TestBuilds[0].JobName.Value, Is.EqualTo(testJobName1));
            Assert.That(branchReference.TestBuilds[0], Has.Count.EqualTo(0));
            Assert.That(branchReference.TestBuilds[1].JobName.Value, Is.EqualTo(testJobName2));
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
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value);
            var added = branchReference.RootBuilds.GetOrAdd(jobName).TryAdd(rootBuild);
            Assert.That(added, Is.True);
            var json = JsonSerializer.Serialize(new BranchReference.Serializable(branchReference));
            var reloaded = JsonSerializer.Deserialize<BranchReference.Serializable>(json);
            Assert.That(reloaded, Is.Not.Null);
            Debug.Assert(reloaded is not null);
            var clone = reloaded.FromSerializable();
            Assert.That(clone!.BranchName, Is.EqualTo(branchReference.BranchName));
            Assert.That(clone.RootBuilds, Has.Count.EqualTo(branchReference.RootBuilds.Count));
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithoutBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var found = branchReference.TryFindRootBuildByCommit(RandomData.NextSha1(), jobName, out var foundRootBuild);
            Assert.That(found, Is.False);
            Assert.That(foundRootBuild, Is.Null);
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithOneBuild_ReturnsBuild()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var commits = 3;
            var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, commits: commits);
            var added = branchReference.TryAdd(rootBuild);
            Assert.That(added, Is.True);
            for (var c = 0; c < commits; c++)
            {
                var found = branchReference.TryFindRootBuildByCommit(rootBuild.Commits[c], jobName, out var foundRootBuild);
                Assert.That(found, Is.True);
                Debug.Assert(foundRootBuild is not null);
                Assert.That(foundRootBuild.Reference, Is.EqualTo(rootBuild.Reference));
            }
            Assert.That(branchReference.TryFindRootBuildByCommit(RandomData.NextSha1(), jobName, out _), Is.False);
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithMultipleBuilds_ReturnsBuild()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var builds = 5;
            var commitsPerBuild = 3;
            var rootBuilds = new List<RootBuild>();
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: b + 1, commits: commitsPerBuild);
                var added = branchReference.TryAdd(rootBuild);
                Assert.That(added, Is.True);
                rootBuilds.Add(rootBuild);
            }
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = rootBuilds[b];
                for (var c = 0; c < commitsPerBuild; c++)
                {
                    var found = branchReference.TryFindRootBuildByCommit(rootBuild.Commits[c], jobName, out var foundRootBuild);
                    Assert.That(found, Is.True);
                    Debug.Assert(foundRootBuild is not null);
                    Assert.That(foundRootBuild.Reference, Is.EqualTo(rootBuild.Reference));
                }
            }
            Assert.That(branchReference.TryFindRootBuildByCommit(RandomData.NextSha1(), jobName, out _), Is.False);
        }
    }

    [Test]
    public void TryFindRootBuildByCommit_WithMultipleFailedBuilds_ReturnsNone()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var builds = 5;
            var commitsPerBuild = 3;
            var rootBuilds = new List<RootBuild>();
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, buildNumber: b + 1, isSuccessful: false, commits: commitsPerBuild);
                var added = branchReference.TryAdd(rootBuild);
                Assert.That(added, Is.True);
                rootBuilds.Add(rootBuild);
            }
            for (var b = 0; b < builds; b++)
            {
                var rootBuild = rootBuilds[b];
                for (var c = 0; c < commitsPerBuild; c++)
                {
                    Assert.That(branchReference.TryFindRootBuildByCommit(rootBuild.Commits[c], jobName, out _), Is.False);
                }
            }
            Assert.That(branchReference.TryFindRootBuildByCommit(RandomData.NextSha1(), jobName, out _), Is.False);
        }
    }

    [Test]
    public void TryAdd_TestBuildTwice_OnlyFirstTime()
    {
        var branchReference = new BranchReference(new("main"), new("MyJob"));
        var testBuild = RandomData.NextTestBuild(testJobName: "MyTestJob");
        var added = branchReference.TryAdd(testBuild);
        Assert.That(added, Is.True);
        added = branchReference.TryAdd(testBuild);
        Assert.That(added, Is.False);
    }

    [Test]
    public void TryFindTestBuild_WithoutBuilds_ReturnsNone()
    {
        var branchReference = new BranchReference(new("main"), new("MyJob"));
        Assert.That(branchReference.TryFindTestBuild(new("MyTestJob"), new BuildReference("MyJob", 42), out var foundTestBuild), Is.False);
        Assert.That(foundTestBuild, Is.Null);
    }

    [Test]
    public void TryFindTestBuild_WithOldRootBuild_ReturnsOnlyLatest()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootJobName = "MyJob";
            var branchReference = new BranchReference(new("main"), new(rootJobName));
            var rootBuild = new BuildReference(rootJobName, RandomData.NextBuildNumber);
            var testBuildNumber = RandomData.NextBuildNumber;
            // Add a test build for root build
            Assert.That(branchReference.TryAdd(RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild)), Is.True);
            // Try to find test build for an older root build - should return none
            rootBuild = rootBuild.Next();
            Assert.That(branchReference.TryFindTestBuild(new("MyTestJob"), rootBuild, out var foundTestBuild), Is.False);
            Assert.That(foundTestBuild, Is.Null);
            // Add a test build for the newer root build
            testBuildNumber++;
            var testBuild = RandomData.NextTestBuild(buildNumber: testBuildNumber, rootBuild: rootBuild);
            Assert.That(branchReference.TryAdd(testBuild), Is.True);
            // Try to find test build for the newer root build - should return the newer one
            Assert.That(branchReference.TryFindTestBuild(new("MyTestJob"), rootBuild, out foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(testBuild.Reference));
        }
    }

    [Test]
    public void TryFindTestBuild_WithNewerRootBuild_Works()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootJobName = "MyJob";
            var branchReference = new BranchReference(new("main"), new(rootJobName));
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(rootJobName, buildNumber);
            var testBuild = RandomData.NextTestBuild(rootBuild: rootBuild.Next());
            Assert.That(branchReference.TryAdd(testBuild), Is.True);
            Assert.That(branchReference.TryFindTestBuild(new("MyTestJob"), rootBuild, out var foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(testBuild.Reference));
        }
    }

    [Test]
    public void TryFindTestBuild_WithLowerAndGreaterRootBuilds_ConsiderOnlyLatest()
    {
        using (Assert.EnterMultipleScope())
        {
            var rootJobName = "MyJob";
            var branchReference = new BranchReference(new("main"), new(rootJobName));
            var rootBuild = new BuildReference(rootJobName, RandomData.NextBuildNumber);
            // Add an old test build for root build N-1
            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(rootBuild.JobName, rootBuild.BuildNumber - 1));
            Assert.That(branchReference.TryAdd(oldTestBuild), Is.True);
            // Add a new test build for root build N+1
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(branchReference.TryAdd(newTtestBuild), Is.True);
            // Search for test build for root build N - should find the newer one (N+1), not the older one (N-1)
            Assert.That(branchReference.TryFindTestBuild(new("MyTestJob"), rootBuild, out var foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(newTtestBuild.Reference));
        }
    }

    [Test]
    public void TryFindTestBuild_WithManyRootBuildJobNames_IgnoreOther()
    {
        using (Assert.EnterMultipleScope())
        {
            var oldRootJobName = "OldJob";
            var rootJobName = "MyJob";
            var branchReference = new BranchReference(new("main"), new(rootJobName));
            var buildNumber = RandomData.NextBuildNumber;
            var rootBuild = new BuildReference(rootJobName, buildNumber);
            var oldTestBuild = RandomData.NextTestBuild(rootBuild: new BuildReference(oldRootJobName, RandomData.NextBuildNumber));
            Assert.That(branchReference.TryAdd(oldTestBuild), Is.True);
            var newTtestBuild = RandomData.NextTestBuild(buildNumber: oldTestBuild.BuildNumber + 1, rootBuild: rootBuild.Next());
            Assert.That(branchReference.TryAdd(newTtestBuild), Is.True);
            Assert.That(branchReference.TryFindTestBuild(new("MyTestJob"), rootBuild, out var foundTestBuild), Is.True);
            Debug.Assert(foundTestBuild is not null);
            Assert.That(foundTestBuild.Reference, Is.EqualTo(newTtestBuild.Reference));
        }
    }

    [Test]
    public void TryFindRefCommit_EmptyCommitArray_ThrowsNotSupportedException()
    {
        var jobName = new JobName("MyJob");
        var branchReference = new BranchReference(new("main"), jobName);
        var branchReferences = new[] { branchReference };
        var commits = Array.Empty<Sha1>();

        Assert.That(branchReferences.TryFindRefCommit(commits, [jobName], new BranchName("main"), out _), Is.False);
    }

    [Test]
    public void TryFindRefCommit_FirstCommitInBranchHistory_ThrowsNotSupportedException()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var commits = new[] { rootBuild.Commits[0], RandomData.NextSha1() };

            Assert.That(
                () => branchReferences.TryFindRefCommit(commits, [jobName], new BranchName("main"), out _),
                Throws.TypeOf<NotSupportedException>()
                    .With.Message.EqualTo("No local commits to test for job MyJob"));
        }
    }

    [Test]
    public void TryFindRefCommit_WithSpecificBranch_FindsRefCommit()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var localCommit = RandomData.NextSha1();
            var refCommit = rootBuild.Commits[1];
            var commits = new[] { localCommit, refCommit };

            var result = branchReferences.TryFindRefCommit(commits, [jobName], new BranchName("main"), out var foundRefCommit);

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
            var branchReference = new BranchReference(new("main"), jobName1);
            branchReference.RootBuilds.GetOrAdd(jobName2);

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
            var result = branchReferences.TryFindRefCommit(commits, [jobName1, jobName2], new BranchName("main"), out var foundRefCommit);
            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]
    public void TryFindRefCommit_WithSpecificBranch_BranchNotFound_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1() };

            var result = branchReferences.TryFindRefCommit(commits, [jobName], new BranchName("nonexistent"), out var foundRefCommit);

            Assert.That(result, Is.False);
            Assert.That(foundRefCommit, Is.Null);
        }
    }

    [Test]
    public void TryFindRefCommit_WithSpecificBranch_RefCommitNotFound_ReturnsFalse()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1(), RandomData.NextSha1() };

            var result = branchReferences.TryFindRefCommit(commits, [jobName], new BranchName("main"), out var foundRefCommit);

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
            var mainBranchRef = new BranchReference(new("main"), mainJob);
            var mainRootBuild = RandomData.NextRootBuild(jobName: mainJob.Value, commits: 3);
            mainBranchRef.TryAdd(mainRootBuild);

            var devJob = new JobName("DevJob");
            var devBranchRef = new BranchReference(new("dev"), devJob);
            var devRootBuild = RandomData.NextRootBuild(jobName: devJob.Value, commits: 3);
            devBranchRef.TryAdd(devRootBuild);

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
            Assert.That(foundBranch, Is.EqualTo(new BranchName("main")));
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
            var mainBranchRef = new BranchReference(new("main"), mainJob);
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
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, commits: 5);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var localCommit1 = RandomData.NextSha1();
            var localCommit2 = RandomData.NextSha1();
            var refCommit1 = rootBuild.Commits[1];
            var refCommit2 = rootBuild.Commits[2];
            var commits = new[] { localCommit1, localCommit2, refCommit1, refCommit2 };

            var result = branchReferences.TryFindRefCommit(commits, [jobName], new BranchName("main"), out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit1));
        }
    }

    [Test]
    public void TryFindRefCommit_MultipleBranches_FindsCorrectBranch()
    {
        using (Assert.EnterMultipleScope())
        {
            var mainJobName = new JobName("MainJob");
            var mainBranch = new BranchReference(new("main"), mainJobName);
            var mainRootBuild = RandomData.NextRootBuild(jobName: mainJobName.Value, commits: 3);
            mainBranch.TryAdd(mainRootBuild);

            var devJobName = new JobName("DevJob");
            var devBranch = new BranchReference(new("dev"), devJobName);
            var devRootBuild = RandomData.NextRootBuild(jobName: devJobName.Value, commits: 3);
            devBranch.TryAdd(devRootBuild);

            var branchReferences = new[] { mainBranch, devBranch };
            var localCommit = RandomData.NextSha1();
            var refCommit = devRootBuild.Commits[1];
            var commits = new[] { localCommit, refCommit };

            // Should find dev branch when not specified
            var result = branchReferences.TryFindRefCommit(commits, [devJobName], new("dev"), out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]
    public void TryFindRefCommit_SingleLocalCommit_FindsRefCommit()
    {
        using (Assert.EnterMultipleScope())
        {
            var jobName = new JobName("MyJob");
            var branchReference = new BranchReference(new("main"), jobName);
            var rootBuild = RandomData.NextRootBuild(jobName: jobName.Value, commits: 3);
            branchReference.TryAdd(rootBuild);

            var branchReferences = new[] { branchReference };
            var localCommit = RandomData.NextSha1();
            var refCommit = rootBuild.Commits[0];
            var commits = new[] { localCommit, refCommit };

            var result = branchReferences.TryFindRefCommit(commits, [jobName], new BranchName("main"), out var foundRefCommit);

            Assert.That(result, Is.True);
            Assert.That(foundRefCommit, Is.EqualTo(refCommit));
        }
    }

    [Test]
    public void TryFindRefCommit_EmptyBranchReferences_ReturnsFalse()
    {
        var branchReferences = Array.Empty<BranchReference>();
        var commits = new[] { RandomData.NextSha1(), RandomData.NextSha1() };

        var result = branchReferences.TryFindRefCommit(commits, [new JobName("UnknownJob")], new BranchName("main"), out var foundRefCommit);

        Assert.That(result, Is.False);
        Assert.That(foundRefCommit, Is.Null);
    }
}
