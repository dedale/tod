using Serilog;
using System.Text.RegularExpressions;
using Tod.Git;

namespace Tod.Jenkins;

internal interface IJenkinsClient
{
    Task<Build[]> GetLastBuilds(JobName jobName, int count = 100);
    Task<JobName[]> GetScheduledJobs(BuildReference buildReference);
    Task<int> GetFailCount(BuildReference buildReference);
    Task<TestBuildData> GetTestData(BuildReference buildReference);
    Task<FailedTest[]> GetFailedTests(BuildReference buildReference);
    Task<int> TriggerBuild(JobName jobName, Sha1 commit, int retryDelayMs = 2000);
    Task<BuildReference?> TryGetRootBuild(BuildReference buildReference);
    Task<JobName[]> GetJobNames(string[] multiBranchFolders);
}

internal sealed class JenkinsClient(JenkinsConfig config, string userToken, IApiClient? apiClient = null) : IJenkinsClient, IDisposable
{
    private readonly IApiClient _apiClient = apiClient ?? new ApiClient(userToken);

    public async Task<Build[]> GetLastBuilds(JobName jobName, int count = 100)
    {
        var url = $"{config.Url}/{jobName.UrlPath}/api/json?tree=builds[id,number,result,timestamp,duration,building,changeSets[items[commitId]]]{{0,{count}}}";
        var doc = await _apiClient.GetAsync(url).ConfigureAwait(false);
        var builds = new List<Build>();
        foreach (var buildElement in doc.RootElement.GetProperty("builds").EnumerateArray())
        {
            bool isBuilding = buildElement.GetProperty("building").GetBoolean();
            if (isBuilding)
            {
                continue;
            }
            builds.Add(Build.FromJson(buildElement));
        }
        return [.. builds];
    }

    private static readonly Regex s_scheduling = new(@"Scheduling project:\s+(?<project>.*)", RegexOptions.Compiled);

    public async Task<JobName[]> GetScheduledJobs(BuildReference buildReference)
    {
        var logUrl = $"{config.Url}/{buildReference.JobName.UrlPath}/{buildReference.BuildNumber}/consoleText";
        var logText = await _apiClient.GetStringAsync(logUrl).ConfigureAwait(false);
        var scheduledJobs = new List<JobName>();
        foreach (var line in logText.Split('\n'))
        {
            var match = s_scheduling.Match(line.Trim('\r'));
            if (match.Success)
            {
                var project = match.Groups["project"].Value;
                project = project.Replace(" » ", "/");
                scheduledJobs.Add(new JobName(project));
            }
        }
        return [.. scheduledJobs];
    }

    private static readonly Regex s_triggering = new(@"Triggering a new build of\s+(?<jobName>.*)\s+#(?<buildNumber>\d+)", RegexOptions.Compiled);

    // Not reliable, it is better to analyze causes in triggered builds. Moreover, two different builds can trigger the same one...
    public async Task<BuildReference[]> GetTriggeredBuilds(BuildReference buildReference)
    {
        var logUrl = $"{config.Url}/{buildReference.JobName.UrlPath}/{buildReference.BuildNumber}/consoleText";
        var logText = await _apiClient.GetStringAsync(logUrl).ConfigureAwait(false);
        var triggeredBuilds = new List<BuildReference>();
        foreach (var line in logText.Split('\n'))
        {
            var match = s_triggering.Match(line);
            if (match.Success)
            {
                var triggeredJobName = match.Groups["jobName"].Value;
                var triggeredBuildNumber = int.Parse(match.Groups["buildNumber"].Value);
                triggeredBuilds.Add(new BuildReference(new JobName(triggeredJobName), triggeredBuildNumber));
            }
        }
        return [.. triggeredBuilds];
    }

    public async Task<int> GetFailCount(BuildReference buildReference)
    {
        var url = $"{config.Url}/{buildReference.JobName.UrlPath}/{buildReference.BuildNumber}/api/json?tree=actions[failCount]";
        var doc = await _apiClient.GetAsync(url).ConfigureAwait(false);
        foreach (var action in doc.RootElement.GetProperty("actions").EnumerateArray())
        {
            if (action.TryGetProperty("failCount", out var failCount))
            {
                return failCount.GetInt32();
            }
        }
        return 0;
    }

