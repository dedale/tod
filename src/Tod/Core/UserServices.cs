using Serilog;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Tod.Core;

[ExcludeFromCodeCoverage]
internal static class UserServices
{
    private static string? GetGitUserEmail()
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

    public static string GetUserEmail(string? userName, string? userDomain)
    {
        var current = userName == null && userDomain == null;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var fileName = "Tod.Windows.dll";
            var path = Path.Combine(AppContext.BaseDirectory, fileName);
            if (File.Exists(path))
            {
                try
                {
                    userName ??= Environment.UserName;
                    userDomain ??= Environment.UserDomainName;
                    var assembly = Assembly.LoadFrom(path);
                    var type = assembly.GetType("Tod.Windows.UserDirectory");
                    var method = type?.GetMethod("GetCurrentUserEmail", BindingFlags.Public | BindingFlags.Static);
                    var email = method?.Invoke(null, [userName, userDomain]) as string;
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
        if (current)
        {
            var gitEmail = GetGitUserEmail();
            if (!string.IsNullOrEmpty(gitEmail))
            {
                return gitEmail;
            }
        }
        throw new InvalidOperationException($"Unable to retrieve user email for {(string.IsNullOrEmpty(userDomain) ? $"{userDomain}\\" : "")}{userName}.");
    }
}
