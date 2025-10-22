using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Tod.Core;

internal sealed class AlreadyLockedException(string message, Exception inner) : Exception(message, inner)
{
}

internal sealed class FileLock : IDisposable
{
    private readonly string _lockPath;
    private readonly FileStream _fileStream;

    public FileLock(string filePath, string reason)
    {
        string? exceptionInfo = null;
        try
        {
            exceptionInfo = TryReadLockReason(filePath, out var existingReason) ? $" (current lock is {existingReason})" : null;
            _lockPath = filePath + ".lock";
            _fileStream = new FileStream(_lockPath, FileMode.Create, FileAccess.Write, FileShare.Read);
            using var sw = new StreamWriter(_fileStream, leaveOpen: true);
            sw.WriteLine($"{DateTime.UtcNow} {reason} at {Environment.MachineName} pid={System.Diagnostics.Process.GetCurrentProcess().Id} command line={Environment.CommandLine}");
        }
        catch (IOException ex)
        {
            throw new AlreadyLockedException($"The file '{filePath}' is already locked.{exceptionInfo}", ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new AlreadyLockedException($"The file '{filePath}' is already locked.{exceptionInfo}", ex);
        }
    }

    public static bool TryReadLockReason(string filePath, [NotNullWhen(true)] out string? reason)
    {
        var lockPath = filePath + ".lock";
        if (!File.Exists(lockPath))
        {
            reason = null;
            return false;
        }
        using var fs = new FileStream(lockPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs, Encoding.UTF8);
        reason = sr.ReadToEnd();
        return true;
    }

    public void Dispose()
    {
        try
        {
            _fileStream.Dispose();
            File.Delete(_lockPath);
        }
        catch (IOException)
        {
            // Might fail if another process has locked the file in the meantime
        }
    }
}
