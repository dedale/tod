using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildReferenceComparerTests
{
    [Test]
    public void Constructor_WithJobMappings_CreatesComparer()
    {
        var mappings = new[] { new JobMapping("old", "new") };
        var comparer = new BuildReferenceComparer(mappings);

        Assert.That(comparer, Is.Not.Null);
    }

    [Test]
    public void Default_IsNotNull()
    {
        var comparer = BuildReferenceComparer.Default;

        Assert.That(comparer, Is.Not.Null);
    }

    [Test]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef = new BuildReference("job1", 123);

        var result = comparer.Equals(buildRef, buildRef);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_BothNull_ReturnsTrue()
    {
        var comparer = new BuildReferenceComparer();

        var result = comparer.Equals(null, null);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_OneNull_ReturnsFalse()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef = new BuildReference("job1", 123);

        var result1 = comparer.Equals(buildRef, null);
        var result2 = comparer.Equals(null, buildRef);

        Assert.That(result1, Is.False);
        Assert.That(result2, Is.False);
    }

    [Test]
    public void Equals_SameJobAndBuildNumber_ReturnsTrue()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_DifferentJob_ReturnsFalse()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job2", 123);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_DifferentBuildNumber_ReturnsFalse()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 124);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_DifferentJobAndBuildNumber_ReturnsFalse()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job2", 124);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_WithJobMapping_MatchesMappedJobs()
    {
        var mapping = new JobMapping("old-job", "new-job");
        var comparer = new BuildReferenceComparer([mapping]);
        var buildRef1 = new BuildReference("old-job", 123);
        var buildRef2 = new BuildReference("new-job", 123);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_WithJobMapping_DifferentBuildNumbers_ReturnsFalse()
    {
        var mapping = new JobMapping("old-job", "new-job");
        var comparer = new BuildReferenceComparer([mapping]);
        var buildRef1 = new BuildReference("old-job", 123);
        var buildRef2 = new BuildReference("new-job", 124);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_WithJobMapping_PartialJobNameMatch()
    {
        var mapping = new JobMapping("MAIN", "PRIMARY");
        var comparer = new BuildReferenceComparer([mapping]);
        var buildRef1 = new BuildReference("MAIN-build", 100);
        var buildRef2 = new BuildReference("PRIMARY-build", 100);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_WithMultipleJobMappings_AppliesAllMappings()
    {
        var mapping1 = new JobMapping("old", "new");
        var mapping2 = new JobMapping("build", "compile");
        var comparer = new BuildReferenceComparer([mapping1, mapping2]);
        var buildRef1 = new BuildReference("old-build", 50);
        var buildRef2 = new BuildReference("new-compile", 50);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void GetHashCode_SameJobAndBuildNumber_ReturnsSameHashCode()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var hash1 = comparer.GetHashCode(buildRef1);
        var hash2 = comparer.GetHashCode(buildRef2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_DifferentJob_ReturnsDifferentHashCode()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job2", 123);

        var hash1 = comparer.GetHashCode(buildRef1);
        var hash2 = comparer.GetHashCode(buildRef2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_DifferentBuildNumber_ReturnsDifferentHashCode()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 124);

        var hash1 = comparer.GetHashCode(buildRef1);
        var hash2 = comparer.GetHashCode(buildRef2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithJobMapping_MappedJobsHaveSameHashCode()
    {
        var mapping = new JobMapping("old-job", "new-job");
        var comparer = new BuildReferenceComparer([mapping]);
        var buildRef1 = new BuildReference("old-job", 123);
        var buildRef2 = new BuildReference("new-job", 123);

        var hash1 = comparer.GetHashCode(buildRef1);
        var hash2 = comparer.GetHashCode(buildRef2);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_ConsistentWithEquals()
    {
        var mapping = new JobMapping("build", "compile");
        var comparer = new BuildReferenceComparer([mapping]);
        var buildRef1 = new BuildReference("test-build", 100);
        var buildRef2 = new BuildReference("test-compile", 100);

        var areEqual = comparer.Equals(buildRef1, buildRef2);
        var hash1 = comparer.GetHashCode(buildRef1);
        var hash2 = comparer.GetHashCode(buildRef2);

        Assert.That(areEqual, Is.True);
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithJobMapping_DifferentBuildNumbers_ReturnsDifferentHashCode()
    {
        var mapping = new JobMapping("old-job", "new-job");
        var comparer = new BuildReferenceComparer([mapping]);
        var buildRef1 = new BuildReference("old-job", 123);
        var buildRef2 = new BuildReference("new-job", 124);

        var hash1 = comparer.GetHashCode(buildRef1);
        var hash2 = comparer.GetHashCode(buildRef2);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Comparer_CanBeUsedInHashSet()
    {
        var mapping = new JobMapping("old", "new");
        var comparer = new BuildReferenceComparer([mapping]);
        var hashSet = new HashSet<BuildReference>(comparer);

        var buildRef1 = new BuildReference("old-job", 100);
        var buildRef2 = new BuildReference("new-job", 100);

        hashSet.Add(buildRef1);

        Assert.That(hashSet.Contains(buildRef2), Is.True);
        Assert.That(hashSet.Count, Is.EqualTo(1));
    }

    [Test]
    public void Comparer_CanBeUsedInDictionary()
    {
        var mapping = new JobMapping("build", "compile");
        var comparer = new BuildReferenceComparer([mapping]);
        var dictionary = new Dictionary<BuildReference, string>(comparer);

        var buildRef1 = new BuildReference("test-build", 50);
        var buildRef2 = new BuildReference("test-compile", 50);

        dictionary[buildRef1] = "value";

        Assert.That(dictionary.ContainsKey(buildRef2), Is.True);
        Assert.That(dictionary[buildRef2], Is.EqualTo("value"));
    }

    [Test]
    public void Comparer_InHashSet_DifferentBuildNumbers_AreSeparate()
    {
        var comparer = new BuildReferenceComparer();
        var hashSet = new HashSet<BuildReference>(comparer);

        var buildRef1 = new BuildReference("job1", 100);
        var buildRef2 = new BuildReference("job1", 101);

        hashSet.Add(buildRef1);
        hashSet.Add(buildRef2);

        Assert.That(hashSet.Count, Is.EqualTo(2));
    }

    [Test]
    public void Default_CanBeUsedForComparison()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var result = BuildReferenceComparer.Default.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Default_NoJobMappings_ComparesDirectly()
    {
        var buildRef1 = new BuildReference("old-job", 100);
        var buildRef2 = new BuildReference("new-job", 100);

        var result = BuildReferenceComparer.Default.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_CaseSensitive()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef1 = new BuildReference("Job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetHashCode_MultipleCalls_ReturnsSameValue()
    {
        var comparer = new BuildReferenceComparer();
        var buildRef = new BuildReference("job1", 123);

        var hash1 = comparer.GetHashCode(buildRef);
        var hash2 = comparer.GetHashCode(buildRef);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Equals_WithEmptyJobMappings_BehavesLikeDefault()
    {
        var comparer = new BuildReferenceComparer([]);
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var result = comparer.Equals(buildRef1, buildRef2);

        Assert.That(result, Is.True);
    }
}
