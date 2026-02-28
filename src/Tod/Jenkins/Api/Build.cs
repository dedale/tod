using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed record CommitAuthor([property: JsonPropertyName("fullName")] string Name, string? Email = null);

internal sealed class Commit(string sha1, CommitAuthor? author = null, string? authorEmail = null, string? message = null)
{
    [JsonPropertyName("commitId")]
    public string CommitId { get; } = sha1;
    [JsonPropertyName("author")]
    public CommitAuthor? Author { get; } = author;
    [JsonPropertyName("authorEmail")]
    public string? AuthorEmail { get; } = authorEmail;
    [JsonPropertyName("msg")]
    public string? Message { get; } = message;
}

internal sealed class ChangeSet(Commit[] commits)
{
    [JsonPropertyName("items")]
    public Commit[] Items { get; } = commits;
}

internal sealed class Build(string id, int number, BuildResult result, DateTime timestampUtc, int durationInMs, bool building, Commit[] commits)
{
    public string Id { get; } = id;
    public int Number { get; } = number;
    [JsonIgnore]
    public BuildResult Result { get; } = result;
    [JsonPropertyName("result")]
    public string ResultString => Result.ToJenkinsString();
    [JsonIgnore]
    public DateTime TimestampUtc { get; } = timestampUtc;
    [JsonPropertyName("timestamp")]
    public long TimestampUtcMs => new DateTimeOffset(TimestampUtc).ToUnixTimeMilliseconds();
    [JsonPropertyName("duration")]
    public int DurationInMs => durationInMs;
    public bool Building => building;
    public ChangeSet[] ChangeSets =>
    [
        new ChangeSet(commits)
    ];

    public Sha1[] GetCommits()
    {
        return [.. commits.Select(c => new Sha1(c.CommitId))];
    }

    public CommitAuthor[] GetCommitAuthors()
    {
        return [.. commits.Where(c => c.Author != null).Select(c => c.Author! with { Email = c.AuthorEmail })];
    }

    public static Build FromJson(JsonElement element)
    {
        var id = element.GetProperty("id").GetString() ?? throw new ArgumentException("Build id is null", nameof(element));
        var number = element.GetProperty("number").GetInt32();
        var resultStr = element.GetProperty("result").GetString() ?? throw new ArgumentException("Build result is null", nameof(element));
        var result = resultStr.ToBuildResult();
        long timestampMillis = element.GetProperty("timestamp").GetInt64();
        var timestampUtc = DateTimeOffset.FromUnixTimeMilliseconds(timestampMillis).UtcDateTime;
        var durationInMs = element.GetProperty("duration").GetInt32();
        var building = element.GetProperty("building").GetBoolean();
        var commits = new List<Commit>();

        foreach (var changeSet in element.GetProperty("changeSets").EnumerateArray())
        {
            if (commits.Count > 0)
            {
                break;
            }

            foreach (var item in changeSet.GetProperty("items").EnumerateArray())
            {
                var commitId = item.GetProperty("commitId").GetString()!;
                CommitAuthor? author = null;

                if (item.TryGetProperty("author", out var authorElement))
                {
                    var name = authorElement.GetProperty("fullName").GetString() ?? string.Empty;
                    author = new CommitAuthor(name);
                }

                string? authorEmail = null;
                if (item.TryGetProperty("authorEmail", out var authorEmailElement))
                {
                    authorEmail = authorEmailElement.GetString();
                }

                string? message = null;
                if (item.TryGetProperty("msg", out var msgElement))
                {
                    message = msgElement.GetString();
                }

                commits.Add(new Commit(commitId, author, authorEmail, message));
            }
        }

        return new Build(id, number, result, timestampUtc, durationInMs, building, [.. commits]);
    }

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return $"Build(Id={Id}, Number={Number}, Result={Result}, Timestamp={TimestampUtc})";
    }
}
