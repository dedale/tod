using NUnit.Framework;
using Tod.Jenkins;
using Tod.Tests.IO;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class ByJobNameStoreTests
{
    private TempDirectory _temp;
    private ByJobNameStore _store;

    [SetUp]
    public void SetUp()
    {
        _temp = new TempDirectory();
        var jobsJsonPath = Path.Combine(_temp.Path, "Jobs.json");
        _store = new ByJobNameStore(BuildBranch.OnDemand, [], jobsJsonPath);
    }

    [TearDown]
    public void TearDown()
    {
        JobName.Init(null);
        _temp.Dispose();
    }

    [Test]
    public void Load_ReturnsDeserializedValue_WhenJsonForJobNameExists()
    {
        var jobName = new JobName("MyJob");
        _store.Save(jobName, "expected-value");

        var result = _store.Load<string>(jobName, () => "default");

        Assert.That(result, Is.EqualTo("expected-value"));
    }

    [Test]
    public void Load_ReturnsDeserializedValue_WhenAlternatePathExists()
    {
        JobName.Init([new JobMapping("foo", "bar"), new JobMapping("old-job", "new-job")]);
        var oldJobName = new JobName("old-job");
        var newJobName = new JobName("new-job");
        _store.Save(oldJobName, "expected-value");

        var result = _store.Load<string>(newJobName, () => "default");

        Assert.That(result, Is.EqualTo("expected-value"));
    }

    [Test]
    public void Load_ReturnsCreatedValue_WhenJsonDoesNotExist()
    {
        JobName.Init([new JobMapping("old-job", "new-job")]);
        var jobName = new JobName("NonExistentJob");

        var result = _store.Load<string>(jobName, () => "default-value");

        Assert.That(result, Is.EqualTo("default-value"));
    }
}
