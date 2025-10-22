using LibGit2Sharp;
using Moq;
using NUnit.Framework;
using Tod.Git;
using Tod.Tests.Jenkins;

namespace Tod.Tests.Git;

[TestFixture]
internal sealed class GitRepoTests
{
    private static readonly string ValidGitRepoPath = Guid.NewGuid().ToString();

    private static string? DiscoverOne(string? _)
    {
        return ValidGitRepoPath;
    }

    private static string? DiscoverNone(string? _)
    {
        return null;
    }

    private static Mock<IRepository> NewMockRepository()
    {
        var mockRepo = new Mock<IRepository>(MockBehavior.Strict);
        mockRepo.Setup(r => r.Dispose());
        return mockRepo;
    }

    private static Commit NewMockCommit(string sha)
    {
        var mockCommit = new Mock<Commit>();
        mockCommit.Setup(c => c.Sha).Returns(sha);
        return mockCommit.Object;
    }

    [Test]
    public void Constructor_WithValidDirectory_InitializesSuccessfully()
    {
        var mockRepo = NewMockRepository();
        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            Assert.That(gitRepo, Is.Not.Null);
        }
        mockRepo.VerifyAll();
    }

    [Test]
    public void Constructor_DiscoverReturnsNull_ThrowsArgumentException()
    {
        var mockRepo = new Mock<IRepository>(MockBehavior.Strict);

        Assert.That(() => new GitRepo(".", DiscoverNone, _ => throw new NotImplementedException()),
            Throws.ArgumentException.With.Message.Contains("No git repository found"));
    }

    [Test]
    public void Head_ReturnsCurrentHeadCommit()
    {
        var expectedSha = RandomData.NextSha1();
        var mockCommit = new Mock<Commit>();
        mockCommit.Setup(c => c.Sha).Returns(expectedSha.Value);

        var mockBranch = new Mock<Branch>();
        mockBranch.Setup(b => b.Tip).Returns(mockCommit.Object);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Head).Returns(mockBranch.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var head = gitRepo.Head;

            Assert.That(head, Is.EqualTo(expectedSha));
        }
        mockRepo.VerifyAll();
    }

    [Test]
    public void Head_CalledMultipleTimes_ReturnsConsistentValue()
    {
        var expectedSha = "abc123def456";
        var mockCommit = new Mock<Commit>(MockBehavior.Strict);
        mockCommit.Setup(c => c.Sha).Returns(expectedSha);

        var mockBranch = new Mock<Branch>(MockBehavior.Strict);
        mockBranch.Setup(b => b.Tip).Returns(mockCommit.Object);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Head).Returns(mockBranch.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var head1 = gitRepo.Head;
            var head2 = gitRepo.Head;
            var head3 = gitRepo.Head;

            Assert.That(head1, Is.EqualTo(head2));
            Assert.That(head2, Is.EqualTo(head3));
        }
        mockRepo.VerifyAll();
        mockBranch.VerifyAll();
        mockCommit.VerifyAll();
    }

    [Test]
    public void GetLastCommits_WithCount_ReturnsCorrectNumber()
    {
        var sha1s = Enumerable.Range(0, 3).Select(_ => RandomData.NextSha1()).ToArray();
        var commits = sha1s.Select(s => NewMockCommit(s.Value));

        var mockCommitLog = new Mock<IQueryableCommitLog>(MockBehavior.Strict);
        mockCommitLog.Setup(c => c.GetEnumerator()).Returns(commits.GetEnumerator);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Commits).Returns(mockCommitLog.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var result = gitRepo.GetLastCommits(3);

            Assert.That(result, Has.Length.EqualTo(3));
            Assert.That(result[0].Value, Is.EqualTo(sha1s[0].Value));
            Assert.That(result[1].Value, Is.EqualTo(sha1s[1].Value));
            Assert.That(result[2].Value, Is.EqualTo(sha1s[2].Value));
        }
        mockRepo.VerifyAll();
        mockCommitLog.VerifyAll();
    }

    [Test]
    public void GetLastCommits_WithZeroCount_ReturnsEmptyArray()
    {
        var sha1s = Enumerable.Range(0, 2).Select(_ => RandomData.NextSha1()).ToArray();
        var commits = sha1s.Select(s => NewMockCommit(s.Value));

        var mockCommitLog = new Mock<IQueryableCommitLog>(MockBehavior.Strict);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Commits).Returns(mockCommitLog.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var result = gitRepo.GetLastCommits(0);

            Assert.That(result, Is.Empty);
        }
        mockRepo.VerifyAll();
        mockCommitLog.VerifyAll();
    }

    [Test]
    public void GetLastCommits_RequestMoreThanAvailable_ReturnsAllAvailable()
    {
        var sha1s = Enumerable.Range(0, 2).Select(_ => RandomData.NextSha1()).ToArray();
        var commits = sha1s.Select(s => NewMockCommit(s.Value));

        var mockCommitLog = new Mock<IQueryableCommitLog>();
        mockCommitLog.Setup(c => c.GetEnumerator()).Returns(commits.GetEnumerator);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Commits).Returns(mockCommitLog.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var result = gitRepo.GetLastCommits(10);

            Assert.That(result, Has.Length.EqualTo(2));
        }
        mockRepo.VerifyAll();
    }

    [Test]
    public void GetLastCommits_CalledMultipleTimes_ReturnsConsistentResults()
    {
        var sha1s = Enumerable.Range(0, 3).Select(_ => RandomData.NextSha1()).ToArray();
        var commits = sha1s.Select(s => NewMockCommit(s.Value));

        var mockCommitLog = new Mock<IQueryableCommitLog>();
        mockCommitLog.Setup(c => c.GetEnumerator()).Returns(commits.GetEnumerator);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Commits).Returns(mockCommitLog.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var result1 = gitRepo.GetLastCommits(2);
            var result2 = gitRepo.GetLastCommits(2);

            Assert.That(result1, Has.Length.EqualTo(2));
            Assert.That(result2, Has.Length.EqualTo(2));
            Assert.That(result1[0].Value, Is.EqualTo(result2[0].Value));
            Assert.That(result1[1].Value, Is.EqualTo(result2[1].Value));
        }
        mockRepo.VerifyAll();
    }

    [Test]
    public void GetLastCommits_WithOne_ReturnsSingleCommit()
    {
        var sha1s = Enumerable.Range(0, 2).Select(_ => RandomData.NextSha1()).ToArray();
        var commits = sha1s.Select(s => NewMockCommit(s.Value));

        var mockCommitLog = new Mock<IQueryableCommitLog>();
        mockCommitLog.Setup(c => c.GetEnumerator()).Returns(commits.GetEnumerator);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Commits).Returns(mockCommitLog.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var result = gitRepo.GetLastCommits(1);

            Assert.That(result, Has.Length.EqualTo(1));
            Assert.That(result[0], Is.EqualTo(sha1s[0]));
        }
        mockRepo.VerifyAll();
    }

    [Test]
    public void Dispose_DisposesUnderlyingRepository()
    {
        var mockRepo = NewMockRepository();
        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
        }
        mockRepo.VerifyAll();
    }

    [Test]
    public void GetLastCommits_EmptyRepository_ReturnsEmptyArray()
    {
        var commits = Enumerable.Empty<Commit>();

        var mockCommitLog = new Mock<IQueryableCommitLog>();
        mockCommitLog.Setup(c => c.GetEnumerator()).Returns(commits.GetEnumerator);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Commits).Returns(mockCommitLog.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var result = gitRepo.GetLastCommits(5);

            Assert.That(result, Is.Empty);
        }
        mockRepo.VerifyAll();
    }

    [Test]
    public void GetLastCommits_LargeCount_HandlesCorrectly()
    {
        var commits = Enumerable.Range(0, 100).Select(i => NewMockCommit($"sha{i:D3}"));

        var mockCommitLog = new Mock<IQueryableCommitLog>();
        mockCommitLog.Setup(c => c.GetEnumerator()).Returns(commits.GetEnumerator);

        var mockRepo = NewMockRepository();
        mockRepo.Setup(r => r.Commits).Returns(mockCommitLog.Object);

        using (var gitRepo = new GitRepo(".", DiscoverOne, _ => mockRepo.Object))
        {
            var result = gitRepo.GetLastCommits(50);

            Assert.That(result, Has.Length.EqualTo(50));
            Assert.That(result[0].Value, Is.EqualTo("sha000"));
            Assert.That(result[49].Value, Is.EqualTo("sha049"));
        }
        mockRepo.VerifyAll();
    }
}
