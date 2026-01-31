using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class JobNameTests
{
    [Test]
    public void Constructor_ValidName_SetsValue()
    {
        const string name = "MyJob";
        var jobName = new JobName(name);
        Assert.That(jobName.Value, Is.EqualTo(name));
    }

    [TestCase("JobA", "JobB", -1)]
    [TestCase("JobB", "JobA", 1)]
    [TestCase("JobA", "JobA", 0)]
    public void CompareTo_DifferentValues_ReturnsExpectedOrder(string value1, string value2, int expectedResult)
    {
        var job1 = new JobName(value1);
        var job2 = new JobName(value2);

        Assert.That(job1.CompareTo(job2), Is.EqualTo(expectedResult));
    }

    [Test]
    public void CompareTo_WithNull_ReturnsOne()
    {
        var jobName = new JobName("MyJob");
        Assert.That(jobName.CompareTo(null), Is.EqualTo(1));
    }

    [TestCase("RootJob", "job/RootJob")]
    [TestCase("MultiBranch/Pipeline/SomeJob", "job/MultiBranch/job/Pipeline/job/SomeJob")]
    public void UrlPath_ReturnsExpectedFormat(string value, string urlPath)
    {
        var jobName = new JobName(value);
        Assert.That(jobName.UrlPath, Is.EqualTo(urlPath));
    }

    [Test]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var jobName = new JobName("test-job");

        var result = jobName.Equals(jobName);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_OneNull_ReturnsFalse()
    {
        var jobName = new JobName("test-job");

        Assert.That(jobName.Equals(null), Is.False);
    }

    [Test]
    public void Equals_SameValue_ReturnsTrue()
    {
        var jobName1 = new JobName("test-job");
        var jobName2 = new JobName("test-job");

        Assert.That(jobName1.Equals(jobName2), Is.True);
    }

    [Test]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var jobName1 = new JobName("test-job-1");
        var jobName2 = new JobName("test-job-2");

        Assert.That(jobName1.Equals(jobName2), Is.False);
    }

    [Test]
    public void Equals_WithMapping_MatchesMappedNames()
    {
        var mapping = new JobMapping("old-name", "new-name");
        var jobName1 = new JobName("old-name");
        var jobName2 = new JobName("new-name");

        Assert.That(jobName1.Equals(jobName2, [mapping]), Is.True);
    }

    [Test]
    public void Equals_WithMapping_ReplacesOldWithNew()
    {
        var mapping = new JobMapping("build", "compile");
        var jobName1 = new JobName("test-build-job");
        var jobName2 = new JobName("test-compile-job");

        Assert.That(jobName1.Equals(jobName2, [mapping]), Is.True);
    }

    [Test]
    public void Equals_WithMultipleMappings_AppliesAllMappings()
    {
        var mapping1 = new JobMapping("old", "new");
        var mapping2 = new JobMapping("build", "compile");
        var jobName1 = new JobName("old-build");
        var jobName2 = new JobName("new-compile");

        Assert.That(jobName1.Equals(jobName2, [mapping1, mapping2]), Is.True);
    }

    [Test]
    public void Equals_WithMapping_PartialMatch()
    {
        var mapping = new JobMapping("MAIN", "PRIMARY");
        var jobName1 = new JobName("MAIN-build-test");
        var jobName2 = new JobName("PRIMARY-build-test");

        Assert.That(jobName1.Equals(jobName2, [mapping]), Is.True);
    }

    [Test]
    public void Equals_WithMapping_NoMatch_ReturnsFalse()
    {
        var mapping = new JobMapping("old", "new");
        var jobName1 = new JobName("test-job-1");
        var jobName2 = new JobName("test-job-2");

        Assert.That(jobName1.Equals(jobName2, [mapping]), Is.False);
    }

    [Test]
    public void Equals_EmptyMappings_ComparesDirectly()
    {
        var jobName1 = new JobName("test-job");
        var jobName2 = new JobName("test-job");

        Assert.That(jobName1.Equals(jobName2), Is.True);
    }

    [Test]
    public void GetHashCode_SameValue_ReturnsSameHashCode()
    {
        var jobName1 = new JobName("test-job");
        var jobName2 = new JobName("test-job");

        var hash1 = jobName1.GetHashCode();
        var hash2 = jobName2.GetHashCode();

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithMapping_MappedNamesHaveSameHashCode()
    {
        var mapping = new JobMapping("old-name", "new-name");
        var jobName1 = new JobName("old-name");
        var jobName2 = new JobName("new-name");

        var hash1 = jobName1.GetHashCode([mapping]);
        var hash2 = jobName2.GetHashCode([mapping]);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithMapping_AppliesMappingBeforeHashing()
    {
        var mapping = new JobMapping("build", "compile");
        var jobName1 = new JobName("test-build");
        var jobName2 = new JobName("test-compile");

        var hash1 = jobName1.GetHashCode([mapping]);
        var hash2 = jobName2.GetHashCode([mapping]);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_DifferentValues_ReturnsDifferentHashCodes()
    {
        var jobName1 = new JobName("test-job-1");
        var jobName2 = new JobName("test-job-2");

        var hash1 = jobName1.GetHashCode([]);
        var hash2 = jobName2.GetHashCode([]);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_ConsistentWithEquals()
    {
        var mapping = new JobMapping("old", "new");
        var jobName1 = new JobName("old-job");
        var jobName2 = new JobName("new-job");

        var areEqual = jobName1.Equals(jobName2, [mapping]);
        var hash1 = jobName1.GetHashCode([mapping]);
        var hash2 = jobName2.GetHashCode([mapping]);

        Assert.That(areEqual, Is.True);
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithMultipleMappings_AppliesAllMappings()
    {
        var mapping1 = new JobMapping("MAIN", "PRIMARY");
        var mapping2 = new JobMapping("build", "compile");
        var jobName1 = new JobName("MAIN-build");
        var jobName2 = new JobName("PRIMARY-compile");

        var hash1 = jobName1.GetHashCode([mapping1, mapping2]);
        var hash2 = jobName2.GetHashCode([mapping1, mapping2]);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Equals_CaseSensitive()
    {
        var jobName1 = new JobName("Test-Job");
        var jobName2 = new JobName("test-job");

        var result = jobName1.Equals(jobName2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_WithMapping_CaseSensitiveReplacement()
    {
        var mapping = new JobMapping("Build", "Compile");
        var jobName1 = new JobName("Test-build");
        var jobName2 = new JobName("Test-Compile");

        Assert.That(jobName1.Equals(jobName2, [mapping]), Is.False);
    }

    [Test]
    public void Equals_WithMapping_ReplacesAllOccurrences()
    {
        var mapping = new JobMapping("test", "prod");
        var jobName1 = new JobName("test-build-test");
        var jobName2 = new JobName("prod-build-prod");

        Assert.That(jobName1.Equals(jobName2, [mapping]), Is.True);
    }
}
