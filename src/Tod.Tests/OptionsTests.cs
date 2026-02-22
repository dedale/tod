using CommandLine;
using NUnit.Framework;
using Tod;

namespace Tod.Tests;

[TestFixture]
internal sealed class OptionsTests
{
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
            "--jenkins-token", "jtoken",
            "--gerrit-token", "gtoken",
            "--user", "john.doe",
            "--domain", "CORP",
            "--service-user", "jenkins-bot"
        };

        var result = Parser.Default.ParseArguments<SyncOptions, NewOptions>(args);

        result.WithParsed<NewOptions>(opts =>
        {
            Assert.That(opts.User, Is.EqualTo("john.doe"));
            Assert.That(opts.UserDomain, Is.EqualTo("CORP"));
            Assert.That(opts.ServiceUser, Is.EqualTo("jenkins-bot"));
            Assert.That(opts.JenkinsToken, Is.EqualTo("jtoken"));
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
            Assert.That(opts.ServiceUser, Is.Null);
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
            Assert.That(opts.ServiceUser, Is.Null);
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
}
