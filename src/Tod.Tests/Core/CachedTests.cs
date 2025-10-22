using NUnit.Framework;
using Tod.Core;
using Tod.Jenkins;
using Tod.Tests.IO;
using Tod.Tests.Jenkins;

namespace Tod.Tests.Core;

[TestFixture]
internal sealed class CachedTests
{
    [Test]
    public void New_SavesValueToFile()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.json");
        var cached = Cached<Dummy, Dummy.Serializable>.New(Dummy.New(), testFilePath);
        Assert.That(File.Exists(testFilePath), Is.True);
        using var loadedDummy = LockedDummy.Load(testFilePath, "Load for verification").Value;
        Assert.That(loadedDummy.Value.References, Has.Count.EqualTo(2));
    }

    [Test]
    public void Ctor_LoadsValueFromFile()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.json");
        Dummy.New().SaveNew(testFilePath);

        var cached = new Cached<Dummy, Dummy.Serializable>(testFilePath);

        Assert.That(cached.Value, Is.Not.Null);
        Assert.That(cached.Value.References, Has.Count.EqualTo(2));
    }

    [Test]
    public void Value_WhenFileUnchanged_ReturnsCachedValue()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.json");
        Dummy.New().SaveNew(testFilePath);

        var cached = new Cached<Dummy, Dummy.Serializable>(testFilePath);
        var firstValue = cached.Value;
        var secondValue = cached.Value;

        Assert.That(ReferenceEquals(firstValue, secondValue), Is.True);
    }

    [Test]
    public void Value_WhenFileModified_ReloadsValue()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.json");
        Dummy.New().SaveNew(testFilePath);

        var cached = new Cached<Dummy, Dummy.Serializable>(testFilePath);
        var firstValue = cached.Value;
        var firstCount = firstValue.References.Count;

        // Modify the file
        Thread.Sleep(10); // Ensure timestamp difference
        var newDummy = new Dummy([
            RequestBuildReference.Create(new JobName("Job1")),
            RequestBuildReference.Create(new JobName("Job2")),
            RequestBuildReference.Create(new JobName("Job3"))
        ]);
        newDummy.SaveNew(testFilePath);

        var secondValue = cached.Value;
        var secondCount = secondValue.References.Count;

        Assert.That(ReferenceEquals(firstValue, secondValue), Is.False);
        Assert.That(firstCount, Is.EqualTo(2));
        Assert.That(secondCount, Is.EqualTo(3));
    }

    [Test]
    public void Value_AfterMultipleReads_MaintainsCorrectCache()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.json");
        Dummy.New().SaveNew(testFilePath);

        var cached = new Cached<Dummy, Dummy.Serializable>(testFilePath);
        
        // Multiple reads without modification
        var value1 = cached.Value;
        var value2 = cached.Value;
        var value3 = cached.Value;

        Assert.That(ReferenceEquals(value1, value2), Is.True);
        Assert.That(ReferenceEquals(value2, value3), Is.True);
    }

    [Test]
    public void Value_WhenFileModifiedMultipleTimes_ReloadsEachTime()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.json");
        Dummy.New().SaveNew(testFilePath);

        var cached = new Cached<Dummy, Dummy.Serializable>(testFilePath);
        var initialValue = cached.Value;
        var initialInstance = initialValue;

        for (int i = 0; i < 3; i++)
        {
            Thread.Sleep(10); // Ensure timestamp difference
            var newDummy = new Dummy([RequestBuildReference.Create(new JobName($"Job{i}"))]);
            newDummy.SaveNew(testFilePath);

            var currentValue = cached.Value;
            Assert.That(ReferenceEquals(initialInstance, currentValue), Is.False);
            Assert.That(currentValue.References, Has.Count.EqualTo(1));
            Assert.That(currentValue.References[0].JobName.Value, Is.EqualTo($"Job{i}"));
            
            initialInstance = currentValue;
        }
    }

    [Test]
    public void Value_PreservesDataIntegrity_AfterReload()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.json");
        
        var jobName = new JobName("TestJob");
        var buildNumber = RandomData.NextBuildNumber;
        var originalDummy = new Dummy([
            RequestBuildReference.Create(jobName).Trigger(buildNumber).DoneTriggered()
        ]);
        originalDummy.SaveNew(testFilePath);

        var cached = new Cached<Dummy, Dummy.Serializable>(testFilePath);
        var loadedValue = cached.Value;

        Assert.That(loadedValue.References, Has.Count.EqualTo(1));
        Assert.That(loadedValue.References[0].JobName.Value, Is.EqualTo("TestJob"));
        Assert.That(loadedValue.References[0].IsDone, Is.True);
    }
}
