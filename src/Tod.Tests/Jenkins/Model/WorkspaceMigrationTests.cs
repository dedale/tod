using NUnit.Framework;
using System.Text.Json;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins.Model;

[TestFixture]
internal sealed class WorkspaceMigrationTests
{
    private string _tempDir = null!;

    [SetUp]
    public void SetUp()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tod-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Test]
    public void WorkspaceStore_LoadMetadata_WithNoFile_ReturnsDefaultMetadata()
    {
        var store = new WorkspaceStore(_tempDir);

        var metadata = store.LoadMetadata();

        Assert.That(metadata.RequestsFormatVersion, Is.EqualTo(0));
    }

    [Test]
    public void WorkspaceStore_SaveMetadata_CreatesFile()
    {
        var store = new WorkspaceStore(_tempDir);
        var metadata = new WorkspaceMetadata(RequestsFormatVersion: 1);

        store.SaveMetadata(metadata);

        var metadataPath = Path.Combine(_tempDir, "Workspace.json");
        Assert.That(File.Exists(metadataPath), Is.True);

        var loaded = store.LoadMetadata();
        Assert.That(loaded.RequestsFormatVersion, Is.EqualTo(1));
    }

    [Test]
    public void WorkspaceStore_Metadata_RoundTrip()
    {
        var store = new WorkspaceStore(_tempDir);
        var metadata = new WorkspaceMetadata(RequestsFormatVersion: 2);

        store.SaveMetadata(metadata);
        var loaded = store.LoadMetadata();

        Assert.That(loaded, Is.EqualTo(metadata));
    }

    [Test]
    public void Workspace_Load_WithNoRequests_DoesNotMigrate()
    {
        var store = new WorkspaceStore(_tempDir);

        var workspace = Workspace.Load(_tempDir, store);

        var metadata = store.LoadMetadata();
        Assert.That(metadata.RequestsFormatVersion, Is.EqualTo(1));
    }

    [Test]
    public void Workspace_Load_WithOldFormatRequest_MigratesRequest()
    {
        var requestsDir = Path.Combine(_tempDir, "Requests");
        Directory.CreateDirectory(requestsDir);

        var oldFormatJson = """
            {
              "Request": {
                "Id": "12345678-1234-1234-1234-123456789abc",
                "UserName": "user",
                "UserEmail": "user@example.org",
                "CreatedUtc": "2024-01-01T12:00:00.0000000Z",
                "Commit": "abc123def456",
                "GitReference": {
                  "Branch": "main",
                  "Commit": "parent123"
                },
                "TestFilters": "tests"
              },
              "ChainDiffs": [
                {
                  "Status": "RootTriggered",
                  "ReferenceRoot": {
                    "JobName": "MainBuild",
                    "BuildNumber": 100
                  },
                  "OnDemandRoot": {
                    "Queued": {
                      "Key": "OnDemandBuild",
                      "Value": "abc123def456"
                    }
                  },
                  "TestBuildDiffs": [
                    {
                      "ReferenceBuild": {
                        "Pending": "MainTest"
                      },
                      "OnDemandBuild": {
                        "Pending": "OnDemandTest"
                      },
                      "TestDuration": "00:00:00"
                    }
                  ]
                }
              ]
            }
            """;

        var requestFile = Path.Combine(requestsDir, "12345678-1234-1234-1234-123456789abc.json");
        File.WriteAllText(requestFile, oldFormatJson);

        var store = new WorkspaceStore(_tempDir);
        var workspace = Workspace.Load(_tempDir, store);

        var migratedContent = File.ReadAllText(requestFile);
        Assert.That(migratedContent, Does.Contain("\"FormatVersion\": 1"));
        Assert.That(migratedContent, Does.Contain("\"BaselineRoot\""));
        Assert.That(migratedContent, Does.Not.Contain("\"ReferenceRoot\""));
        Assert.That(migratedContent, Does.Contain("\"BaselineBuild\""));
        Assert.That(migratedContent, Does.Not.Contain("\"ReferenceBuild\""));

        var metadata = store.LoadMetadata();
        Assert.That(metadata.RequestsFormatVersion, Is.EqualTo(1));
    }

    [Test]
    public void Workspace_Load_WithNewFormatRequest_DoesNotModifyRequest()
    {
        var requestsDir = Path.Combine(_tempDir, "Requests");
        Directory.CreateDirectory(requestsDir);

        var newFormatJson = """
            {
              "FormatVersion": 1,
              "Request": {
                "Id": "12345678-1234-1234-1234-123456789abc",
                "UserName": "user",
                "UserEmail": "user@example.org",
                "CreatedUtc": "2024-01-01T12:00:00.0000000Z",
                "Commit": "abc123def456",
                "GitReference": {
                  "Branch": "main",
                  "Commit": "parent123"
                },
                "TestFilters": "tests"
              },
              "ChainDiffs": [
                {
                  "Status": "RootTriggered",
                  "BaselineRoot": {
                    "JobName": "MainBuild",
                    "BuildNumber": 100
                  },
                  "OnDemandRoot": {
                    "Queued": {
                      "Key": "OnDemandBuild",
                      "Value": "abc123def456"
                    }
                  },
                  "TestBuildDiffs": [
                    {
                      "BaselineBuild": {
                        "Pending": "MainTest"
                      },
                      "OnDemandBuild": {
                        "Pending": "OnDemandTest"
                      },
                      "TestDuration": "00:00:00"
                    }
                  ]
                }
              ]
            }
            """;

        var requestFile = Path.Combine(requestsDir, "12345678-1234-1234-1234-123456789abc.json");
        File.WriteAllText(requestFile, newFormatJson);
        var originalModified = File.GetLastWriteTimeUtc(requestFile);

        Thread.Sleep(100);

        var store = new WorkspaceStore(_tempDir);
        var workspace = Workspace.Load(_tempDir, store);

        var currentModified = File.GetLastWriteTimeUtc(requestFile);
        Assert.That(currentModified, Is.EqualTo(originalModified));

        var metadata = store.LoadMetadata();
        Assert.That(metadata.RequestsFormatVersion, Is.EqualTo(1));
    }

    [Test]
    public void Workspace_Load_WithMixedFormatRequests_MigratesOnlyOldOnes()
    {
        var requestsDir = Path.Combine(_tempDir, "Requests");
        Directory.CreateDirectory(requestsDir);

        var oldGuid = Guid.NewGuid();
        var newGuid = Guid.NewGuid();

        var oldFormatJson = $$$"""
            {
              "Request": {
                "Id": "{{{oldGuid}}}",
                "UserName": "user",
                "UserEmail": "user@example.org",
                "CreatedUtc": "2024-01-01T12:00:00.0000000Z",
                "Commit": "abc123",
                "GitReference": {"Branch": "main", "Commit": "parent"},
                "TestFilters": "tests"
              },
              "ChainDiffs": [
                {
                  "Status": "RootTriggered",
                  "ReferenceRoot": {"JobName": "MainBuild", "BuildNumber": 100},
                  "OnDemandRoot": {"Queued": {"Key": "OnDemandBuild", "Value": "abc123"}},
                  "TestBuildDiffs": [
                    {
                      "ReferenceBuild": {"Pending": "MainTest"},
                      "OnDemandBuild": {"Pending": "OnDemandTest"},
                      "TestDuration": "00:00:00"
                    }
                  ]
                }
              ]
            }
            """;

        var newFormatJson = $$$"""
            {
              "FormatVersion": 1,
              "Request": {
                "Id": "{{{newGuid}}}",
                "UserName": "user",
                "UserEmail": "user@example.org",
                "CreatedUtc": "2024-01-01T12:00:00.0000000Z",
                "Commit": "def456",
                "GitReference": {"Branch": "main", "Commit": "parent"},
                "TestFilters": "tests"
              },
              "ChainDiffs": [
                {
                  "Status": "RootTriggered",
                  "BaselineRoot": {"JobName": "MainBuild", "BuildNumber": 100},
                  "OnDemandRoot": {"Queued": {"Key": "OnDemandBuild", "Value": "def456"}},
                  "TestBuildDiffs": [
                    {
                      "BaselineBuild": {"Pending": "MainTest"},
                      "OnDemandBuild": {"Pending": "OnDemandTest"},
                      "TestDuration": "00:00:00"
                    }
                  ]
                }
              ]
            }
            """;

        var oldFile = Path.Combine(requestsDir, $"{oldGuid}.json");
        var newFile = Path.Combine(requestsDir, $"{newGuid}.json");
        File.WriteAllText(oldFile, oldFormatJson);
        File.WriteAllText(newFile, newFormatJson);

        var store = new WorkspaceStore(_tempDir);
        var workspace = Workspace.Load(_tempDir, store);

        var oldContent = File.ReadAllText(oldFile);
        Assert.That(oldContent, Does.Contain("\"FormatVersion\": 1"));
        Assert.That(oldContent, Does.Contain("\"BaselineRoot\""));

        var newContent = File.ReadAllText(newFile);
        Assert.That(newContent, Does.Contain("\"FormatVersion\": 1"));
        Assert.That(newContent, Does.Contain("\"BaselineRoot\""));
    }

    [Test]
    public void Workspace_Load_SecondTime_DoesNotMigrateAgain()
    {
        var requestsDir = Path.Combine(_tempDir, "Requests");
        Directory.CreateDirectory(requestsDir);

        var oldFormatJson = """
            {
              "Request": {
                "Id": "12345678-1234-1234-1234-123456789abc",
                "UserName": "user",
                "UserEmail": "user@example.org",
                "CreatedUtc": "2024-01-01T12:00:00.0000000Z",
                "Commit": "abc123",
                "GitReference": {"Branch": "main", "Commit": "parent"},
                "TestFilters": "tests"
              },
              "ChainDiffs": [
                {
                  "Status": "RootTriggered",
                  "ReferenceRoot": {"JobName": "MainBuild", "BuildNumber": 100},
                  "OnDemandRoot": {"Queued": {"Key": "OnDemandBuild", "Value": "abc123"}},
                  "TestBuildDiffs": [
                    {
                      "ReferenceBuild": {"Pending": "MainTest"},
                      "OnDemandBuild": {"Pending": "OnDemandTest"},
                      "TestDuration": "00:00:00"
                    }
                  ]
                }
              ]
            }
            """;

        var requestFile = Path.Combine(requestsDir, "12345678-1234-1234-1234-123456789abc.json");
        File.WriteAllText(requestFile, oldFormatJson);

        var store = new WorkspaceStore(_tempDir);
        var workspace1 = Workspace.Load(_tempDir, store);

        var modifiedAfterFirstLoad = File.GetLastWriteTimeUtc(requestFile);
        Thread.Sleep(100);

        var workspace2 = Workspace.Load(_tempDir, store);

        var modifiedAfterSecondLoad = File.GetLastWriteTimeUtc(requestFile);
        Assert.That(modifiedAfterSecondLoad, Is.EqualTo(modifiedAfterFirstLoad));
    }
}
