using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

internal static class BuildAssertions
{
    public static void AssertEqual(Build expected, Build actual)
    {
        Assert.That(actual.Id, Is.EqualTo(expected.Id));
        Assert.That(actual.Number, Is.EqualTo(expected.Number));
        Assert.That(actual.Result, Is.EqualTo(expected.Result));
        Assert.That(actual.TimestampUtc, Is.EqualTo(expected.TimestampUtc));
        Assert.That(actual.ChangeSets, Has.Length.EqualTo(expected.ChangeSets.Length));
        for (var i = 0; i < expected.ChangeSets.Length; i++)
        {
            Assert.That(actual.ChangeSets[i].Items, Has.Length.EqualTo(expected.ChangeSets[i].Items.Length));
            for (var j = 0; j < expected.ChangeSets[i].Items.Length; j++)
            {
                Assert.That(actual.ChangeSets[i].Items[0].CommitId, Is.EqualTo(expected.ChangeSets[i].Items[0].CommitId));
            }
        }
    }
}

[TestFixture]
internal sealed class BuildTests
{
    [Test]
    public void Equals_Works()
    {
        var build = RandomBuilds.Generate(1).Single();
        var json = new BuildList { Builds = [build] }.Serialize();
        var clone = json.RootElement.GetProperty("builds").EnumerateArray().Select(Build.FromJson).Single();
        BuildAssertions.AssertEqual(build, clone);
    }

    [Test]
    public void GetCommits_WithManyChangesets_TakesFirstOne()
    {
        var build = RandomBuilds.Generate(1).Single();
        var changeSet1 = new[]
        {
            RandomData.NextSha1().Value,
            RandomData.NextSha1().Value

        };
        var changeSet2 = new[]
        {
            RandomData.NextSha1().Value,
        };
        var buildDoc = new
        {
            id = build.Id,
            number = build.Number,
            result = build.Result.ToJenkinsString(),
            timestamp = new DateTimeOffset(build.TimestampUtc).ToUnixTimeMilliseconds(),
            duration = build.DurationInMs,
            building = false,
            changeSets = new[]
            {
                new
                {
                    items = new[]
                    {
                        new { commitId = changeSet1[0] },
                        new { commitId = changeSet1[1] }
                    }
                },
                new
                {
                    items = new[]
                    {
                        new { commitId = changeSet2[0] }
                    }
                }
            }
        }.Serialize();
        var clone = Build.FromJson(buildDoc.RootElement);
        Assert.That(clone.GetCommits().Select(s => s.Value), Is.EquivalentTo(changeSet1));
    }

    [Test]
    public void FromJson_WithoutBuildId_ThrowsArgumentException()
    {
        var build = RandomBuilds.Generate(1).Single();
        var buildDoc = new
        {
            id = (string)null!,
            number = build.Number,
            result = build.Result.ToJenkinsString(),
            timestamp = new DateTimeOffset(build.TimestampUtc).ToUnixTimeMilliseconds(),
            duration = build.DurationInMs,
            building = false,
            changeSets = new[]
            {
                new
                {
                    items = new[]
                    {
                        new { commitId = RandomData.NextSha1().Value }
                    }
                }
            }
        }.Serialize();
        Assert.That(() => Build.FromJson(buildDoc.RootElement),
            Throws.ArgumentException.And.Message.EqualTo("Build id is null (Parameter 'element')"));
    }

    [Test]
    public void FromJson_WithoutBuildResult_ThrowsArgumentException()
    {
        var build = RandomBuilds.Generate(1).Single();
        var buildDoc = new
        {
            id = build.Id,
            number = build.Number,
            result = (string)null!,
            timestamp = new DateTimeOffset(build.TimestampUtc).ToUnixTimeMilliseconds(),
            duration = build.DurationInMs,
            building = false,
            changeSets = new[]
            {
                new
                {
                    items = new[]
                    {
                        new { commitId = RandomData.NextSha1().Value }
                    }
                }
            }
        }.Serialize();
        Assert.That(() => Build.FromJson(buildDoc.RootElement),
            Throws.ArgumentException.And.Message.EqualTo("Build result is null (Parameter 'element')"));
    }
}
