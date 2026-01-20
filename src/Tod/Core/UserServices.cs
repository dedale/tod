using Serilog;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Tod.Core;

[ExcludeFromCodeCoverage]
internal static class UserServices
{
    public static readonly string CurrentUserEmail = GetCurrentUserEmail();

    public static string? GetGitUserEmail()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = "config --get user.email",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return null;
            }

            string output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(TimeSpan.FromSeconds(2));

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
            {
                return output.Trim();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetCurrentUserEmail()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var fileName = "Tod.Windows.dll";
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(path))
            {
                try
                {
                    var assembly = Assembly.LoadFrom(path);
                    var type = assembly.GetType("Tod.Windows.UserDirectory");
                    var method = type?.GetMethod("GetCurrentUserEmail", BindingFlags.Public | BindingFlags.Static);
                    var email = method?.Invoke(null, null) as string;
                    if (email != null)
                    {
                        return email;
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Failed to get email from '{WindowsLib}'", fileName);
                }
            }
        }
        var gitEmail = GetGitUserEmail();
        if (!string.IsNullOrEmpty(gitEmail))
        {
            return gitEmail;
        }
        throw new InvalidOperationException("Unable to retrieve current user email on this platform.");
    }
}
