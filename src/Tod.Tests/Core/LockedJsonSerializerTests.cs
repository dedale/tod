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
    [Test]
    public void New_ReturnsLockedJson_IsLocked()
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Directory.Path, "request.json");
        using (var lockedJson = new LockedDummy(dummy, path, "New request"))
        {
            Assert.That(() => new LockedDummy(dummy, path, "New request"), Throws.TypeOf<AlreadyLockedException>());
        }
    }

    [Test]
    public void Load_ReturnsLockedJson_IsLocked()
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Directory.Path, "request.json");
        dummy.SaveNew(path);
        using (var loaded = LockedDummy.Load(path, "Load request"))
        {
            Assert.That(() => new LockedDummy(dummy, path, "New request"), Throws.TypeOf<AlreadyLockedException>());
        }
    }

    [Test]
    public void LoadUnlocked_WhenAlreadyLocked_DoesNotThrow()
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Directory.Path, "request.json");
        dummy.SaveNew(path);
        using (var lockedJson = new LockedDummy(dummy, path, "Locking for test"))
        {
            var unlockedDummy = LockedDummy.LoadUnlocked(path);
            Assert.That(unlockedDummy.References.Count, Is.EqualTo(dummy.References.Count));
        }
    }

    [Test]
    public void Dispose_UnlocksFile_CanLockAgain()
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Directory.Path, "request.json");
        using (var lockedJson = new LockedDummy(dummy, path, "New request"))
        {
            // Locked
            Assert.That(() => new LockedDummy(dummy, path, "New request"), Throws.TypeOf<AlreadyLockedException>());
        }
        // Unlocked
        using (var lockedJson2 = new LockedDummy(dummy, path, "New request"))
        {
        }
    }

    [Test]
    public void Load_InvalidJson_Throws()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Directory.Path, "request.json");
        File.WriteAllText(path, JsonSerializer.Serialize((Dummy)null!));
        Assert.That(() => LockedDummy.Load(path, "Load invalid json"),
            Throws.InvalidOperationException.And.Message.StartsWith("Cannot deserialize Tod.Tests.Core.Dummy+Serializable from"));
        File.Delete(path);
        Assert.That(Directory.GetFiles(temp.Directory.Path), Is.Empty);
    }

    [Test]
    public void LoadUnlocked_InvalidJson_Throws()
    {
        using var temp = new TempDirectory();
        var path = Path.Combine(temp.Directory.Path, "request.json");
        File.WriteAllText(path, JsonSerializer.Serialize((Dummy)null!));
        Assert.That(() => LockedDummy.LoadUnlocked(path),
            Throws.InvalidOperationException.And.Message.StartsWith("Cannot deserialize Tod.Tests.Core.Dummy+Serializable from"));
        File.Delete(path);
        Assert.That(Directory.GetFiles(temp.Directory.Path), Is.Empty);
    }

    private static bool AreEqual(RequestBuildReference x, RequestBuildReference y)
    {
        return x.Match(
            onPending: jobNameX => y.Match(
                onPending: jobNameY => jobNameX.Equals(jobNameY),
                onTriggered: _ => false,
                onDone: _ => false),
            onTriggered: referenceX => y.Match(
                onPending: _ => false,
                onTriggered: referenceY => referenceX.JobName.Equals(referenceY.JobName) && referenceX.BuildNumber == referenceY.BuildNumber,
                onDone: _ => false),
            onDone: referenceX => y.Match(
                onPending: _ => false,
                onTriggered: _ => false,
                onDone: referenceY => referenceX.JobName.Equals(referenceY.JobName) && referenceX.BuildNumber == referenceY.BuildNumber)
        );
    }

    [TestCase(true, false)]
    [TestCase(false, true)]
    public void SaveLoad_WithDifferentIndenting_PreservesData(bool saveIndented, bool loadIndented)
    {
        using var temp = new TempDirectory();
        var dummy = Dummy.New();
        var path = Path.Combine(temp.Directory.Path, "request.json");
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
        var path = Path.Combine(temp.Directory.Path, "request.json");
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
        var path = Path.Combine(temp.Directory.Path, "request.json");
        DateTime lastModifiedUtc;
        using (var lockedJson = new LockedDummy(dummy, path, "New request"))
        {
            lockedJson.Value.Save();
            lastModifiedUtc = lockedJson.Value.LastModifiedUtc;
            Thread.Sleep(10); // Ensure timestamp difference
            lockedJson.Value.Update(d =>
            {
                var newReferences = new List<RequestBuildReference>(d.References)
                {
                    RequestBuildReference.Create(new JobName("AdditionalJob"))
                };
                return new Dummy(newReferences);
            });
        }
        using (var lockedJson = LockedDummy.Load(path, "Load updated"))
        {
            Assert.That(lockedJson.Value.Value.References.Count, Is.EqualTo(3));
            Assert.That(lockedJson.Value.LastModifiedUtc, Is.GreaterThan(lastModifiedUtc));
        }
    }
}
