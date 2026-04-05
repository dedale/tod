using CommandLine;
using NUnit.Framework;

namespace Tod.Tests;

[TestFixture]
internal sealed class OptionsTests
{
    [Test]
    public void BaseOptions_DebugParameter_ParsesCorrectly()
    {
        var args = new[]
        {
            "sync",
            "--config", "test.json",
            "--workspace", "./ws",
            "--jenkins-token", "token123",
            "--debug"
        };

        var result = Parser.Default.ParseArguments<SyncOptions>(args);

        result.WithParsed<SyncOptions>(opts =>
        {
            Assert.That(opts.Debug, Is.True);
        });
    }

    [Test]
    public void BaseOptions_WithoutDebugParameter_DefaultsToFalse()
    {
        var args = new[]
        {
            "sync",
            "--config", "test.json",
            "--workspace", "./ws",
            "--jenkins-token", "token123"
        };

        var result = Parser.Default.ParseArguments<SyncOptions>(args);

        result.WithParsed<SyncOptions>(opts =>
        {
            Assert.That(opts.Debug, Is.False);
        });
    }

    [Test]
    public void SyncOptions_WithServiceUser_ParsesCorrectly()
    {
        var args = new[]
        {
            "sync",
            "--config", "test.json",
            "--workspace", "./ws",
            "--jenkins-token", "token123",
            "--service-user", "jenkins-bot"
        };

        var result = Parser.Default.ParseArguments<SyncOptions, NewOptions>(args);

        result.WithParsed<SyncOptions>(opts =>
        {
            Assert.That(opts.ConfigPath, Is.EqualTo("test.json"));
            Assert.That(opts.WorkspaceDir, Is.EqualTo("./ws"));
            Assert.That(opts.JenkinsToken, Is.EqualTo("token123"));
            Assert.That(opts.ServiceUser, Is.EqualTo("jenkins-bot"));
        });
    }

    [Test]
    public void SyncOptions_WithoutServiceUser_ParsesCorrectly()
    {
        var args = new[]
        {
            "sync",
            "--config", "test.json",
            "--workspace", "./ws",
            "--jenkins-token", "token123"
        };

        var result = Parser.Default.ParseArguments<SyncOptions, NewOptions>(args);

        result.WithParsed<SyncOptions>(opts =>
        {
            Assert.That(opts.ServiceUser, Is.Null);
        });
    }

    [Test]
    public void NewOptions_WithServiceUser_ParsesCorrectly()
    {
        var args = new[]
        {
            "new",
            "--config", "test.json",
            "--workspace", "./ws",
            "--branch", "main",
            "--root-filters", "build",
            "--test-filters", "unit",
            "--user", "john.doe",
            "--domain", "CORP",
            "--email", "john.doe@example.org",
            "--jenkins-user", "jenkins-bot",
            "--jenkins-token", "jtoken",
            "--gerrit-user", "gerrit-bot",
            "--gerrit-token", "gtoken",
        };

        var result = Parser.Default.ParseArguments<SyncOptions, NewOptions>(args);

        result.WithParsed<NewOptions>(opts =>
        {
            Assert.That(opts.User, Is.EqualTo("john.doe"));
            Assert.That(opts.UserDomain, Is.EqualTo("CORP"));
            Assert.That(opts.UserMail, Is.EqualTo("john.doe@example.org"));
            Assert.That(opts.JenkinsUser, Is.EqualTo("jenkins-bot"));
            Assert.That(opts.JenkinsToken, Is.EqualTo("jtoken"));
            Assert.That(opts.GerritUser, Is.EqualTo("gerrit-bot"));
            Assert.That(opts.GerritToken, Is.EqualTo("gtoken"));
        });
    }

    [Test]
    public void NewOptions_WithoutServiceUser_ParsesCorrectly()
    {
        var args = new[]
        {
            "new",
            "--config", "test.json",
            "--workspace", "./ws",
            "--root-filters", "build",
            "--test-filters", "unit",
            "--jenkins-token", "jtoken",
            "--gerrit-token", "gtoken"
        };

        var result = Parser.Default.ParseArguments<SyncOptions, NewOptions>(args);

        result.WithParsed<NewOptions>(opts =>
        {
            Assert.That(opts.JenkinsUser, Is.Null);
            Assert.That(opts.GerritUser, Is.Null);
            Assert.That(opts.User, Is.Null);
        });
    }

