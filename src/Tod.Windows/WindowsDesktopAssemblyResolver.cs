using Serilog;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Tod.Windows;

[ExcludeFromCodeCoverage]
internal sealed class WindowsDesktopAssemblyResolver : IDisposable
{
    private readonly AssemblyLoadContext _context;
    private readonly Func<AssemblyLoadContext, AssemblyName, Assembly?> _handler;
    private readonly string? _windowsDesktopPath;
    private bool _isRegistered;

    public WindowsDesktopAssemblyResolver()
    {
        _context = AssemblyLoadContext.Default;
        _handler = ResolveFromWindowsDesktop;

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        _windowsDesktopPath = FindWindowsDesktopRuntime();
        if (_windowsDesktopPath == null)
        {
            return;
        }

        Log.Debug("Windows Desktop runtime found at: {Path}", _windowsDesktopPath);
        _context.Resolving += _handler;
        _isRegistered = true;
    }

    public void Dispose()
    {
        if (_isRegistered)
        {
            _context.Resolving -= _handler;
            _isRegistered = false;
        }
    }

    private Assembly? ResolveFromWindowsDesktop(AssemblyLoadContext context, AssemblyName name)
    {
        if (_windowsDesktopPath == null)
        {
            return null;
        }
        var candidate = Path.Combine(_windowsDesktopPath, name.Name + ".dll");
        if (File.Exists(candidate))
        {
            Log.Debug("Resolving assembly {Assembly} from Windows Desktop runtime", name.Name);
            return context.LoadFromAssemblyPath(candidate);
        }
        return null;
    }

    private static string? FindWindowsDesktopRuntime()
    {
        // Example: C:\Program Files\dotnet\shared\Microsoft.NETCore.App\10.0.2\
        var coreDir = RuntimeEnvironment.GetRuntimeDirectory();
        var sharedRoot = Path.GetFullPath(Path.Combine(coreDir, "..", ".."));

        string desktopRoot = Path.Combine(sharedRoot, "Microsoft.WindowsDesktop.App");
        if (!Directory.Exists(desktopRoot))
        {
            return null;
        }

        // Extract current runtime major.minor (e.g., "8.0")
        Version v = Environment.Version;
        var prefix = $"{v.Major}.{v.Minor}.";

        var folder = Directory.GetDirectories(desktopRoot)
            .Select(Path.GetFileName)
            .Where(f => f != null && f.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f)
            .FirstOrDefault();

        return folder == null ? null : Path.Combine(desktopRoot, folder);
    }
}