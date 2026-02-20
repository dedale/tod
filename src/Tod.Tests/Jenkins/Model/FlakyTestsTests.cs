using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class FlakyTestsTests
{
    // Tests for FlakyTests would go here

    private static readonly BranchName s_main = new("main");
    private static readonly JobName s_testJob = new("MainTest");

    [Test]
    public void IsFlaky_WithFlakyTest_ReturnsTrue()
    {
        var flakyStore = InMemoryFlakyStore.Default;
        var flakyTests = new FlakyTests(flakyStore);

        var baselineBranches = new List<BaselineBranch>();
        var baselineStore = new InMemoryBaselineStore(s_main);
        var baselineBranch = new BaselineBranch(baselineStore);
        baselineBranches.Add(baselineBranch);

        var buildCount = 30;
        var buildNumber = RandomData.NextBuildNumber;
        var failedTest = new FailedTest("FlakyClass", "FlakyTest", "FlakyDetails");
        var flakies = 0;
        for (var i = 0; i < buildCount; i++, buildNumber++)
        {
            var failedTests = new List<FailedTest>();
            if (i % 7 == 5)
            {
                flakies++;
                failedTests.Add(failedTest);
            }
            var testBuild = RandomData.NextTestBuild(testJobName: s_testJob.Value, buildNumber: buildNumber, failedTests: [.. failedTests]);
            Assert.That(baselineBranch.TryAdd(testBuild), Is.True);
            buildNumber++;
        }

        flakyTests.Update(baselineBranches);

        Assert.That(flakyTests.IsFlaky(s_testJob, failedTest), Is.True);
    }

    [Test]
    public void IsFlaky_WithoutFlakyTest_ReturnsFalse()
    {
        var flakyStore = InMemoryFlakyStore.Default;
        var flakyTests = new FlakyTests(flakyStore);

        var baselineBranches = new List<BaselineBranch>();
        var baselineStore = new InMemoryBaselineStore(s_main);
        var baselineBranch = new BaselineBranch(baselineStore);
        baselineBranches.Add(baselineBranch);

        var buildCount = 30;
        bool IsFlaky(int ith) => ith / 10 == 2;
        var buildNumber = RandomData.NextBuildNumber;
        var failedTest = new FailedTest("Class", "Test", "Details");
        for (var i = 0; i < buildCount; i++, buildNumber++)
        {
            var failedTests = new List<FailedTest>();
            if (IsFlaky(i))
            {
                failedTests.Add(failedTest);
            }
            var testBuild = RandomData.NextTestBuild(testJobName: s_testJob.Value, buildNumber: buildNumber, failedTests: [.. failedTests]);
            Assert.That(baselineBranch.TryAdd(testBuild), Is.True);
            buildNumber++;
        }

        flakyTests.Update(baselineBranches);

        Assert.That(flakyTests.IsFlaky(s_testJob, failedTest), Is.False);
    }

    [Test]
    public void Serialization_Works()
    {
        var flakyStore = InMemoryFlakyStore.Default;
        var flakyTests = new FlakyTests(flakyStore);

        var baselineBranches = new List<BaselineBranch>();
        var baselineStore = new InMemoryBaselineStore(s_main);
        var baselineBranch = new BaselineBranch(baselineStore);
        baselineBranches.Add(baselineBranch);

        var buildCount = 30;
        var buildNumber = RandomData.NextBuildNumber;
        var failedTest = new FailedTest("FlakyClass", "FlakyTest", "FlakyDetails");
        for (var i = 0; i < buildCount; i++, buildNumber++)
        {
            var failedTests = new List<FailedTest>();
            if (RandomData.IsFlaky())
            {
                failedTests.Add(failedTest);
            }
            var testBuild = RandomData.NextTestBuild(testJobName: s_testJob.Value, buildNumber: buildNumber, failedTests: [.. failedTests]);
            Assert.That(baselineBranch.TryAdd(testBuild), Is.True);
            buildNumber++;
        }

        flakyTests.Update(baselineBranches);

        var serializable = flakyTests.ToSerializable();
        var reloaded = serializable.FromSerializable(flakyStore);

        Assert.That(flakyTests.IsFlaky(s_testJob, failedTest), Is.True);
    }
}
