using Serilog;
using System.Xml.Linq;
using Tod.Net;

namespace Tod.Jenkins;

internal sealed class BaselineReportSender(IJobLinker jobLinker, IMailSender mailSender, bool hideFlakies)
{
    public async Task SendReport(BaselineChainReport report)
    {
        var chainName = report.ChainName == RootFilter.DefaultChain ? "(default)" : report.ChainName;

        var authors = report.RootBuilds
            .SelectMany(rb => rb.CommitAuthors)
            .Select(ca => ca.Email)
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Distinct()
            .ToArray();

        if (authors.Length == 0)
        {
            Log.Warning("No authors found for baseline report on {BranchName} {ChainName}", report.BranchName, chainName);
            return;
        }

        var newFailures = report.TestDiffs.Values
            .SelectMany(diff => diff.Diff.Match(onNotComparable: _ => [], onComparable: d => d.FailedTests.Where(t => t.Newness == Newness.New)))
            .Count();

        if (newFailures == 0)
        {
            Log.Information("No new failures for {BranchName} {ChainName}, skipping report", report.BranchName, chainName);
            return;
        }

        var latestRootBuild = report.RootBuilds[^1];
        var shortJobName = latestRootBuild.JobName.Value.Split('/')[^1];
        var subject = $"{report.BranchName} Build Report {shortJobName} #{latestRootBuild.BuildNumber}";

        var body = BuildEmailBody(report, false);
        var recipients = string.Join(", ", authors);
        var attachment = BuildEmailBody(report, true);

        try
        {
            await mailSender.Send(recipients, subject, body, attachment).ConfigureAwait(false);
            Log.Information("Sent baseline report for {BranchName} {ChainName} to {AuthorCount} author(s): {Authors}",
                report.BranchName, chainName, authors.Length, recipients);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to send baseline report for {BranchName} {ChainName} to {Authors}",
                report.BranchName, chainName, recipients);
        }
    }

    private string BuildEmailBody(BaselineChainReport report, bool full)
    {
        var flakyTests = report.TestDiffs.Values
            .SelectMany(d => d.Diff.Match(onNotComparable: _ => [], onComparable: fd => fd.FailedTests.Where(t => t.IsFlaky).Select(t => t.Test.ErrorDetails)))
            .ToHashSet();

        var newFailures = report.TestDiffs.Values
            .SelectMany(diff => diff.Diff.Match(onNotComparable: _ => [], onComparable: d => d.FailedTests.Where(ContainsNewErrors)))
            .Count();

        var totalFailures = report.TestDiffs.Values
            .SelectMany(diff => diff.Diff.Match(onNotComparable: _ => [], onComparable: d => (IEnumerable<FailedTestResult>)d.FailedTests))
            .Count();

        var doc = new XDocument(
            new XElement("html",
                TodReports.GetHead(),
                new XElement("body",
                    new XElement("h1",
                        new XElement("span", new XAttribute("style", "color:red"), "Build Report")),
                    new XElement("table",
                        new XAttribute("class", "summary"),
                        new XElement("tr",
                            new XElement("th", "🌿 Branch"),
                            new XElement("td", report.BranchName)),
                        new XElement("tr",
                            new XElement("th", "⚙ Chain"),
                            new XElement("td", report.ChainName == RootFilter.DefaultChain ? "(default)" : report.ChainName)),
                        new XElement("tr",
                            new XElement("th", "🏗 Root Builds"),
                            new XElement("td", report.RootBuilds.Select((rb, i) => new object[]
                            {
                                i > 0 ? (object)", " : "",
                                new XElement("a",
                                    new XAttribute("href", jobLinker.GetUrl(rb.JobName, rb.BuildNumber)),
                                    $"#{rb.BuildNumber} ({(rb.IsSuccessful ? "✅" : "❌")})")
                            }))),
                        new XElement("tr",
                            new XElement("th", "📝 Commits"),
                            new XElement("td", string.Join(", ", report.RootBuilds
                                .SelectMany(rb => rb.Commits)
                                .Distinct()
                                .Select(c => c.Value[..8])
                            ))),
                        new XElement("tr",
                            new XElement("th", "👥 Authors"),
                            new XElement("td", string.Join(", ", report.RootBuilds
                                .SelectMany(rb => rb.CommitAuthors)
                                .Select(ca => ca.Name)
                                .Distinct()
                            ))),
                        new XElement("tr",
                            new XElement("th", "🔴 New Failures"),
                            new XElement("td", $"{newFailures} / {totalFailures}"))),
                    full ? report.TestDiffs
                        .Where(kvp => kvp.Value.Diff.Match(onNotComparable: _ => false, onComparable: d => d.FailedTests.Any(ContainsNewErrors)))
                        .Select(kvp => GetTestDiffElement(kvp.Key, kvp.Value, ContainsNewErrors)) : null
                )
            )
        );

        return doc.ToString();

        bool ContainsNewErrors(FailedTestResult testResult)
        {
            return testResult.Newness == Newness.New
                && (!hideFlakies || !testResult.IsFlaky && !flakyTests.Contains(testResult.Test.ErrorDetails));
        }
    }

    private XElement GetTestDiffElement(JobName testJob, BuildDiffResult buildDiffResult, Func<FailedTestResult, bool> containsNewErrors)
    {
        return new XElement("table",
            new XAttribute("class", "tests"),
            new XElement("tr",
                new XElement("th",
                    new XAttribute("colspan", "2"),
                    new XAttribute("class", "build-header")),
                GetLink(buildDiffResult.Result, "🧪"),
                " vs ",
                GetLink(buildDiffResult.Baseline, "📘")),
            buildDiffResult.Diff.Match(
                onNotComparable: _ => null,
                onComparable: diff => (object?)diff.FailedTests
                    .Where(containsNewErrors)
                    .Select(t => new XElement("tr",
                        new XElement("td",
                            new XAttribute("class", "test-info"),
                            t.IsFlaky ? "🟠" : "🔴",
                            t.IsFlaky ? new XElement("span",
                                new XAttribute("class", "label unstable"),
                                "flaky") : null,
                            t.Newness == Newness.New ? new XElement("span",
                                new XAttribute("class", "label new"),
                                "new") : null),
                        new XElement("td",
                            new XAttribute("class", "test-name"),
                            new XElement("span",
                                new XAttribute("style", "color:#2a5db0;"),
                                new XAttribute("title", $"{t.Test.ClassName}.{t.Test.TestName}"),
                                $"{t.Test.ClassName}.{t.Test.TestName}"),
                            new XElement("pre",
                                TodReports.Shorten(t.Test.ErrorDetails)))))
            )
        );
    }

    private IEnumerable<object> GetLink(BuildReferenceResult result, string emoji)
    {
        yield return emoji;
        yield return new XElement("a",
            new XAttribute("href", jobLinker.GetUrl(result)),
            result.Id);
    }
}
