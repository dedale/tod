using System.Diagnostics.CodeAnalysis;
using System.DirectoryServices.AccountManagement;
using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed record GitReference(BranchName Branch, Sha1 Commit);

internal sealed record Request
{
    [JsonConstructor]
    private Request(Guid id, string userName, string userEmail, DateTime createdUtc, Sha1 commit, GitReference gitReference, string filters)
    {
        Id = id;
        UserName = userName;
        UserEmail = userEmail;
        CreatedUtc = createdUtc;
        Commit = commit;
        GitReference = gitReference;
        Filters = filters;
    }

    [ExcludeFromCodeCoverage]
    private static string GetUserEmail(string userName)
    {
        using var context = new PrincipalContext(ContextType.Domain, Environment.UserDomainName);
        var principal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, userName);
        if (principal == null)
        {
            throw new InvalidOperationException($"User '{userName}' not found in Active Directory");
        }
        return principal.EmailAddress;
    }

    public static Request Create(Sha1 commit, Sha1 refCommit, BranchName refBranch, string[] filters, Func<string, string>? getUserEmail = null)
    {
        return Create(commit, new GitReference(refBranch, refCommit), filters, getUserEmail);
    }

    public static Request Create(Sha1 commit, GitReference gitReference, string[] filters, Func<string, string>? getUserEmail = null)
    {
        return new Request(
            Guid.NewGuid(),
            Environment.UserName,
            (getUserEmail ?? GetUserEmail)(Environment.UserName),
            DateTime.UtcNow,
            commit,
            gitReference,
            string.Join(";", filters)
        );
    }

    public Guid Id { get; }
    public string UserName { get; }
    public string UserEmail { get; }
    public DateTime CreatedUtc { get; }
    public Sha1 Commit { get; }
    public GitReference GitReference { get; }
    public string Filters { get; }

    public string[] GetFilters() => Filters.Split(';', StringSplitOptions.RemoveEmptyEntries);
}
