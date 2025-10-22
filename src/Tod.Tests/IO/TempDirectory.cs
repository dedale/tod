using System.Runtime.CompilerServices;

namespace Tod.Tests.IO;

internal sealed class TempDirectory([CallerMemberName] string caller = "") : IDisposable
{
    public DirectoryPath Directory { get; } = DirectoryPath.Temp.CreateUnique(caller);

    public void Dispose()
    {
        Directory.DeleteIgnoringErrors();
    }
}
