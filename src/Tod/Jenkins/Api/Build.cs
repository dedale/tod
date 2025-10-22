using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

internal sealed class Commit(string sha1)
{
    public string CommitId { get; } = sha1;
}

internal sealed class ChangeSet(Commit[] commits)
{
    public Commit[] Items { get; } = commits;
}

internal sealed class Build(string id, int number, BuildResult result, DateTime timestampUtc, int durationInMs, bool building, string[] commits)
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
        new ChangeSet([.. commits.Select(sha1 => new Commit(sha1))])
    ];

    public Sha1[] GetCommits()
    {
        return [.. ChangeSets.First().Items.Select(c => new Sha1(c.CommitId))];
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
        var commits = Array.Empty<string>();
        foreach (var changeSet in element.GetProperty("changeSets").EnumerateArray())
        {
            if (commits.Length > 0)
            {
                break;
            }
            commits = [.. changeSet.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("commitId").GetString()!)];
        }
        return new Build(id, number, result, timestampUtc, durationInMs, building, commits);
    }

    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return $"Build(Id={Id}, Number={Number}, Result={Result}, Timestamp={TimestampUtc})";
    }
}
