using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class BuildReferenceTests
{
    [Test]
    public void TestSerializable()
    {
        new BuildReference("MyJob", 123).AssertSerializable();
    }

    [Test]
    public void CompareTo_WithNull_ReturnsOne()
    {
        var reference = new BuildReference("MyJob", 42);
        Assert.That(reference.CompareTo(null), Is.EqualTo(1));
    }

    [TestCase("JobA", "JobB", -1)]
    [TestCase("JobB", "JobA", 1)]
    [TestCase("JobA", "JobA", 0)]
    public void CompareTo_DifferentJobNames_ReturnsExpectedOrder(string jobName1, string jobName2, int expectedResult)
    {
        var ref1 = new BuildReference(jobName1, 42);
        var ref2 = new BuildReference(jobName2, 42);

        Assert.That(ref1.CompareTo(ref2), Is.EqualTo(expectedResult));
    }

    [TestCase(1, 2, -1)]
    [TestCase(2, 1, 1)]
    [TestCase(42, 42, 0)]
    public void CompareTo_SameJobNameDifferentNumbers_ReturnsExpectedOrder(int number1, int number2, int expectedResult)
    {
        var job = "MyJob";
        var ref1 = new BuildReference(job, number1);
        var ref2 = new BuildReference(job, number2);

        Assert.That(ref1.CompareTo(ref2), Is.EqualTo(expectedResult));
    }

    [Test]
    public void Next_IncrementsBuildNumber()
    {
        var reference = new BuildReference("MyJob", 42);
        var next = reference.Next();

        Assert.Multiple(() =>
        {
            Assert.That(next.JobName, Is.EqualTo(reference.JobName));
            Assert.That(next.BuildNumber, Is.EqualTo(reference.BuildNumber + 1));
        });
    }

    [Test]
    public void ToString_IncludesJobNameAndBuildNumber()
    {
        var reference = new BuildReference("MyJob", 42);
        Assert.That(reference.ToString(), Is.EqualTo("MyJob #42"));
    }

    [Test]
    public void Equals_SameInstance_ReturnsTrue()
    {
        var buildRef = new BuildReference("job1", 123);

        var result = buildRef.Equals(buildRef);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_OneNull_ReturnsFalse()
    {
        var buildRef = new BuildReference("job1", 123);

        var result1 = buildRef.Equals(null);

        Assert.That(result1, Is.False);
    }

    [Test]
    public void Equals_SameJobAndBuildNumber_ReturnsTrue()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var result = buildRef1.Equals(buildRef2);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_DifferentJob_ReturnsFalse()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job2", 123);

        var result = buildRef1.Equals(buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_DifferentBuildNumber_ReturnsFalse()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 124);

        var result = buildRef1.Equals(buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_DifferentJobAndBuildNumber_ReturnsFalse()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job2", 124);

        var result = buildRef1.Equals(buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_WithJobMapping_MatchesMappedJobs()
    {
        var mapping = new JobMapping("old-job", "new-job");
        var buildRef1 = new BuildReference("old-job", 123);
        var buildRef2 = new BuildReference("new-job", 123);

        var result = buildRef1.Equals(buildRef2, [mapping]);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_WithJobMapping_DifferentBuildNumbers_ReturnsFalse()
    {
        var mapping = new JobMapping("old-job", "new-job");
        var buildRef1 = new BuildReference("old-job", 123);
        var buildRef2 = new BuildReference("new-job", 124);

        var result = buildRef1.Equals(buildRef2, [mapping]);

        Assert.That(result, Is.False);
    }

    [Test]
    public void Equals_WithJobMapping_PartialJobNameMatch()
    {
        var mapping = new JobMapping("MAIN", "PRIMARY");
        var buildRef1 = new BuildReference("MAIN-build", 100);
        var buildRef2 = new BuildReference("PRIMARY-build", 100);

        var result = buildRef1.Equals(buildRef2, [mapping]);

        Assert.That(result, Is.True);
    }

    [Test]
    public void Equals_WithMultipleJobMappings_AppliesAllMappings()
    {
        var mapping1 = new JobMapping("old", "new");
        var mapping2 = new JobMapping("build", "compile");
        var buildRef1 = new BuildReference("old-build", 50);
        var buildRef2 = new BuildReference("new-compile", 50);

        var result = buildRef1.Equals(buildRef2, [mapping1, mapping2]);

        Assert.That(result, Is.True);
    }

    [Test]
    public void GetHashCode_SameJobAndBuildNumber_ReturnsSameHashCode()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var hash1 = buildRef1.GetHashCode();
        var hash2 = buildRef2.GetHashCode();

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_DifferentJob_ReturnsDifferentHashCode()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job2", 123);

        var hash1 = buildRef1.GetHashCode();
        var hash2 = buildRef2.GetHashCode();

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_DifferentBuildNumber_ReturnsDifferentHashCode()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 124);

        var hash1 = buildRef1.GetHashCode();
        var hash2 = buildRef2.GetHashCode();

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithJobMapping_MappedJobsHaveSameHashCode()
    {
        var mapping = new JobMapping("old-job", "new-job");
        var buildRef1 = new BuildReference("old-job", 123);
        var buildRef2 = new BuildReference("new-job", 123);

        var hash1 = buildRef1.GetHashCode([mapping]);
        var hash2 = buildRef2.GetHashCode([mapping]);

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_ConsistentWithEquals()
    {
        var mapping = new JobMapping("build", "compile");
        var buildRef1 = new BuildReference("test-build", 100);
        var buildRef2 = new BuildReference("test-compile", 100);

        var areEqual = buildRef1.Equals(buildRef2, [mapping]);
        var hash1 = buildRef1.GetHashCode([mapping]);
        var hash2 = buildRef2.GetHashCode([mapping]);

        Assert.That(areEqual, Is.True);
        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void GetHashCode_WithJobMapping_DifferentBuildNumbers_ReturnsDifferentHashCode()
    {
        var mapping = new JobMapping("old-job", "new-job");
        var buildRef1 = new BuildReference("old-job", 123);
        var buildRef2 = new BuildReference("new-job", 124);

        var hash1 = buildRef1.GetHashCode([mapping]);
        var hash2 = buildRef2.GetHashCode([mapping]);

        Assert.That(hash1, Is.Not.EqualTo(hash2));
    }

    [Test]
    public void Comparer_InHashSet_DifferentBuildNumbers_AreSeparate()
    {
        var hashSet = new HashSet<BuildReference>();

        var buildRef1 = new BuildReference("job1", 100);
        var buildRef2 = new BuildReference("job1", 101);

        hashSet.Add(buildRef1);
        hashSet.Add(buildRef2);

        Assert.That(hashSet.Count, Is.EqualTo(2));
    }

    [Test]
    public void Equals_CaseSensitive()
    {
        var buildRef1 = new BuildReference("Job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var result = buildRef1.Equals(buildRef2);

        Assert.That(result, Is.False);
    }

    [Test]
    public void GetHashCode_MultipleCalls_ReturnsSameValue()
    {
        var buildRef = new BuildReference("job1", 123);

        var hash1 = buildRef.GetHashCode();
        var hash2 = buildRef.GetHashCode();

        Assert.That(hash1, Is.EqualTo(hash2));
    }

    [Test]
    public void Equals_WithEmptyJobMappings_BehavesLikeDefault()
    {
        var buildRef1 = new BuildReference("job1", 123);
        var buildRef2 = new BuildReference("job1", 123);

        var result = buildRef1.Equals(buildRef2);

        Assert.That(result, Is.True);
    }
}
