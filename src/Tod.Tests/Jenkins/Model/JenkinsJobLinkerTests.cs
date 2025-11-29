using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class JenkinsJobLinkerTests
{
    [Test]
    public void GetUrl_KnownJob_ReturnsCorrectUrl()
    {
        var config = new JenkinsConfig("http://jenkins.example.org");
        var linker = new JenkinsJobLinker(config);
        var jobName = new JobName("MY-job");
        var buildNumber = 42;
        Assert.That(linker.GetUrl(jobName, buildNumber), Is.EqualTo("http://jenkins.example.org/job/MY-job/42"));
    }

    [Test]
    public void GetUrl_KnownJobWithoutBuildNumber_ReturnsCorrectUrl()
    {
        var config = new JenkinsConfig("http://jenkins.example.org");
        var linker = new JenkinsJobLinker(config);
        var jobName = new JobName("MY-job");
        Assert.That(linker.GetUrl(jobName), Is.EqualTo("http://jenkins.example.org/job/MY-job/"));
    }

    [Test]
    public void GetUrl_JobPath_ReturnsCorrectUrl()
    {
        var config = new JenkinsConfig("http://jenkins.example.org");
        var linker = new JenkinsJobLinker(config);
        var jobName = new JobName("Very/Long/JobName");
        var buildNumber = 7;
        Assert.That(linker.GetUrl(jobName, buildNumber), Is.EqualTo("http://jenkins.example.org/job/Very/job/Long/job/JobName/7"));
    }
}