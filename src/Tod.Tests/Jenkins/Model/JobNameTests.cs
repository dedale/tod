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
}
