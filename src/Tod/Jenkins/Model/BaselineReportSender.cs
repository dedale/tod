using Serilog;
using System.Xml.Linq;
using Tod.Net;

namespace Tod.Jenkins;

internal sealed class BaselineReportSender(IMailSender mailSender)
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
            Log.Warning("No authors found for reference report on {BranchName} {ChainName}", report.BranchName, chainName);
            return;
        }

        var newFailures = report.TestDiffs.Values
            .SelectMany(diff => diff.FailedTests.Where(t => t.Newness == Newness.New))
            .Count();

        if (newFailures == 0)
        {
            Log.Information("No new failures for {BranchName} {ChainName}, skipping report", report.BranchName, chainName);
            return;
        }

        var body = BuildEmailBody(report, false);
        var attachment = BuildEmailBody(report, true);

        foreach (var author in authors)
        {
            try
            {
                var subject = $"{report.BranchName} Build Report{(report.ChainName.Length > 0 ? $": {report.ChainName}" : "")}";
                await mailSender.Send(author!, subject, body, attachment)
                    .ConfigureAwait(false);
                Log.Information("Sent reference report for {BranchName} {ChainName} to {Author}",
                    report.BranchName, chainName, authors.Length, author);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to send reference report for {BranchName} {ChainName} to {Author}",
                    report.BranchName, chainName, author);
            }
        }

        Log.Information("Sent reference report for {BranchName} {ChainName} to {AuthorCount} author(s)",
            report.BranchName, chainName, authors.Length);
    }

    private string BuildEmailBody(BaselineChainReport report, bool full)
    {
        var newFailures = report.TestDiffs.Values
            .SelectMany(diff => diff.FailedTests.Where(t => t.Newness == Newness.New))
            .Count();

        var totalFailures = report.TestDiffs.Values
            .SelectMany(diff => diff.FailedTests)
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
                            new XElement("th", "🌿 Reference branch"),
                            new XElement("td", report.BranchName)),
                        new XElement("tr",
                            new XElement("th", "⚙ Chain"),
                            new XElement("td", report.ChainName == RootFilter.DefaultChain ? "(default)" : report.ChainName)),
                        new XElement("tr",
                            new XElement("th", "🏗 Root Builds"),
                            new XElement("td", string.Join(", ", report.RootBuilds.Select(rb =>
                                $"#{rb.BuildNumber} ({(rb.IsSuccessful ? "✅" : "❌")})"
                            )))),
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
                        .Where(kvp => kvp.Value.FailedTests.Any(t => t.Newness == Newness.New))
                        .Select(kvp => GetTestDiffElement(kvp.Key, kvp.Value)) : null
                )
            )
        );

        return doc.ToString();
    }

    private XElement GetTestDiffElement(JobName testJob, FailedTestDiff diff)
    {
        return new XElement("table",
            new XAttribute("class", "tests"),
            new XElement("tr",
                new XElement("th",
                    new XAttribute("colspan", "2"),
                    $"Test: {testJob}")),
            diff.FailedTests
                .Where(t => t.Newness == Newness.New)
                .Select(t => new XElement("tr",
                    new XElement("td",
                        new XAttribute("class", "test-info"),
                        t.IsFlaky ? "🟠" : "🔴",
                        t.IsFlaky ? new XElement("span",
                            new XAttribute("class", "label unstable"),
                            "flaky") : null),
                    new XElement("td",
                        new XAttribute("class", "test-name"),
                        new XElement("span",
                            new XAttribute("style", "color:#2a5db0;"),
                            new XAttribute("title", $"{t.Test.ClassName}.{t.Test.TestName}"),
                            $"{t.Test.ClassName}.{t.Test.TestName}"),
                        new XElement("pre",
                            TodReports.Shorten(t.Test.ErrorDetails)))))
        );
    }
}
