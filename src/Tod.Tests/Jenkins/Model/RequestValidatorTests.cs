using Moq;
using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class RequestValidatorTests
{
    private Mock<IJenkinsClient> _jenkinsClient;
    private JenkinsConfig _config;

    [SetUp]
    public void SetUp()
    {
        _jenkinsClient = new Mock<IJenkinsClient>(MockBehavior.Strict);
        _config = JenkinsConfig.New("https://jenkins.example.com");
    }

    [TearDown]
    public void TearDown()
    {
        _jenkinsClient.VerifyAll();
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenNoLoadThresholdsConfigured()
    {
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(100);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.FromHours(2));

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenQueueSizeBelowThreshold()
    {
        var loadThresholds = new[] { new LoadThreshold(50, TimeSpan.FromHours(1)) };
        _config = JenkinsConfig.New("https://jenkins.example.com", loadThresholds: loadThresholds);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(30);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.FromHours(2));

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenDurationBelowThreshold()
    {
        var loadThresholds = new[] { new LoadThreshold(50, TimeSpan.FromHours(3)) };
        _config = JenkinsConfig.New("https://jenkins.example.com", loadThresholds: loadThresholds);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(100);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.FromHours(2));

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Validate_ReturnsFalse_WhenBothQueueSizeAndDurationExceedThreshold()
    {
        var loadThresholds = new[] { new LoadThreshold(50, TimeSpan.FromHours(1)) };
        _config = JenkinsConfig.New("https://jenkins.example.com", loadThresholds: loadThresholds);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(100);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.FromHours(2));

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenOnlyFirstThresholdNotExceeded()
    {
        var loadThresholds = new[]
        {
            new LoadThreshold(200, TimeSpan.FromHours(1)),
            new LoadThreshold(50, TimeSpan.FromHours(5))
        };
        _config = JenkinsConfig.New("https://jenkins.example.com", loadThresholds: loadThresholds);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(100);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.FromHours(2));

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Validate_ReturnsFalse_WhenAnyThresholdExceeded()
    {
        var loadThresholds = new[]
        {
            new LoadThreshold(200, TimeSpan.FromHours(1)),
            new LoadThreshold(50, TimeSpan.FromHours(1))
        };
        _config = JenkinsConfig.New("https://jenkins.example.com", loadThresholds: loadThresholds);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(100);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.FromHours(2));

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenQueueSizeEqualsThreshold()
    {
        var loadThresholds = new[] { new LoadThreshold(100, TimeSpan.FromHours(1)) };
        _config = JenkinsConfig.New("https://jenkins.example.com", loadThresholds: loadThresholds);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(100);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.FromHours(2));

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenDurationEqualsThreshold()
    {
        var loadThresholds = new[] { new LoadThreshold(50, TimeSpan.FromHours(2)) };
        _config = JenkinsConfig.New("https://jenkins.example.com", loadThresholds: loadThresholds);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(100);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.FromHours(2));

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenEmptyChains()
    {
        var loadThresholds = new[] { new LoadThreshold(50, TimeSpan.FromHours(1)) };
        _config = JenkinsConfig.New("https://jenkins.example.com", loadThresholds: loadThresholds);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(100);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = Array.Empty<RequestChain>();

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenMaxUserActiveRequestsNotConfigured()
    {
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(0);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.Zero);

        var result = await validator.Validate(chains, 2);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenUserActivesRequestsBelowMaximum()
    {
        _config = JenkinsConfig.New("https://jenkins.example.com", maxUserActiveRequests: 3);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(0);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.Zero);

        var result = await validator.Validate(chains, 2);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Validate_ReturnsFalse_WhenUserActiveRequestsEqualsMaximum()
    {
        _config = JenkinsConfig.New("https://jenkins.example.com", maxUserActiveRequests: 2);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.Zero);

        var result = await validator.Validate(chains, 2);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Validate_ReturnsFalse_WhenUserActiveRequestsExceedsMaximum()
    {
        _config = JenkinsConfig.New("https://jenkins.example.com", maxUserActiveRequests: 1);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.Zero);

        var result = await validator.Validate(chains, 2);

        Assert.That(result, Is.False);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenOtherUsersHaveActiveRequests()
    {
        _config = JenkinsConfig.New("https://jenkins.example.com", maxUserActiveRequests: 2);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(0);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.Zero);

        var result = await validator.Validate(chains, 1);

        Assert.That(result, Is.True);
    }

    [Test]
    public async Task Validate_ReturnsTrue_WhenUserHasDoneRequests()
    {
        _config = JenkinsConfig.New("https://jenkins.example.com", maxUserActiveRequests: 1);
        _jenkinsClient.Setup(x => x.GetQueueSize()).ReturnsAsync(0);
        var validator = new RequestValidator(_config, _jenkinsClient.Object);
        var chains = CreateRequestChains(TimeSpan.Zero);

        var result = await validator.Validate(chains, 0);

        Assert.That(result, Is.True);
    }

    private static RequestChain[] CreateRequestChains(TimeSpan totalDuration)
    {
        var rootBuild = RandomData.NextRootBuild();
        var referenceRoot = new BuildReference(rootBuild.JobName.Value, rootBuild.BuildNumber);
        var ondemandRoot = RequestRootBuildReference.Queue(
            new JobName("CUSTOM-build"),
            RandomData.NextSha1()
        );

        var testBuildDiff = new RequestBuildDiff(
            new JobName("MAIN-job"),
            new JobName("CUSTOM-job"),
            TimeSpan.FromMinutes(30)
        );

        var chain = new RequestChain(
            referenceRoot,
            ondemandRoot,
            totalDuration,
            [testBuildDiff]
        );

        return [chain];
    }
}
