using System.Diagnostics.CodeAnalysis;
using System.DirectoryServices.AccountManagement;

namespace Tod.Windows;

[ExcludeFromCodeCoverage]
internal static class UserDirectory
{
    public static string GetCurrentUserEmail()
    {
        using var context = new PrincipalContext(ContextType.Domain, Environment.UserDomainName);
        var principal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, Environment.UserName);
        if (principal == null)
        {
            throw new InvalidOperationException($"User '{Environment.UserName}' not found in Active Directory");
        }
        return principal.EmailAddress;
    }
}