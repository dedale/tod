using NUnit.Framework;
using Serilog;
using Serilog.Sinks.TestCorrelator;
using System.Text.RegularExpressions;
using Tod.Core;
using Tod.Jenkins;
using Tod.Tests.Core;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class FilterAnalyzerTests
{
    [Test]
    public void Run_NoFiltersOrJobs_ReturnsEmpty()
    {
        var config = JenkinsConfig.New("http://localhost");
        var jobGroups = new JobGroups([], []);
        var analyzer = new FilterAnalyzer(config, jobGroups);

        var result = analyzer.Run();

        Assert.That(result.Chains, Is.Empty);
        Assert.That(result.TestGroups, Is.Empty);
        Assert.That(result.Errors, Is.Empty);
    }

    [Test]
    public void Run_NoFiltersOrJobs_ForCoverage()
    {
        var config = JenkinsConfig.New("http://localhost");
        var jobGroups = new JobGroups([], []);
        FilterAnalyzer.LogFilters(config, jobGroups, []);
    }

    [Test]
    public void Run_RootFilterMatchesRootJob_CreatesChainFilter()
    {
        var rootFilter = new RootFilter("build", "build");
        var config = JenkinsConfig.New("http://localhost", rootFilters: [rootFilter]);

        var rootGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-build") },
            new("CUSTOM-build")
        );
        var byRoot = new Dictionary<RootName, JobGroup> { [new RootName("build")] = rootGroup };
        var jobGroups = new JobGroups(byRoot, []);

        var analyzer = new FilterAnalyzer(config, jobGroups);
        var result = analyzer.Run();

        Assert.That(result.Chains, Has.Length.EqualTo(1));
        Assert.That(result.Errors, Is.Empty);

        var chainFilter = result.GetChainFilters(result.Chains[0]);
        Assert.That(chainFilter.Name, Is.EqualTo(RootFilter.DefaultChain));
        Assert.That(chainFilter.RootFilter, Is.EqualTo(rootFilter));
        Assert.That(chainFilter.RootJob.Value, Is.EqualTo("CUSTOM-build"));
        Assert.That(chainFilter.TestsByFilter, Is.Empty);
    }

    [Test]
    public void Run_RootFilterDoesNotMatch_AddsError()
    {
        var rootFilter = new RootFilter("build", "build");
        var config = JenkinsConfig.New("http://localhost", rootFilters: [rootFilter]);

        var rootGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-deploy") },
            new("CUSTOM-deploy")
        );
        var byRoot = new Dictionary<RootName, JobGroup> { [new RootName("deploy")] = rootGroup };
        var jobGroups = new JobGroups(byRoot, []);

        var analyzer = new FilterAnalyzer(config, jobGroups);
        var result = analyzer.Run();

        Assert.That(result.Chains, Is.Empty);
        Assert.That(result.Errors, Has.Length.EqualTo(1));
        Assert.That(result.Errors[0], Is.EqualTo("RootFilter 'build' does not match any root job"));
    }

    [Test]
    public void Run_TestFilterInChainGroupMatchesWithChain_AddsToChainFilters()
    {
        var rootFilter = new RootFilter("build", @"build-(?<chain>\w+)");
        var testFilter = new TestFilter("test", @"test-(?<chain>\w+)", "chains");
        var config = JenkinsConfig.New("http://localhost", rootFilters: [rootFilter], chainTestGroup: "chains", testFilters: [testFilter]);

        var rootGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-build-feature") },
            new("CUSTOM-build-feature")
        );
        var testGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-test-feature") },
            new("CUSTOM-test-feature")
        );
        var byRoot = new Dictionary<RootName, JobGroup> { [new RootName("build-feature")] = rootGroup };
        var byTest = new Dictionary<TestName, JobGroup> { [new TestName("test-feature")] = testGroup };
        var jobGroups = new JobGroups(byRoot, byTest);

        var analyzer = new FilterAnalyzer(config, jobGroups, new() { [new JobName("CUSTOM-test-feature")] = TimeSpan.Zero });
        var result = analyzer.Run();

        Assert.That(result.Chains, Has.Length.EqualTo(1));
        Assert.That(result.Errors, Is.Empty);

        var chainFilter = result.GetChainFilters(result.Chains[0]);
        Assert.That(chainFilter.TestsByFilter, Has.Count.EqualTo(1));
        Assert.That(chainFilter.TestsByFilter.ContainsKey("test"), Is.True);
        var jobs = chainFilter.TestsByFilter["test"].Jobs.ToList();
        Assert.That(jobs, Has.Count.EqualTo(1));
        Assert.That(jobs[0].Value, Is.EqualTo("CUSTOM-test-feature"));
    }

    [Test]
    public void Run_TestFilterInChainGroupMatchesWithoutRootFilter_AddsError()
    {
        var testFilter = new TestFilter("test", @"test-(?<chain>\w+)", "chains");
        var config = JenkinsConfig.New("http://localhost", chainTestGroup: "chains", testFilters: [testFilter]);

        var testGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-test-feature") },
            new("CUSTOM-test-feature")
        );
        var byTest = new Dictionary<TestName, JobGroup> { [new TestName("test-feature")] = testGroup };
        var jobGroups = new JobGroups([], byTest);

        var analyzer = new FilterAnalyzer(config, jobGroups);
        var result = analyzer.Run();

        Assert.That(result.Chains, Is.Empty);
        Assert.That(result.Errors, Has.Length.EqualTo(1));
        Assert.That(result.Errors[0], Is.EqualTo("TestFilter 'test' matches chain 'feature' which has no matching RootFilter"));
    }

    [Test]
    public void Run_TestFilterInChainGroupDoesNotMatch_AddsError()
    {
        var testFilter = new TestFilter("test", @"test-feature", "chains");
        var config = JenkinsConfig.New("http://localhost", chainTestGroup: "chains", testFilters: [testFilter]);

        var testGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-integration") },
            new("CUSTOM-integration")
        );
        var byTest = new Dictionary<TestName, JobGroup> { [new TestName("integration")] = testGroup };
        var jobGroups = new JobGroups([], byTest);

        var analyzer = new FilterAnalyzer(config, jobGroups);
        var result = analyzer.Run();

        Assert.That(result.Chains, Is.Empty);
        Assert.That(result.Errors, Has.Length.EqualTo(1));
        Assert.That(result.Errors[0], Is.EqualTo("TestFilter 'test' does not match any test job"));
    }

    [Test]
    public void Run_TestFilterInRegularGroupMatches_AddsToTestFilters()
    {
        var testFilter = new TestFilter("integration", "integration", "tests");
        var config = JenkinsConfig.New("http://localhost", testFilters: [testFilter]);

        var testGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-integration") },
            new("CUSTOM-integration")
        );
        var byTest = new Dictionary<TestName, JobGroup> { [new TestName("integration")] = testGroup };
        var jobGroups = new JobGroups([], byTest);

        var analyzer = new FilterAnalyzer(config, jobGroups, new() { [new JobName("CUSTOM-integration")] = TimeSpan.Zero });
        var result = analyzer.Run();

        Assert.That(result.TestGroups, Has.Length.EqualTo(1));
        Assert.That(result.TestGroups[0], Is.EqualTo("tests"));
        Assert.That(result.Errors, Is.Empty);

        var testsByFilter = result.GetTestsByFilterForGroup("tests");
        Assert.That(testsByFilter, Has.Count.EqualTo(1));
        Assert.That(testsByFilter.ContainsKey("integration"), Is.True);

        var filterJobs = testsByFilter["integration"];
        Assert.That(filterJobs.Filter, Is.EqualTo(testFilter));
        var jobs = filterJobs.Jobs.ToList();
        Assert.That(jobs, Has.Count.EqualTo(1));
        Assert.That(jobs[0].Value, Is.EqualTo("CUSTOM-integration"));
    }

    [Test]
    public void Run_TestFilterInRegularGroupMatchesMultipleJobs_AddsAllJobs()
    {
        var testFilter = new TestFilter("test", @"test-\w+", "tests");
        var config = JenkinsConfig.New("http://localhost", testFilters: [testFilter]);

        var testGroup1 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-test-unit") },
            new("CUSTOM-test-unit")
        );
        var testGroup2 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-test-integration") },
            new("CUSTOM-test-integration")
        );
        var byTest = new Dictionary<TestName, JobGroup>
        {
            [new TestName("test-unit")] = testGroup1,
            [new TestName("test-integration")] = testGroup2
        };
        var jobGroups = new JobGroups([], byTest);

        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("CUSTOM-test-unit")] = TimeSpan.FromMinutes(5),
            [new JobName("CUSTOM-test-integration")] = TimeSpan.FromMinutes(15)
        };
        var analyzer = new FilterAnalyzer(config, jobGroups, durationByJob);
        var result = analyzer.Run();

        Assert.That(result.TestGroups, Has.Length.EqualTo(1));
        Assert.That(result.Errors, Is.Empty);

        var testsByFilter = result.GetTestsByFilterForGroup("tests");
        var filterJobs = testsByFilter["test"];
        var jobs = filterJobs.Jobs.ToList();
        Assert.That(jobs, Has.Count.EqualTo(2));
        Assert.That(jobs.Select(j => j.Value), Is.EquivalentTo(new[] { "CUSTOM-test-unit", "CUSTOM-test-integration" }));
    }

    [Test]
    public void Run_TestFilterInRegularGroupDoesNotMatch_AddsError()
    {
        var testFilter = new TestFilter("integration", "integration", "tests");
        var config = JenkinsConfig.New("http://localhost", testFilters: [testFilter]);

        var testGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-unit") },
            new("CUSTOM-unit")
        );
        var byTest = new Dictionary<TestName, JobGroup> { [new TestName("unit")] = testGroup };
        var jobGroups = new JobGroups([], byTest);

        var analyzer = new FilterAnalyzer(config, jobGroups, new() { [new JobName("CUSTOM-unit")] = TimeSpan.FromMinutes(10) });
        var result = analyzer.Run();

        Assert.That(result.TestGroups, Is.Empty);
        Assert.That(result.Errors, Has.Length.EqualTo(1));
        Assert.That(result.Errors[0], Is.EqualTo("TestFilter 'integration' does not match any test job"));
    }

    [Test]
    public void Run_MultipleTestFiltersInSameGroup_GroupedCorrectly()
    {
        var testFilter1 = new TestFilter("unit", "unit", "tests");
        var testFilter2 = new TestFilter("integration", "integration", "tests");
        var config = JenkinsConfig.New("http://localhost", testFilters: [testFilter1, testFilter2]);

        var testGroup1 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-unit") },
            new("CUSTOM-unit")
        );
        var testGroup2 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-integration") },
            new("CUSTOM-integration")
        );
        var byTest = new Dictionary<TestName, JobGroup>
        {
            [new TestName("unit")] = testGroup1,
            [new TestName("integration")] = testGroup2
        };
        var jobGroups = new JobGroups([], byTest);

        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("CUSTOM-unit")] = TimeSpan.FromMinutes(5),
            [new JobName("CUSTOM-integration")] = TimeSpan.FromMinutes(15)
        };
        var analyzer = new FilterAnalyzer(config, jobGroups, durationByJob);
        var result = analyzer.Run();

        Assert.That(result.TestGroups, Has.Length.EqualTo(1));
        Assert.That(result.Errors, Is.Empty);

        var testsByFilter = result.GetTestsByFilterForGroup("tests");
        Assert.That(testsByFilter, Has.Count.EqualTo(2));
        Assert.That(testsByFilter.ContainsKey("unit"), Is.True);
        Assert.That(testsByFilter.ContainsKey("integration"), Is.True);
    }

    [Test]
    public void Run_TestFiltersInDifferentGroups_GroupedSeparately()
    {
        var testFilter1 = new TestFilter("unit", "unit", "fast");
        var testFilter2 = new TestFilter("integration", "integration", "slow");
        var config = JenkinsConfig.New("http://localhost", testFilters: [testFilter1, testFilter2]);

        var testGroup1 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-unit") },
            new("CUSTOM-unit")
        );
        var testGroup2 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-integration") },
            new("CUSTOM-integration")
        );
        var byTest = new Dictionary<TestName, JobGroup>
        {
            [new TestName("unit")] = testGroup1,
            [new TestName("integration")] = testGroup2
        };
        var jobGroups = new JobGroups([], byTest);

        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("CUSTOM-unit")] = TimeSpan.FromMinutes(5),
            [new JobName("CUSTOM-integration")] = TimeSpan.FromMinutes(15)
        };
        var analyzer = new FilterAnalyzer(config, jobGroups, durationByJob);
        var result = analyzer.Run();

        Assert.That(result.TestGroups, Has.Length.EqualTo(2));
        Assert.That(result.TestGroups, Is.EquivalentTo(["fast", "slow"]));
        Assert.That(result.Errors, Is.Empty);

        var fastTests = result.GetTestsByFilterForGroup("fast");
        Assert.That(fastTests, Has.Count.EqualTo(1));
        Assert.That(fastTests.ContainsKey("unit"), Is.True);

        var slowTests = result.GetTestsByFilterForGroup("slow");
        Assert.That(slowTests, Has.Count.EqualTo(1));
        Assert.That(slowTests.ContainsKey("integration"), Is.True);
    }

    [Test]
    public void Run_ComplexScenario_HandlesCorrectly()
    {
        var rootFilter = new RootFilter("build", @"build-(?<chain>\w+)");
        var testFilter1 = new TestFilter("test", @"test-(?<chain>\w+)", "chains");
        var testFilter2 = new TestFilter("unit", "unit", "tests");
        var testFilter3 = new TestFilter("unmatched", "unmatched", "tests");
        var config = JenkinsConfig.New(
            "http://localhost",
            rootFilters: [rootFilter],
            chainTestGroup: "chains",
            testFilters: [testFilter1, testFilter2, testFilter3]
        );

        var rootGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-build-feature") },
            new("CUSTOM-build-feature")
        );
        var testGroup1 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-test-feature") },
            new("CUSTOM-test-feature")
        );
        var testGroup2 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-unit") },
            new("CUSTOM-unit")
        );
        var byRoot = new Dictionary<RootName, JobGroup> { [new RootName("build-feature")] = rootGroup };
        var byTest = new Dictionary<TestName, JobGroup>
        {
            [new TestName("test-feature")] = testGroup1,
            [new TestName("unit")] = testGroup2
        };
        var jobGroups = new JobGroups(byRoot, byTest);

        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("CUSTOM-test-feature")] = TimeSpan.FromMinutes(10),
            [new JobName("CUSTOM-unit")] = TimeSpan.FromMinutes(5)
        };
        var analyzer = new FilterAnalyzer(config, jobGroups, durationByJob);
        var result = analyzer.Run();

        Assert.That(result.Chains, Has.Length.EqualTo(1));
        Assert.That(result.TestGroups, Has.Length.EqualTo(1));
        Assert.That(result.TestGroups[0], Is.EqualTo("tests"));
        Assert.That(result.Errors, Has.Length.EqualTo(1));
        Assert.That(result.Errors[0], Is.EqualTo("TestFilter 'unmatched' does not match any test job"));

        var chainFilter = result.GetChainFilters(result.Chains[0]);
        Assert.That(chainFilter.TestsByFilter, Has.Count.EqualTo(1));
        Assert.That(chainFilter.TestsByFilter.ContainsKey("test"), Is.True);
        var jobs = chainFilter.TestsByFilter["test"].Jobs.ToList();
        Assert.That(jobs, Has.Count.EqualTo(1));
        Assert.That(jobs[0].Value, Is.EqualTo("CUSTOM-test-feature"));

        var testsByFilter = result.GetTestsByFilterForGroup("tests");
        Assert.That(testsByFilter, Has.Count.EqualTo(1));
        Assert.That(testsByFilter.ContainsKey("unit"), Is.True);
    }

    [Test]
    public void LogFilters_ComplexScenario_LogsCorrectly()
    {
        var rootFilter = new RootFilter("build", @"build-(?<chain>\w+)");
        var testFilter1 = new TestFilter("test", @"test-(?<chain>\w+)", "chains");
        var testFilter2 = new TestFilter("unit", "unit", "tests");
        var testFilter3 = new TestFilter("unmatched", "unmatched", "tests");
        var config = JenkinsConfig.New(
            "http://localhost",
            rootFilters: [rootFilter],
            chainTestGroup: "chains",
            testFilters: [testFilter1, testFilter2, testFilter3]
        );

        var rootGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-build-feature") },
            new("CUSTOM-build-feature")
        );
        var testGroup1 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-test-feature") },
            new("CUSTOM-test-feature")
        );
        var testGroup2 = new JobGroup(
            new Dictionary<BranchName, JobName> { [new BranchName("main")] = new("MAIN-unit") },
            new("CUSTOM-unit")
        );
        var byRoot = new Dictionary<RootName, JobGroup> { [new RootName("build-feature")] = rootGroup };
        var byTest = new Dictionary<TestName, JobGroup>
        {
            [new TestName("test-feature")] = testGroup1,
            [new TestName("unit")] = testGroup2
        };
        var jobGroups = new JobGroups(byRoot, byTest);

        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("CUSTOM-test-feature")] = TimeSpan.FromMinutes(10),
            [new JobName("CUSTOM-unit")] = TimeSpan.FromMinutes(5)
        };

        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Destructure.With<JobNameDestructuringPolicy>()
                .Destructure.With<BuildReferenceDestructuringPolicy>()
                .Destructure.With<BuildResultInfoDestructuringPolicy>()
                .Enrich.With<TimeSpanEnricher>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            FilterAnalyzer.LogFilters(config, jobGroups, durationByJob, logger);

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var logs = events.Select(e => $"{e.Level}: {e.RenderMessage().RemoveAnsiCodes()}").ToArray();
            Assert.That(logs, Is.EqualTo([
                @"Information: Chain: ""feature""",
                @"Information:   Root: '""build""': ""CUSTOM-build-feature""",
                @"Information:     '""test""' (""10 min"")",
                @"Information:       ""CUSTOM-test-feature""",
                @"Information: Test Group: ""tests""",
                @"Information:   '""unit""' (""5 min"")",
                @"Information:     ""CUSTOM-unit""",
                @"Error: Error:",
                @"Error:   ""TestFilter 'unmatched' does not match any test job""",
            ]));
        }
    }
}