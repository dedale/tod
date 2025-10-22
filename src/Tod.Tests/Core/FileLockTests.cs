using NUnit.Framework;
using Tod.Core;
using Tod.Tests.IO;

namespace Tod.Tests.Core;

[TestFixture]
internal sealed class FileLockTests
{
    private static FileLock NewFileLock(string path)
    {
        return NewFileLock(path, out _);
    }

    private static FileLock NewFileLock(string path, out string reason)
    {
        reason = $"Testing lock reason {Guid.NewGuid()}";
        return new FileLock(path, reason);
    }

    [Test]
    public void Ctor_CreatesLockFileWithReason()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.txt");
        using (var locker = NewFileLock(testFilePath, out var reason))
        {
            var lockPath = testFilePath + ".lock";
            Assert.That(File.Exists(lockPath), Is.True);
            Assert.That(FileLock.TryReadLockReason(testFilePath, out var existingReason), Is.True);
            Assert.That(existingReason, Does.Contain(reason));
        }
        Assert.That(File.Exists(testFilePath + ".lock"), Is.False);
    }

    [Test]
    public void Ctor_WhenAlreadyLocked_Fails()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.txt");
        using (var locker1 = NewFileLock(testFilePath))
        {
            Assert.That(() => NewFileLock(testFilePath), Throws.TypeOf<AlreadyLockedException>());
        }
    }

    [Test]
    public void Dispose_RemovesLockFile()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.txt");
        var locker = NewFileLock(testFilePath);
        var lockPath = testFilePath + ".lock";
        Assert.That(File.Exists(lockPath), Is.True);
        locker.Dispose();
        Assert.That(File.Exists(lockPath), Is.False);
    }

    [Test]
    public void Dispose_ReleaseLock()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.txt");
        for (var i = 0; i < 2; i++)
        {
            using (var _ = NewFileLock(testFilePath))
            {
            }
        }
    }

    [Test]
    public async Task Ctor_OnConcurrentAccess_ThrowsAlreadyLockedException()
    {
        using var temp = new TempDirectory();
        var testFilePath = Path.Combine(temp.Directory.Path, $"testfile_{Guid.NewGuid()}.txt");
        var taskCount = Environment.ProcessorCount / 2;
        Assert.That(taskCount, Is.GreaterThan(1));
        var tasks = Enumerable.Range(0, taskCount).Select(_ => Task.Run(async () =>
        {
            var countByException = new Dictionary<Type, int>();
            for (var attempt = 0; attempt < 100; attempt++)
            {
                try
                {
                    using var locker = NewFileLock(testFilePath);
                    await Task.Delay(10).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    var type = ex.GetType();
                    if (!countByException.TryGetValue(type, out var count))
                    {
                        countByException.Add(type, 1);
                    }
                    else
                    {
                        countByException[type] = count + 1;
                    }
                }
            }
            return countByException;
        })).ToArray();
        await Task.WhenAll(tasks).ConfigureAwait(false);
        var totalCountByOtherExceptions = tasks
            .SelectMany(t => t.Result)
            .Where(x => x.Key != typeof(AlreadyLockedException))
            .GroupBy(x => x.Key)
            .ToDictionary(g => g.Key, g => g.Sum(x => x.Value));
        Assert.That(totalCountByOtherExceptions, Is.Empty);
    }
}