    public async Task<TestBuildData> GetTestData(BuildReference buildReference)
    {
        var url = $"{config.Url}/{buildReference.JobName.UrlPath}/{buildReference.BuildNumber}/api/json?tree=actions[failCount,causes[upstreamBuild,upstreamProject]]";
        var doc = await _apiClient.GetAsync(url).ConfigureAwait(false);
        int? failCount = null;
        var upstreamBuilds = new List<BuildReference>();
        foreach (var action in doc.RootElement.GetProperty("actions").EnumerateArray())
        {
            if (action.TryGetProperty("failCount", out var failCountProperty))
            {
                failCount = failCountProperty.GetInt32();
            }
            if (action.TryGetProperty("causes", out var causesProperty))
            {
                foreach (var cause in causesProperty.EnumerateArray())
                {
                    if (cause.TryGetProperty("upstreamProject", out var upstreamProjectProperty) &&
                        cause.TryGetProperty("upstreamBuild", out var upstreamBuildProperty))
                    {
                        var upstreamJobName = new JobName(upstreamProjectProperty.GetString()!);
                        var upstreamBuildNumber = upstreamBuildProperty.GetInt32();
                        upstreamBuilds.Add(new BuildReference(upstreamJobName, upstreamBuildNumber));
                    }
                }
            }
            if (failCount.HasValue && upstreamBuilds.Count > 0)
            {
                break;
            }
        }
        return new TestBuildData(failCount ?? 0, [.. upstreamBuilds]);
    }

    public async Task<FailedTest[]> GetFailedTests(BuildReference buildReference)
    {
        var url = $"{config.Url}/{buildReference.JobName.UrlPath}/{buildReference.BuildNumber}/testReport/api/json";
        var doc = await _apiClient.GetAsync(url).ConfigureAwait(false);
        var failedTests = new List<FailedTest>();
        foreach (var suite in doc.RootElement.GetProperty("suites").EnumerateArray())
        {
            foreach (var testCase in suite.GetProperty("cases").EnumerateArray())
            {
                if (testCase.GetProperty("status").GetString() == "FAILED")
                {
                    string className = testCase.GetProperty("className").GetString()!;
                    string testName = testCase.GetProperty("name").GetString()!;
                    string errorDetails = testCase.GetProperty("errorDetails").GetString()!;
                    //string stackTrace = testCase.GetProperty("errorStackTrace").GetString()!;
                    failedTests.Add(new FailedTest(className, testName, errorDetails));
                }
            }
        }
        return [.. failedTests];
    }

    public async Task<int> TriggerBuild(JobName jobName, Sha1 commit, int retryDelayMs = 2000)
    {
        Log.Information("Triggering {JobName} build with commit {Commit}", jobName, commit);
        var crumbUrl = $"{config.Url}/crumbIssuer/api/json";
        var triggerUrl = $"{config.Url}/{jobName.UrlPath}/buildWithParameters?BUILD_REF_SPEC={Uri.EscapeDataString(commit.Value)}";
        var location = await _apiClient.PostAsync(crumbUrl, triggerUrl).ConfigureAwait(false);
        if (!location.Contains("/queue/item/"))
        {
            throw new InvalidOperationException("Missing queue local header in response");
        }
        string queueUrl = location + "api/json";
        for (int i = 0; i < 30; i++)
        {
            await Task.Delay(retryDelayMs).ConfigureAwait(false);
            var queueDoc = await _apiClient.GetAsync(queueUrl).ConfigureAwait(false);
            if (queueDoc.RootElement.TryGetProperty("executable", out var executable))
            {
                var buildNumber = executable.GetProperty("number").GetInt32();
                Log.Information("Triggered {JobName} #{BuildNumber}", jobName, buildNumber);
                return buildNumber;
            }
        }
        throw new TimeoutException("Timed out waiting for build to be scheduled.");
    }

