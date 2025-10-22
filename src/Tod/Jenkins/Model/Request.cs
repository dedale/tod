using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed record Request
{
    [JsonConstructor]
    private Request(Guid id, DateTime createdUtc, Sha1 commit, Sha1 parentCommit, BranchName referenceBranchName, string filters)
    {
        Id = id;
        CreatedUtc = createdUtc;
        Commit = commit;
        ParentCommit = parentCommit;
        ReferenceBranchName = referenceBranchName;
        Filters = filters;
    }

    public static Request Create(Sha1 commit, Sha1 parentCommit, BranchName referenceBranchName, string[] filters)
    {
        return new Request(
            Guid.NewGuid(),
            DateTime.UtcNow,
            commit,
            parentCommit,
            referenceBranchName,
            string.Join(";", filters)
        );
    }

    public Guid Id { get; }
    public DateTime CreatedUtc { get; }
    public Sha1 Commit { get; }
    public Sha1 ParentCommit { get; }
    public BranchName ReferenceBranchName { get; }
    public string Filters { get; }

    public string[] GetFilters() => Filters.Split(';', StringSplitOptions.RemoveEmptyEntries);
}