    [Test]
    public void NewOptions_UserAndServiceUserIndependent()
    {
        var args = new[]
        {
            "new",
            "--config", "test.json",
            "--workspace", "./ws",
            "--root-filters", "build",
            "--test-filters", "unit",
            "--jenkins-token", "jtoken",
            "--gerrit-token", "gtoken",
            "--user", "developer"
        };

        var result = Parser.Default.ParseArguments<SyncOptions, NewOptions>(args);

        result.WithParsed<NewOptions>(opts =>
        {
            Assert.That(opts.User, Is.EqualTo("developer"));
            Assert.That(opts.JenkinsUser, Is.Null);
            Assert.That(opts.GerritUser, Is.Null);
        });
    }

    [Test]
    public void SyncOptions_WithJobs_ParsesCorrectly()
    {
        var args = new[]
        {
            "sync",
            "--config", "test.json",
            "--workspace", "./ws",
            "--jenkins-token", "token123",
            "--jobs"
        };

        var result = Parser.Default.ParseArguments<SyncOptions, NewOptions>(args);

        result.WithParsed<SyncOptions>(opts =>
        {
            Assert.That(opts.Jobs, Is.True);
        });
    }

    [Test]
    public void JobsOptions_WithAllParameters_ParsesCorrectly()
    {
        var args = new[]
        {
            "jobs",
            "--config", "test.json",
            "--workspace", "./ws",
            "--branch", "develop",
            "--root-filters", "build",
            "--test-filters", "unit", "integration",
            "--commits", "abc123", "def456"
        };

        var result = Parser.Default.ParseArguments<JobsOptions>(args);

        result.WithParsed<JobsOptions>(opts =>
        {
            Assert.That(opts.ConfigPath, Is.EqualTo("test.json"));
            Assert.That(opts.WorkspaceDir, Is.EqualTo("./ws"));
            Assert.That(opts.BranchName, Is.EqualTo("develop"));
            Assert.That(opts.RootFilters, Is.EquivalentTo(new[] { "build" }));
            Assert.That(opts.TestFilters, Is.EquivalentTo(new[] { "unit", "integration" }));
            Assert.That(opts.Commits, Is.EquivalentTo(new[] { "abc123", "def456" }));
        });
    }

    [Test]
    public void JobsOptions_WithoutOptionalParameters_ParsesCorrectly()
    {
        var args = new[]
        {
            "jobs",
            "--config", "test.json",
            "--workspace", "./ws",
            "--root-filters", "build",
            "--test-filters", "unit"
        };

        var result = Parser.Default.ParseArguments<JobsOptions>(args);

        result.WithParsed<JobsOptions>(opts =>
        {
            Assert.That(opts.BranchName, Is.Null);
            Assert.That(opts.Commits, Is.Empty);
        });
    }

    [Test]
    public void JobsOptions_WithMultipleFilters_ParsesCorrectly()
    {
        var args = new[]
        {
            "jobs",
            "--config", "test.json",
            "--workspace", "./ws",
            "--root-filters", "build", "deploy",
            "--test-filters", "unit", "integration", "e2e"
        };

        var result = Parser.Default.ParseArguments<JobsOptions>(args);

        result.WithParsed<JobsOptions>(opts =>
        {
            Assert.That(opts.RootFilters, Is.EquivalentTo(new[] { "build", "deploy" }));
            Assert.That(opts.TestFilters, Is.EquivalentTo(new[] { "unit", "integration", "e2e" }));
        });
    }

    [Test]
    public void ReportOptions_WithAllParameters_ParsesCorrectly()
    {
        var args = new[]
        {
            "report",
            "--config", "test.json",
            "--workspace", "./ws",
            "--request-id", "12345678-1234-1234-1234-123456789abc",
            "--user", "john.doe"
        };

        var result = Parser.Default.ParseArguments<ReportOptions>(args);

        result.WithParsed<ReportOptions>(opts =>
        {
            Assert.That(opts.ConfigPath, Is.EqualTo("test.json"));
            Assert.That(opts.WorkspaceDir, Is.EqualTo("./ws"));
            Assert.That(opts.RequestId, Is.EqualTo("12345678-1234-1234-1234-123456789abc"));
            Assert.That(opts.User, Is.EqualTo("john.doe"));
        });
    }

    [Test]
    public void ReportOptions_WithoutOptionalUser_ParsesCorrectly()
    {
        var args = new[]
        {
            "report",
            "--config", "test.json",
            "--workspace", "./ws",
            "--request-id", "12345678-1234-1234-1234-123456789abc"
        };

        var result = Parser.Default.ParseArguments<ReportOptions>(args);

        result.WithParsed<ReportOptions>(opts =>
        {
            Assert.That(opts.User, Is.Null);
        });
    }

