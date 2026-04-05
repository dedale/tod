using LibGit2Sharp;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tod.Git;

[JsonConverter(typeof(SingleStringValueConverterFactory))]
internal sealed record Sha1(string Value)
{
    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return Value;
    }
}

internal sealed class GitRepo : IDisposable
{
    private readonly IRepository _repo;

    [ExcludeFromCodeCoverage]
    private static IRepository NewRepository(string path)
    {
        return new Repository(path);
    }

    [ExcludeFromCodeCoverage]
    public GitRepo(string? fromDir = null)
        : this(fromDir, Repository.Discover, NewRepository)
    {
    }

    public GitRepo(string? fromDir, Func<string?, string?> discover, Func<string, IRepository> newRepo)
    {
        var discovered = discover(fromDir);
        if (discovered == null)
        {
            throw new ArgumentException($"No git repository found from directory '{fromDir}'");
        }
        _repo = newRepo(discovered);
    }

    public Sha1 Head => new(_repo.Head.Tip.Sha);

    public Sha1[] GetLastCommits(int count)
    {
        return [.. _repo.Commits.Take(count).Select(c => new Sha1(c.Sha))];
    }

    public void Dispose()
    {
        _repo.Dispose();
    }
}
