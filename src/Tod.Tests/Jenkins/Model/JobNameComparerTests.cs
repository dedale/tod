using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class JobNameComparerTests
{
    [Test]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var comparer = new JobNameComparer([]);
        var jobName = new JobName("test-job");

        var result = comparer.Equals(jobName, jobName);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_BothNull_ReturnsTrue()
    {
        var comparer = new JobNameComparer([]);

        Assert.That(comparer.Equals(null, null), Is.True);
    }

    [Test]
    public void Equals_OneNull_ReturnsFalse()
    {
        var comparer = new JobNameComparer([]);
        var jobName = new JobName("test-job");

        Assert.That(comparer.Equals(jobName, null), Is.False);
        Assert.That(comparer.Equals(null, jobName), Is.False);
    }

    [Test]
    public void Equals_SameValue_ReturnsTrue()
    {
        var comparer = new JobNameComparer([]);
        var jobName1 = new JobName("test-job");
        var jobName2 = new JobName("test-job");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.True);
    }

    [Test]
    public void Equals_DifferentValues_ReturnsFalse()
    {
        var comparer = new JobNameComparer([]);
        var jobName1 = new JobName("test-job-1");
        var jobName2 = new JobName("test-job-2");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.False);
    }

    [Test]
    public void Equals_WithMapping_MatchesMappedNames()
    {
        var mapping = new JobMapping("old-name", "new-name");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("old-name");
        var jobName2 = new JobName("new-name");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.True);
    }

    [Test]
    public void Equals_WithMapping_ReplacesOldWithNew()
    {
        var mapping = new JobMapping("build", "compile");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("test-build-job");
        var jobName2 = new JobName("test-compile-job");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.True);
    }

    [Test]
    public void Equals_WithMultipleMappings_AppliesAllMappings()
    {
        var mapping1 = new JobMapping("old", "new");
        var mapping2 = new JobMapping("build", "compile");
        var comparer = new JobNameComparer([mapping1, mapping2]);
        var jobName1 = new JobName("old-build");
        var jobName2 = new JobName("new-compile");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.True);
    }

    [Test]
    public void Equals_WithMapping_PartialMatch()
    {
        var mapping = new JobMapping("MAIN", "PRIMARY");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("MAIN-build-test");
        var jobName2 = new JobName("PRIMARY-build-test");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.True);
    }

    [Test]
    public void Equals_WithMapping_NoMatch_ReturnsFalse()
    {
        var mapping = new JobMapping("old", "new");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("test-job-1");
        var jobName2 = new JobName("test-job-2");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.False);
    }

    [Test]
    public void Equals_EmptyMappings_ComparesDirectly()
    {
        var comparer = new JobNameComparer([]);
        var jobName1 = new JobName("test-job");
        var jobName2 = new JobName("test-job");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.True);
    }

    [Test]
    public void GetHashCode_SameValue_ReturnsSameHashCode()
    {
        var comparer = new JobNameComparer([]);
        var jobName1 = new JobName("test-job");
        var jobName2 = new JobName("test-job");

        var hash1 = comparer.GetHashCode(jobName1);
        var hash2 = comparer.GetHashCode(jobName2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithMapping_MappedNamesHaveSameHashCode()
    {
        var mapping = new JobMapping("old-name", "new-name");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("old-name");
        var jobName2 = new JobName("new-name");

        var hash1 = comparer.GetHashCode(jobName1);
        var hash2 = comparer.GetHashCode(jobName2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithMapping_AppliesMappingBeforeHashing()
    {
        var mapping = new JobMapping("build", "compile");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("test-build");
        var jobName2 = new JobName("test-compile");

        var hash1 = comparer.GetHashCode(jobName1);
        var hash2 = comparer.GetHashCode(jobName2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_DifferentValues_ReturnsDifferentHashCodes()
    {
        var comparer = new JobNameComparer([]);
        var jobName1 = new JobName("test-job-1");
        var jobName2 = new JobName("test-job-2");

        var hash1 = comparer.GetHashCode(jobName1);
        var hash2 = comparer.GetHashCode(jobName2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_ConsistentWithEquals()
    {
        var mapping = new JobMapping("old", "new");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("old-job");
        var jobName2 = new JobName("new-job");

        var areEqual = comparer.Equals(jobName1, jobName2);
        var hash1 = comparer.GetHashCode(jobName1);
        var hash2 = comparer.GetHashCode(jobName2);

        Assert.That(areEqual, Is.True);
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithMultipleMappings_AppliesAllMappings()
    {
        var mapping1 = new JobMapping("MAIN", "PRIMARY");
        var mapping2 = new JobMapping("build", "compile");
        var comparer = new JobNameComparer([mapping1, mapping2]);
        var jobName1 = new JobName("MAIN-build");
        var jobName2 = new JobName("PRIMARY-compile");

        var hash1 = comparer.GetHashCode(jobName1);
        var hash2 = comparer.GetHashCode(jobName2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Comparer_CanBeUsedInHashSet()
    {
        var mapping = new JobMapping("old", "new");
        var comparer = new JobNameComparer([mapping]);
        var hashSet = new HashSet<JobName>(comparer);

        var jobName1 = new JobName("old-job");
        var jobName2 = new JobName("new-job");

        hashSet.Add(jobName1);

        Assert.That(hashSet.Contains(jobName2), Is.True);
        Assert.That(hashSet.Count, Is.EqualTo(1));
    }

    [Test]
    public void Comparer_CanBeUsedInDictionary()
    {
        var mapping = new JobMapping("build", "compile");
        var comparer = new JobNameComparer([mapping]);
        var dictionary = new Dictionary<JobName, string>(comparer);

        var jobName1 = new JobName("test-build");
        var jobName2 = new JobName("test-compile");

        dictionary[jobName1] = "value";

        Assert.That(dictionary.ContainsKey(jobName2), Is.True);
        Assert.That(dictionary[jobName2], Is.EqualTo("value"));
    }

    [Test]
    public void Equals_CaseSensitive()
    {
        var comparer = new JobNameComparer([]);
        var jobName1 = new JobName("Test-Job");
        var jobName2 = new JobName("test-job");

        var result = comparer.Equals(jobName1, jobName2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_WithMapping_CaseSensitiveReplacement()
    {
        var mapping = new JobMapping("Build", "Compile");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("Test-build");
        var jobName2 = new JobName("Test-Compile");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.False);
    }

    [Test]
    public void Equals_WithMapping_ReplacesAllOccurrences()
    {
        var mapping = new JobMapping("test", "prod");
        var comparer = new JobNameComparer([mapping]);
        var jobName1 = new JobName("test-build-test");
        var jobName2 = new JobName("prod-build-prod");

        Assert.That(comparer.Equals(jobName1, jobName2), Is.True);
    }
}
