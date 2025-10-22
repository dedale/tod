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
}
