using NUnit.Framework;
using System.Text.Json;
using Tod.Core;
using Tod.Jenkins;
using Tod.Tests.IO;
using Tod.Tests.Jenkins;

namespace Tod.Tests.Core;

[TestFixture]
internal sealed class LockedJsonSerializerTests
{
    [TestCase(0, 0)]
    [TestCase(20, 10)]
    public void New_ReturnsLockedJson_IsLocked(int timeoutInMs, int retryInMs)
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Path, "request.json");
        using (var lockedJson = new LockedDummy(dummy, path, "New request"))
        {
            Assert.That(() => new LockedDummy(dummy, path, "New request", TimeSpan.FromMilliseconds(timeoutInMs), retryInMs), Throws.TypeOf<TimeoutException>());
        }
    }

    [TestCase(0, 0)]
    [TestCase(20, 10)]
    public void Load_ReturnsLockedJson_IsLocked(int timeoutInMs, int retryInMs)
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Path, "request.json");
        dummy.SaveNew(path);
        using (var loaded = LockedDummy.Load(path, "Load request"))
        {
            Assert.That(() => new LockedDummy(dummy, path, "New request", TimeSpan.FromMilliseconds(timeoutInMs), retryInMs), Throws.TypeOf<TimeoutException>());
        }
    }

    [Test]
    public void LoadUnlocked_WhenAlreadyLocked_DoesNotThrow()
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Path, "request.json");
        dummy.SaveNew(path);
        using (var lockedJson = new LockedDummy(dummy, path, "Locking for test"))
        {
            var unlockedDummy = LockedDummy.LoadUnlocked(path);
            Assert.That(unlockedDummy.References.Count, Is.EqualTo(dummy.References.Count));
        }
    }

    [TestCase(0, 0)]
    [TestCase(20, 10)]
    public void Dispose_UnlocksFile_CanLockAgain(int timeoutInMs, int retryInMs)
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Path, "request.json");
        using (var lockedJson = new LockedDummy(dummy, path, "New request"))
        {
            // Locked
            Assert.That(() => new LockedDummy(dummy, path, "New request", TimeSpan.FromMilliseconds(timeoutInMs), retryInMs), Throws.TypeOf<TimeoutException>());
        }
        // Unlocked
        using (var lockedJson2 = new LockedDummy(dummy, path, "New request"))
        {
        }
    }

    [Test]
    public async Task New_WhenUnlockedBeforeTimeout_CanLock()
    {
        using var temp = new TempDirectory();
        Task<LockedDummy>? task = null;
        try
        {
            var dummy = Dummy.New();
            var path = Path.Combine(temp.Path, "request.json");
            using (var lockedJson = new LockedDummy(dummy, path, "New request"))
            {
                task = Task.Run(() => new LockedDummy(dummy, path, "New request"));
            }
            await task.ConfigureAwait(false);
            Assert.That(task.Result, Is.InstanceOf<LockedDummy>());
        }
        finally
        {
            if (task != null)
            {
                await task.ConfigureAwait(false);
                task.Result.Dispose();
            }
        }
    }

    [Test]
    public async Task Load_WhenUnlockedBeforeTimeout_CanLock()
    {
        using var temp = new TempDirectory();
        Task<LockedDummy>? task = null;
        try
        {
            var dummy = Dummy.New();
            var path = Path.Combine(temp.Path, "request.json");
            using (var lockedJson = new LockedDummy(dummy, path, "New request"))
            {
                task = Task.Run(() => LockedDummy.Load(path, "Load request", retryDelayInMs: 10));
                lockedJson.Save();
            }
            await task.ConfigureAwait(false);
            Assert.That(task.Result, Is.InstanceOf<LockedDummy>());
        }
        finally
        {
            if (task != null)
            {
                await task.ConfigureAwait(false);
                task.Result.Dispose();
            }
        }
    }

    [Test]
    public void Load_InvalidJson_Throws()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(path, JsonSerializer.Serialize((Dummy)null!));
        Assert.That(() => LockedDummy.Load(path, "Load invalid json"),
            Throws.InvalidOperationException.And.Message.StartsWith("Cannot deserialize Tod.Tests.Core.Dummy+Serializable from"));
        File.Delete(path);
        Assert.That(Directory.GetFiles(temp.Path), Is.Empty);
    }

    [Test]
    public void LoadUnlocked_InvalidJson_Throws()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Path, "request.json");
        File.WriteAllText(path, JsonSerializer.Serialize((Dummy)null!));
        Assert.That(() => LockedDummy.LoadUnlocked(path),
            Throws.InvalidOperationException.And.Message.StartsWith("Cannot deserialize Tod.Tests.Core.Dummy+Serializable from"));
        File.Delete(path);
        Assert.That(Directory.GetFiles(temp.Path), Is.Empty);
    }

    private static bool AreEqual(RequestTestBuildReference x, RequestTestBuildReference y)
    {
        return x.Match(
            onPending: jobNameX => y.Match(
                onPending: jobNameY => jobNameX.Equals(jobNameY),
                onQueued: _ => false,
                onDone: _ => false),
            onQueued: jobX => y.Match(
                onPending: _ => false,
                onQueued: jobY => jobX.Equals(jobY),
                onDone: _ => false),
            onDone: referenceX => y.Match(
                onPending: _ => false,
                onQueued: _ => false,
                onDone: referenceY => referenceX.JobName.Equals(referenceY.JobName) && referenceX.BuildNumber == referenceY.BuildNumber)
        );
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void SaveLoad_WithDifferentIndenting_PreservesData(bool saveIndented, bool loadIndented)
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Path, "request.json");
        // Save
        using (var lockedJson = LockedJsonSerializer<Dummy, Dummy.Serializable>.New(dummy, path, "Save request", saveIndented))
        {
            lockedJson.Save();
        }
        // Load
        Dummy loadedDummy;
        using (var lockedJson = LockedJsonSerializer<Dummy, Dummy.Serializable>.Load(path, "Load request", loadIndented))
        {
            loadedDummy = lockedJson.Value;
        }
        // Verify
        Assert.That(loadedDummy.References.Count, Is.EqualTo(dummy.References.Count));
        for (int i = 0; i < dummy.References.Count; i++)
        {
            Assert.That(AreEqual(loadedDummy.References[i], dummy.References[i]), Is.True, $"Reference at index {i} differs");
        }
    }

    [Test]
    public void LastModifiedUtc_ReflectsFileModificationTime()
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Path, "request.json");
        DateTime beforeSaveUtc = DateTime.UtcNow.AddSeconds(-1);
        DateTime lastModifiedUtc;
        using (var lockedJson = new LockedDummy(dummy, path, "New request"))
        {
            Assert.That(lockedJson.Value.LastModifiedUtc, Is.EqualTo(DateTime.MinValue));
            lockedJson.Value.Save();
            lastModifiedUtc = lockedJson.Value.LastModifiedUtc;
        }
        DateTime afterSaveUtc = DateTime.UtcNow;
        Assert.That(lastModifiedUtc, Is.GreaterThanOrEqualTo(beforeSaveUtc).And.LessThanOrEqualTo(afterSaveUtc));
    }

    [Test]
    public void Update_ModifiesValueAndUpdatesLastModifiedUtc()
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Path, "request.json");
        DateTime lastModifiedUtc;
        using (var lockedJson = new LockedDummy(dummy, path, "New request"))
        {
            lockedJson.Value.Save();
            lastModifiedUtc = lockedJson.Value.LastModifiedUtc;
            Thread.Sleep(10); // Ensure timestamp difference
            lockedJson.Value.Update(d =>
            {
                var newReferences = new List<RequestTestBuildReference>(d.References)
                {
                    RequestTestBuildReference.Create(new JobName("AdditionalJob"))
                };
                return Task.FromResult(new Dummy(newReferences));
            });
        }
        using (var lockedJson = LockedDummy.Load(path, "Load updated"))
        {
            Assert.That(lockedJson.Value.Value.References.Count, Is.EqualTo(3));
            Assert.That(lockedJson.Value.LastModifiedUtc, Is.GreaterThan(lastModifiedUtc));
        }
    }
}