    [Test]
    public void ListOptions_WithAllParameter_ParsesCorrectly()
    {
        var args = new[]
        {
            "list",
            "--config", "test.json",
            "--workspace", "./ws",
            "--all",
            "--user", "jane.smith"
        };

        var result = Parser.Default.ParseArguments<ListOptions>(args);

        result.WithParsed<ListOptions>(opts =>
        {
            Assert.That(opts.ConfigPath, Is.EqualTo("test.json"));
            Assert.That(opts.WorkspaceDir, Is.EqualTo("./ws"));
            Assert.That(opts.All, Is.True);
            Assert.That(opts.User, Is.EqualTo("jane.smith"));
        });
    }

    [Test]
    public void ListOptions_WithoutAllParameter_DefaultsToFalse()
    {
        var args = new[]
        {
            "list",
            "--config", "test.json",
            "--workspace", "./ws"
        };

        var result = Parser.Default.ParseArguments<ListOptions>(args);

        result.WithParsed<ListOptions>(opts =>
        {
            Assert.That(opts.All, Is.False);
            Assert.That(opts.User, Is.Null);
        });
    }

    [Test]
    public void AbortOptions_WithAllParameters_ParsesCorrectly()
    {
        var args = new[]
        {
            "abort",
            "--config", "test.json",
            "--workspace", "./ws",
            "--request-id", "87654321-4321-4321-4321-abcdef123456",
            "--user", "bob.builder"
        };

        var result = Parser.Default.ParseArguments<AbortOptions>(args);

        result.WithParsed<AbortOptions>(opts =>
        {
            Assert.That(opts.ConfigPath, Is.EqualTo("test.json"));
            Assert.That(opts.WorkspaceDir, Is.EqualTo("./ws"));
            Assert.That(opts.RequestId, Is.EqualTo("87654321-4321-4321-4321-abcdef123456"));
            Assert.That(opts.User, Is.EqualTo("bob.builder"));
        });
    }

    [Test]
    public void AbortOptions_WithoutOptionalUser_ParsesCorrectly()
    {
        var args = new[]
        {
            "abort",
            "--config", "test.json",
            "--workspace", "./ws",
            "--request-id", "87654321-4321-4321-4321-abcdef123456"
        };

        var result = Parser.Default.ParseArguments<AbortOptions>(args);

        result.WithParsed<AbortOptions>(opts =>
        {
            Assert.That(opts.User, Is.Null);
        });
    }

    [Test]
    public void FiltersOptions_ParsesCorrectly()
    {
        var args = new[]
        {
            "filters",
            "--config", "test.json",
            "--workspace", "./ws"
        };

        var result = Parser.Default.ParseArguments<FiltersOptions>(args);

        result.WithParsed<FiltersOptions>(opts =>
        {
            Assert.That(opts.ConfigPath, Is.EqualTo("test.json"));
            Assert.That(opts.WorkspaceDir, Is.EqualTo("./ws"));
        });
    }

    [Test]
    public void FiltersOptions_WithDebug_ParsesCorrectly()
    {
        var args = new[]
        {
            "filters",
            "--config", "test.json",
            "--workspace", "./ws",
            "--debug"
        };

        var result = Parser.Default.ParseArguments<FiltersOptions>(args);

        result.WithParsed<FiltersOptions>(opts =>
        {
            Assert.That(opts.Debug, Is.True);
        });
    }

    [Test]
    public void Parser_WithInvalidVerb_ReturnsError()
    {
        var args = new[]
        {
            "invalid-command",
            "--config", "test.json"
        };

        var result = Parser.Default.ParseArguments<SyncOptions, NewOptions, JobsOptions, ReportOptions, ListOptions, AbortOptions, FiltersOptions>(args);

        Assert.That(result.Tag, Is.EqualTo(ParserResultType.NotParsed));
    }

    [Test]
    public void Parser_WithMissingRequiredParameter_ReturnsError()
    {
        var args = new[]
        {
            "sync",
            "--config", "test.json"
            // Missing --workspace and --jenkins-token
        };

        var result = Parser.Default.ParseArguments<SyncOptions>(args);

        Assert.That(result.Tag, Is.EqualTo(ParserResultType.NotParsed));
    }
}