    private static readonly Regex s_copiedArtifacts = new(@"Copied \d+ artifacts from \""(?<jobName>.*)\"" build number (?<buildNumber>\d+)", RegexOptions.Compiled);

    public async Task<BuildReference?> TryGetRootBuild(BuildReference buildReference)
    {
        var logUrl = $"{config.Url}/{buildReference.JobName.UrlPath}/{buildReference.BuildNumber}/consoleText";
        var logText = await _apiClient.GetStringAsync(logUrl).ConfigureAwait(false);
        foreach (var line in logText.Split('\n'))
        {
            var match = s_copiedArtifacts.Match(line);
            if (match.Success)
            {
                var rootJobName = match.Groups["jobName"].Value;
                var rootBuildNumber = int.Parse(match.Groups["buildNumber"].Value);
                return new BuildReference(new JobName(rootJobName), rootBuildNumber);
            }
        }
        return null;
    }

    private async Task<JobName[]> GetJobNames(string jobPath)
    {
        var url = $"{config.Url}/{jobPath}api/json?tree=jobs[name]";
        var doc = await _apiClient.GetAsync(url).ConfigureAwait(false);
        return [.. doc.RootElement.GetProperty("jobs").EnumerateArray().Select(e => new JobName(e.GetProperty("name").ToString()))];
    }

    public async Task<JobName[]> GetJobNames(string[] multiBranchFolders)
    {
        var allJobNames = new List<JobName>();
        allJobNames.AddRange(await GetJobNames(string.Empty).ConfigureAwait(false));
        foreach (var folder in multiBranchFolders)
        {
            var jobPath = $"job/{string.Join("/job/", folder.Trim('/').Split('/'))}/";
            var jobNames = await GetJobNames(jobPath).ConfigureAwait(false);
            allJobNames.AddRange(jobNames.Select(j => new JobName(string.Concat(folder.Trim('/'), "/", j.Value))));
        }
        allJobNames.Sort();
        return [.. allJobNames];
    }

    public void Dispose()
    {
        _apiClient.Dispose();
    }

#if false

    public async Task GetTestBuilds2()
    {
        string jenkinsUrl = "http://your-jenkins-url";
        string jobName = "MAIN-build";
        int buildNumber = 123;

        var handler = new HttpClientHandler
        {
            UseDefaultCredentials = true // Uses Windows identity
        };

        using var client = new HttpClient(handler);
        var apiUrl = $"{jenkinsUrl}/{jobName.UrlPath}/{buildNumber}/api/json";
        var logUrl = ...;

        var triggeredBuilds = new List<(string job, int number, string url)>();

        try
        {
            var response = await client.GetAsync(apiUrl);
            response.EnsureSuccessStatusCode();
            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            bool foundViaApi = false;

            // Check for subBuilds
            foreach (var action in doc.RootElement.GetProperty("actions").EnumerateArray())
            {
                if (action.TryGetProperty("subBuilds", out var subBuilds))
                {
                    foreach (var subBuild in subBuilds.EnumerateArray())
                    {
                        string job = subBuild.GetProperty("jobName").GetString();
                        int number = subBuild.GetProperty("buildNumber").GetInt32();
                        string url = subBuild.GetProperty("url").GetString();
                        triggeredBuilds.Add((job, number, url));
                    }
                    foundViaApi = true;
                    break;
                }
            }

            // Fallback: parse console log
            if (!foundViaApi)
            {
                ...
            }

            // Output results
            Console.WriteLine("Triggered Builds:");
            foreach (var build in triggeredBuilds)
            {
                Console.WriteLine($"- {build.job} #{build.number} → {build.url}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }
    }

    public async Task GetParentCommit()
    {
        string gerritApiUrl = "https://your-gerrit-url/a/projects/your-project/commits/";
        string commitSha = "abc123..."; // SHA1 of the ref spec

        using var client = new HttpClient();
        var url = $"{gerritApiUrl}{commitSha}";

        var response = await client.GetAsync(url);
        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync();
        var json = rawJson.TrimStart(")]}'\n".ToCharArray()); // Gerrit prepends anti-XSSI chars

        var doc = JsonDocument.Parse(json);
        var parentSha = doc.RootElement.GetProperty("parents")[0].GetProperty("commit").GetString();

        Console.WriteLine($"Parent SHA1: {parentSha}");
    }
#endif

}
