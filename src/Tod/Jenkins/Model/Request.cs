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

    public static Request Create(Sha1 commit, Sha1 refCommit, BranchName refBranch, string[] filters, string userEmail)
    {
        return Create(commit, new GitReference(refBranch, refCommit), filters, userEmail);
    }

    public static Request Create(Sha1 commit, GitReference gitReference, string[] filters, string userEmail)
    {
        return new Request(
            Guid.NewGuid(),
            Environment.UserName,
            userEmail,
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
