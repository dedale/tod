using NUnit.Framework;
using System.Diagnostics;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class FilterManagerTests
{
    private static readonly BranchName _mainBranch = new("main");
    private static readonly BranchName _prodBranch = new("prod");

    private static FilterManager GetFilterManager()
    {
        var filters = new TestFilter[]
        {
            new("integration", "integration", "tests"),
            new("empty1", "unmatched1", "tests"),
            new("empty2", "unmatched2", "tests"),
            new("prod", "production-tests", "tests"),
        };
        var _config = new JenkinsConfig("http://localhost:8080", [], [], [], filters);

        var referenceJobByBranch = new Dictionary<BranchName, JobName> { [_mainBranch] = new("MAIN-build") };
        var rootGroup = new JobGroup(referenceJobByBranch, new("CUSTOM-build"));
        var byRoot = new Dictionary<RootName, JobGroup>
        {
            [new RootName("build")] = rootGroup,
        };

        var testGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [_mainBranch] = new("MAIN-test") },
            new("CUSTOM-test")
        );
        var prodTestGroup = new JobGroup(
            new Dictionary<BranchName, JobName> { [_prodBranch] = new("PROD-production-tests") },
            new("CUSTOM-production-tests")
        );
        var byTest = new Dictionary<TestName, JobGroup>
        {
            [new TestName("integration")] = testGroup,
            [new TestName("production-tests")] = prodTestGroup,
        };

        var _jobGroups = new JobGroups(byRoot, byTest);
        return new FilterManager(_config, _jobGroups);
    }

    [Test]
    public void GetTestBuildDiffs_UnknownFilters_Throws()
    {
        var manager = GetFilterManager();

        Assert.That(() => manager.GetTestBuildDiffs(["unknown1", "unknown2"], _mainBranch),
            Throws.InvalidOperationException.And.Message.EqualTo("Unknown test filters: 'unknown1', 'unknown2'"));
    }

    [Test]
    public void GetTestBuildDiffs_UnknownFilter_Throws()
    {
        var manager = GetFilterManager();

        Assert.That(() => manager.GetTestBuildDiffs(["unknown1"], _mainBranch),
            Throws.InvalidOperationException.And.Message.EqualTo("Unknown test filter: 'unknown1'"));
    }

    [Test]
    public void GetTestBuildDiffs_FilterWithoutGroup_Throws()
    {
        var manager = GetFilterManager();

        Assert.That(() => manager.GetTestBuildDiffs(["empty1"], _mainBranch),
            Throws.InvalidOperationException.And.Message.EqualTo("No test groups for the request filter: 'empty1'"));
    }

    [Test]
    public void GetTestBuildDiffs_FiltersWithoutGroup_Throws()
    {
        var manager = GetFilterManager();

        Assert.That(() => manager.GetTestBuildDiffs(["empty1", "empty2"], _mainBranch),
            Throws.InvalidOperationException.And.Message.EqualTo("No test groups for the request filters: 'empty1', 'empty2'"));
    }

    [Test]
    public void GetTestBuildDiffs_NoRefJobInTestGroup_ThrowsInvalidOperationException()
    {
        var manager = GetFilterManager();

        Assert.That(() => manager.GetTestBuildDiffs(["prod"], _mainBranch),
            Throws.InvalidOperationException.With.Message.EqualTo("No reference job for 'main' branch in test group"));
    }

    [Test]
    public void GetRootDiffs_WithMatchingRootName_ReturnsRootDiff()
    {
        // Arrange
        var manager = GetFilterManager();
        var rootNames = new[] { new RootName("build") };

        // Act
        var rootDiffs = manager.GetRootDiffs(rootNames, _mainBranch);

        // Assert
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].ReferenceJob.Value, Is.EqualTo("MAIN-build"));
        Assert.That(rootDiffs[0].OnDemandJob.Value, Is.EqualTo("CUSTOM-build"));
    }

    [Test]
    public void GetRootDiffs_WithNonMatchingRootName_ReturnsEmpty()
    {
        // Arrange
        var manager = GetFilterManager();
        var rootNames = new[] { new RootName("nonexistent") };

        // Act
        var rootDiffs = manager.GetRootDiffs(rootNames, _mainBranch);

        // Assert
        Assert.That(rootDiffs, Is.Empty);
    }

    [Test]
    public void GetRootDiffs_WithEmptyRootNames_ReturnsEmpty()
    {
        // Arrange
        var manager = GetFilterManager();
        var rootNames = Array.Empty<RootName>();

        // Act
        var rootDiffs = manager.GetRootDiffs(rootNames, _mainBranch);

        // Assert
        Assert.That(rootDiffs, Is.Empty);
    }

    [Test]
    public void GetRootDiffs_WithInvalidBranch_ThrowsInvalidOperationException()
    {
        // Arrange
        var manager = GetFilterManager();
        var rootNames = new[] { new RootName("build") };
        var invalidBranch = new BranchName("nonexistent-branch");

        // Act & Assert
        Assert.That(() => manager.GetRootDiffs(rootNames, invalidBranch),
            Throws.InvalidOperationException.With.Message.EqualTo("No reference job for 'nonexistent-branch' branch in test group"));
    }

    [Test]
    public void GetRootDiffs_WithMultipleRootNames_ReturnsMultipleRootDiffs()
    {
        // Arrange
        var referenceJobByBranch = new Dictionary<BranchName, JobName> { [_mainBranch] = new("MAIN-build") };
        var rootGroup1 = new JobGroup(referenceJobByBranch, new("CUSTOM-build"));
        var rootGroup2 = new JobGroup(new Dictionary<BranchName, JobName> { [_mainBranch] = new("MAIN-deploy") }, new("CUSTOM-deploy"));
        var rootGroup3 = new JobGroup(new Dictionary<BranchName, JobName> { [_mainBranch] = new("MAIN-package") }, new("CUSTOM-package"));
        
        var byRoot = new Dictionary<RootName, JobGroup>
        {
            [new RootName("build")] = rootGroup1,
            [new RootName("deploy")] = rootGroup2,
            [new RootName("package")] = rootGroup3,
        };
        var byTest = new Dictionary<TestName, JobGroup>();
        var jobGroups = new JobGroups(byRoot, byTest);
        var config = new JenkinsConfig("http://localhost:8080", [], [], [], []);
        var manager = new FilterManager(config, jobGroups);

        var rootNames = new[] { new RootName("build"), new RootName("deploy"), new RootName("package") };

        // Act
        var rootDiffs = manager.GetRootDiffs(rootNames, _mainBranch);

        // Assert
        Assert.That(rootDiffs, Has.Length.EqualTo(3));
        Assert.That(rootDiffs.Select(d => d.ReferenceJob.Value), Is.EquivalentTo(new[] { "MAIN-build", "MAIN-deploy", "MAIN-package" }));
        Assert.That(rootDiffs.Select(d => d.OnDemandJob.Value), Is.EquivalentTo(new[] { "CUSTOM-build", "CUSTOM-deploy", "CUSTOM-package" }));
    }

    [Test]
    public void GetRootDiffs_WithPartialMatch_ReturnsOnlyMatchingRootDiffs()
    {
        // Arrange
        var referenceJobByBranch = new Dictionary<BranchName, JobName> { [_mainBranch] = new("MAIN-build") };
        var rootGroup1 = new JobGroup(referenceJobByBranch, new("CUSTOM-build"));
        var rootGroup2 = new JobGroup(new Dictionary<BranchName, JobName> { [_mainBranch] = new("MAIN-deploy") }, new("CUSTOM-deploy"));
        
        var byRoot = new Dictionary<RootName, JobGroup>
        {
            [new RootName("build")] = rootGroup1,
            [new RootName("deploy")] = rootGroup2,
        };
        var byTest = new Dictionary<TestName, JobGroup>();
        var jobGroups = new JobGroups(byRoot, byTest);
        var config = new JenkinsConfig("http://localhost:8080", [], [], [], []);
        var manager = new FilterManager(config, jobGroups);

        var rootNames = new[] { new RootName("build"), new RootName("nonexistent") };

        // Act
        var rootDiffs = manager.GetRootDiffs(rootNames, _mainBranch);

        // Assert
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].ReferenceJob.Value, Is.EqualTo("MAIN-build"));
        Assert.That(rootDiffs[0].OnDemandJob.Value, Is.EqualTo("CUSTOM-build"));
    }

    [Test]
    public void GetRootDiffs_WithMultipleBranches_ReturnsCorrectJobs()
    {
        // Arrange
        var rootGroup = new JobGroup(
            new Dictionary<BranchName, JobName> 
            { 
                [_mainBranch] = new("MAIN-build"),
                [_prodBranch] = new("PROD-build")
            }, 
            new("CUSTOM-build"));
        
        var byRoot = new Dictionary<RootName, JobGroup>
        {
            [new RootName("build")] = rootGroup,
        };
        var byTest = new Dictionary<TestName, JobGroup>();
        var jobGroups = new JobGroups(byRoot, byTest);
        var config = new JenkinsConfig("http://localhost:8080", [], [], [], []);
        var manager = new FilterManager(config, jobGroups);

        var rootNames = new[] { new RootName("build") };

        // Act - Test with main branch
        var mainRootDiffs = manager.GetRootDiffs(rootNames, _mainBranch);

        // Assert
        Assert.That(mainRootDiffs, Has.Length.EqualTo(1));
        Assert.That(mainRootDiffs[0].ReferenceJob.Value, Is.EqualTo("MAIN-build"));
        Assert.That(mainRootDiffs[0].OnDemandJob.Value, Is.EqualTo("CUSTOM-build"));

        // Act - Test with prod branch
        var prodRootDiffs = manager.GetRootDiffs(rootNames, _prodBranch);

        // Assert
        Assert.That(prodRootDiffs, Has.Length.EqualTo(1));
        Assert.That(prodRootDiffs[0].ReferenceJob.Value, Is.EqualTo("PROD-build"));
        Assert.That(prodRootDiffs[0].OnDemandJob.Value, Is.EqualTo("CUSTOM-build"));
    }

    [Test]
    public void GetRootDiffs_WithDuplicateRootNames_ReturnsSingleRootDiff()
    {
        // Arrange
        var manager = GetFilterManager();
        var rootNames = new[] { new RootName("build"), new RootName("build") };

        // Act
        var rootDiffs = manager.GetRootDiffs(rootNames, _mainBranch);

        // Assert
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].ReferenceJob.Value, Is.EqualTo("MAIN-build"));
        Assert.That(rootDiffs[0].OnDemandJob.Value, Is.EqualTo("CUSTOM-build"));
    }

    private static readonly FilterManager s_complexManager = GetComplexManager();

    private static FilterManager GetComplexManager()
    {
        var jobs = new JobName[] {
            new("MAIN-FrontEnd-build"),
            new("MAIN-FrontEnd-dev-tests-net6"),
            new("MAIN-FrontEnd-dev-tests-net8"),
            new("MAIN-FrontEnd-integration-tests-net6"),
            new("MAIN-FrontEnd-integration-tests-net8"),
            new("MAIN-Core-build"),
            new("MAIN-Core-dev-tests-net6"),
            new("MAIN-Core-dev-tests-net8"),
            new("MAIN-Core-integration-tests-net6"),
            new("MAIN-Core-integration-tests-net8"),
            new("MAIN-BackEnd-build"),
            new("MAIN-BackEnd-dev-tests-net6"),
            new("MAIN-BackEnd-dev-tests-net8"),
            new("MAIN-BackEnd-integration-tests-net6"),
            new("MAIN-BackEnd-integration-tests-net8"),
            new("PROD-FrontEnd-build"),
            new("PROD-FrontEnd-dev-tests-net6"),
            new("PROD-FrontEnd-dev-tests-net8"),
            new("PROD-FrontEnd-integration-tests-net6"),
            new("PROD-FrontEnd-integration-tests-net8"),
            new("PROD-Core-build"),
            new("PROD-Core-dev-tests-net6"),
            new("PROD-Core-dev-tests-net8"),
            new("PROD-Core-integration-tests-net6"),
            new("PROD-Core-integration-tests-net8"),
            new("PROD-BackEnd-build"),
            new("PROD-BackEnd-dev-tests-net6"),
            new("PROD-BackEnd-dev-tests-net8"),
            new("PROD-BackEnd-integration-tests-net6"),
            new("PROD-BackEnd-integration-tests-net8"),
            new("CUSTOM-FrontEnd-build"),
            new("CUSTOM-FrontEnd-dev-tests-net6"),
            new("CUSTOM-FrontEnd-dev-tests-net8"),
            new("CUSTOM-FrontEnd-integration-tests-net6"),
            new("CUSTOM-FrontEnd-integration-tests-net8"),
            new("CUSTOM-Core-build"),
            new("CUSTOM-Core-dev-tests-net6"),
            new("CUSTOM-Core-dev-tests-net8"),
            new("CUSTOM-Core-integration-tests-net6"),
            new("CUSTOM-Core-integration-tests-net8"),
            new("CUSTOM-BackEnd-buils"),
            new("CUSTOM-BackEnd-dev-tests-net6"),
            new("CUSTOM-BackEnd-dev-tests-net8"),
            new("CUSTOM-BackEnd-integration-tests-net6"),
            new("CUSTOM-BackEnd-integration-tests-net8"),
        };
        var referenceJobs = new ReferenceJobConfig[]
        {
            new("MAIN-(?<root>.*build)", _mainBranch, true),
            new("MAIN-(?<test>.*-tests-.*)", _mainBranch, false),
            new("PROD-(?<root>.*build)", _prodBranch, true),
            new("PROD-(?<test>.*-tests-.*)", _prodBranch, false),
        };
        var onDemandJobs = new OnDemandJobConfig[]
        {
            new("CUSTOM-(?<root>.*build)", true),
            new("CUSTOM-(?<test>.*-tests-.*)", false),
        };
        var filters = new TestFilter[]
        {
            new("FrontEnd", "FrontEnd", "team"),
            new("Core", "Core", "team"),
            new("BackEnd", "BackEnd", "team"),

            new("dev", "dev-tests", "tests"),
            new("integration", "integration-tests", "tests"),

            new("net6", "net6", "framework"),
            new("net8", "net8", "framework"),
        };
        var config = new JenkinsConfig("http://localhost:8080", [], referenceJobs, onDemandJobs, filters);
        var jobGroups = JobManager.TryLoad(config, jobs);
        Debug.Assert(jobGroups is not null);
        return new FilterManager(config, jobGroups);
    }

    [Test]
    public void GetRootDiffs_ComplexCase_WithBuildRoot_ReturnsCorrectDiff()
    {
        // Arrange
        var rootNames = new[] { new RootName("Core-build") };

        // Act
        var rootDiffs = s_complexManager.GetRootDiffs(rootNames, _mainBranch);

        // Assert
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].ReferenceJob.Value, Is.EqualTo("MAIN-Core-build"));
        Assert.That(rootDiffs[0].OnDemandJob.Value, Is.EqualTo("CUSTOM-Core-build"));
    }

    [Test]
    public void GetRootDiffs_ComplexCase_WithProdBranch_ReturnsCorrectDiff()
    {
        // Arrange
        var rootNames = new[] { new RootName("Core-build") };

        // Act
        var rootDiffs = s_complexManager.GetRootDiffs(rootNames, _prodBranch);

        // Assert
        Assert.That(rootDiffs, Has.Length.EqualTo(1));
        Assert.That(rootDiffs[0].ReferenceJob.Value, Is.EqualTo("PROD-Core-build"));
        Assert.That(rootDiffs[0].OnDemandJob.Value, Is.EqualTo("CUSTOM-Core-build"));
    }

    [Test]
    public void ComplexCase_FrontEnd()
    {
        var testBuildDiffs = s_complexManager.GetTestBuildDiffs(["FrontEnd"], _mainBranch);
        Assert.That(testBuildDiffs, Has.Length.EqualTo(4));
        var jobNames = testBuildDiffs.Select(d => d.OnDemandBuild.JobName);
        Assert.That(jobNames, Is.EquivalentTo(new JobName[]
        {
            new("CUSTOM-FrontEnd-dev-tests-net6"),
            new("CUSTOM-FrontEnd-dev-tests-net8"),
            new("CUSTOM-FrontEnd-integration-tests-net6"),
            new("CUSTOM-FrontEnd-integration-tests-net8"),
        }));
    }

    [Test]
    public void ComplexCase_Core_net6()
    {
        var testBuildDiffs = s_complexManager.GetTestBuildDiffs(["Core", "net6"], _mainBranch);
        Assert.That(testBuildDiffs, Has.Length.EqualTo(2));
        var jobNames = testBuildDiffs.Select(d => d.OnDemandBuild.JobName);
        Assert.That(jobNames, Is.EquivalentTo(new JobName[]
        {
            new("CUSTOM-Core-dev-tests-net6"),
            new("CUSTOM-Core-integration-tests-net6"),
        }));
    }

    [Test]
    public void ComplexCase_dev_net6()
    {
        var testBuildDiffs = s_complexManager.GetTestBuildDiffs(["dev", "net6"], _mainBranch);
        Assert.That(testBuildDiffs, Has.Length.EqualTo(3));
        var jobNames = testBuildDiffs.Select(d => d.OnDemandBuild.JobName);
        Assert.That(jobNames, Is.EquivalentTo(new JobName[]
        {
            new("CUSTOM-FrontEnd-dev-tests-net6"),
            new("CUSTOM-Core-dev-tests-net6"),
            new("CUSTOM-BackEnd-dev-tests-net6"),
        }));
    }
}
