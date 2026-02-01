using System.Diagnostics.CodeAnalysis;
using System.DirectoryServices.AccountManagement;

namespace Tod.Windows;

[ExcludeFromCodeCoverage]
internal static class UserDirectory
{
    public static string GetUserEmail(string? userName, string? userDomain)
    {
        using var resolver = new WindowsDesktopAssemblyResolver();
        userName ??= Environment.UserName;
        userDomain ??= Environment.UserDomainName;
        using var context = new PrincipalContext(ContextType.Domain, userDomain);
        var principal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, userName);
        if (principal == null)
        {
            throw new InvalidOperationException($"User '{(!string.IsNullOrEmpty(userDomain) ? $"{userDomain}\\" : "")}{userName}' not found in Active Directory");
        }
        return principal.EmailAddress;
    }
}
