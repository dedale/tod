using Moq;
using NUnit.Framework;
using System.Text.Json;
using Tod.Gerrit;
using Tod.Git;
using Tod.Jenkins;
using Tod.Tests.Jenkins;

namespace Tod.Tests.Gerrit;

[TestFixture]
internal sealed class GerritClientTests
{
    private Mock<IApiClient> _apiClient;
    private const string ServerUrl = "https://gerrit.example.com";
    private const string GerritToken = "test-gerrit-token";

    [SetUp]
    public void SetUp()
    {
        _apiClient = new Mock<IApiClient>(MockBehavior.Strict);
    }

    [TearDown]
    public void TearDown()
    {
        _apiClient.VerifyAll();
    }

    [Test]
    public void TestConstructor()
    {
        using var client = new GerritClient("https://gerrit.example.org", "token");
        Assert.That(client, Is.Not.Null);
    }

    [Test]
    public async Task IsKnown_ReturnsTrue_WhenCommitExistsInGerrit()
    {
        var commit = RandomData.NextSha1();
        var expectedUrl = $"{ServerUrl}/a/changes/?q=commit:{commit.Value}";
        var jsonResponse = JsonDocument.Parse("""
            [
                {
                    "id": "project~branch~I1234567890123456789012345678901234567890",
                    "project": "myproject",
                    "branch": "main",
                    "change_id": "I1234567890123456789012345678901234567890",
                    "subject": "Test change",
                    "status": "NEW",
                    "created": "2024-01-01 12:00:00.000000000",
                    "updated": "2024-01-01 12:00:00.000000000",
                    "submit_type": "MERGE_IF_NECESSARY",
                    "insertions": 10,
                    "deletions": 5,
                    "_number": 12345
                }
            ]
            """);

        _apiClient.Setup(x => x.GetAsync(expectedUrl)).ReturnsAsync(jsonResponse);
        _apiClient.Setup(x => x.Dispose());

        using var client = new GerritClient(ServerUrl, GerritToken, _apiClient.Object);
        var result = await client.IsKnown(commit);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task IsKnown_ReturnsFalse_WhenCommitDoesNotExistInGerrit()
    {
        var commit = RandomData.NextSha1();
        var expectedUrl = $"{ServerUrl}/a/changes/?q=commit:{commit.Value}";
        var jsonResponse = JsonDocument.Parse("[]");

        _apiClient.Setup(x => x.GetAsync(expectedUrl)).ReturnsAsync(jsonResponse);
        _apiClient.Setup(x => x.Dispose());

        using var client = new GerritClient(ServerUrl, GerritToken, _apiClient.Object);
        var result = await client.IsKnown(commit);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsKnown_ReturnsFalse_WhenGerritQueryFails()
    {
        var commit = RandomData.NextSha1();
        var expectedUrl = $"{ServerUrl}/a/changes/?q=commit:{commit.Value}";

        _apiClient.Setup(x => x.GetAsync(expectedUrl)).ThrowsAsync(new InvalidOperationException("Network error"));
        _apiClient.Setup(x => x.Dispose());

        using var client = new GerritClient(ServerUrl, GerritToken, _apiClient.Object);
        var result = await client.IsKnown(commit);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task IsKnown_ReturnsTrue_WhenMultipleChangesExist()
    {
        var commit = RandomData.NextSha1();
        var expectedUrl = $"{ServerUrl}/a/changes/?q=commit:{commit.Value}";
        var jsonResponse = JsonDocument.Parse("""
            [
                {
                    "id": "project~branch~I1111111111111111111111111111111111111111",
                    "project": "myproject",
                    "branch": "main",
                    "_number": 12345
                },
                {
                    "id": "project~branch~I2222222222222222222222222222222222222222",
                    "project": "myproject",
                    "branch": "feature",
                    "_number": 12346
                }
            ]
            """);

        _apiClient.Setup(x => x.GetAsync(expectedUrl)).ReturnsAsync(jsonResponse);
        _apiClient.Setup(x => x.Dispose());

        using var client = new GerritClient(ServerUrl, GerritToken, _apiClient.Object);
        var result = await client.IsKnown(commit);

        Assert.That(result, Is.True);
    }
}
