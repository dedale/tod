using System.Runtime.CompilerServices;

namespace Tod.Tests.IO;

internal sealed class TempDirectory([CallerMemberName] string caller = "") : IDisposable
{
    private readonly DirectoryPath _directory = DirectoryPath.Temp.CreateUnique(caller);

    public string Path => _directory.Path;

    public void Dispose()
    {
        _directory.DeleteIgnoringErrors();
    }
}
