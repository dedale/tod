using System.Security.Cryptography;
using System.Text;
using Tod.Git;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

internal static class RandomBuilds
{
    private static readonly Random s_rand = new();

    // Jenkins builds are ordered from latest to oldest
    public static IEnumerable<Build> Generate(int count, int[] buildNumbers = null!, bool[] success = null!, bool[] buildings = null!)
    {
        buildNumbers ??= [];
        success ??= [];
        buildings ??= [];
        var latestNumber = s_rand.Next(1_000, 2_000);
        var timestamp = DateTime.UtcNow.TruncateSeconds().AddMinutes(-s_rand.Next(10, 20));
        for (var i = 0; i < count; i++)
        {
            var id = s_rand.Next(1_000_000, 9_999_999).ToString();
            var number = buildNumbers.Length > i ? buildNumbers[i] : latestNumber - i;
            var result = success.Length > i ? (success[i] ? BuildResult.Success : BuildResult.Failure) : (s_rand.Next(0, 2) == 0 ? BuildResult.Success : BuildResult.Failure);
            timestamp = timestamp.AddMinutes(-s_rand.Next(10, 20));
            var durationInMs = s_rand.Next(5, 15) * 60 * 1000;
            var building = buildings.Length > i ? buildings[i] : false;
            var commits = Enumerable.Range(0, s_rand.Next(1, 3)).Select(_ => RandomData.NextSha1().Value).ToArray();
            yield return new Build(id, number, result, timestamp, durationInMs, building, commits);
        }
    }
}

internal static class RandomFailedTests
{
    private static readonly Random s_rand = new();

    public static IEnumerable<FailedTest> Generate(int count)
    {
        for (var i = 0; i < count; i++)
        {
            var className = $"Test.Class{s_rand.Next(10, 100)}";
            var testName = $"Test{s_rand.Next(10, 100)}";
            var details = Guid.NewGuid().ToString();
            yield return new FailedTest(className, testName, details);
        }
    }
}

internal static class RandomData
{
    private static readonly Random s_random = new();

    public static Sha1 NextSha1()
    {
        var input = Guid.NewGuid().ToString();
        var hashBytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return new Sha1(BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant());
    }

    private static readonly HashSet<int> s_usedNumbers = [];
    public static int NextBuildNumber
    {
        get
        {
            while (true)
            {
                // Some unit tests fail if the same number is used twice
                var number = s_random.Next(10, 1000);
                if (s_usedNumbers.Add(number))
                {
                    return number;
                }
            }
        }
    }

    public static RootBuild NextRootBuild(string jobName = "MyJob", int buildNumber = 0, bool isSuccessful = true, int commits = 2, string[]? testJobNames = null)
    {
        return new RootBuild(
            new JobName(jobName),
            Guid.NewGuid().ToString(),
            buildNumber == 0 ? NextBuildNumber : buildNumber,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            isSuccessful,
            [.. Enumerable.Range(0, commits).Select(_ => NextSha1())],
            [.. (testJobNames ?? ["MyTestJob1", "MyTestJob2"]).Select(j => new JobName(j))]
        );
    }

    public static TestBuild NextTestBuild(string testJobName = "MyTestJob", int buildNumber = 0, BuildReference? rootBuild = null)
    {
        return new TestBuild(
            new JobName(testJobName),
            Guid.NewGuid().ToString(),
            buildNumber == 0 ? NextBuildNumber : buildNumber,
            DateTime.UtcNow.AddHours(-1),
            DateTime.UtcNow,
            true,
            rootBuild ?? new BuildReference("MyJob", 42),
            []
        );
    }
}
