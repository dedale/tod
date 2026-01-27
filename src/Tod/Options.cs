using CommandLine;
using System.Diagnostics.CodeAnalysis;

namespace Tod;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.

[ExcludeFromCodeCoverage]
internal abstract class BaseOptions
{
    [Option('c', "config", Required = true, HelpText = "Path to config file")]
    public string ConfigPath { get; set; }

    [Option('w', "workspace", Required = true, HelpText = "Path to workspace dir")]
    public string WorkspaceDir { get; set; }

    [Option('d', "--debug", HelpText = "Enable debug logs")]
    public bool Debug { get; set; }
}

[ExcludeFromCodeCoverage]
[Verb("sync", HelpText = "Sync builds or jobs")]
internal sealed class SyncOptions : BaseOptions
{
    [Option('j', "jenkins-token", Required = true, HelpText = "Jenkins API token for authentication")]
    public string JenkinsToken { get; set; }

    [Option('s', "jobs", HelpText = "Synchronize jobs")]
    public bool Jobs { get; set; }
}


[ExcludeFromCodeCoverage]
[Verb("new", HelpText = "Create a new request")]
internal sealed class NewOptions : BaseOptions
{
    [Option('b', "branch", Required = false, HelpText = "Reference branch")]
    public string? BranchName { get; set; }

    [Option('r', "root-filters", Required = true, HelpText = "Root filter names")]
    public IEnumerable<string> RootFilters { get; set; }

    [Option('t', "test-filters", Required = true, HelpText = "Test filter names")]
    public IEnumerable<string> TestFilters { get; set; }

    [Option('u', "user", HelpText = "User name when working in service mode")]
    public string User { get; set; }

    [Option("domain", HelpText = "User domain when running in service mode")]
    public string UserDomain { get; set; }

    [Option('j', "jenkins-token", Required = true, HelpText = "Jenkins API token for authentication")]
    public string JenkinsToken { get; set; }

    [Option('g', "gerrit-token", Required = true, HelpText = "Gerrit API token for authentication")]
    public string GerritToken { get; set; }
}


[ExcludeFromCodeCoverage]
[Verb("jobs", HelpText = "Get job names from filters")]
internal sealed class JobsOptions : BaseOptions
{
    [Option('b', "branch", Required = false, HelpText = "Reference branch")]
    public string? BranchName { get; set; }

    [Option('r', "root-filters", Required = true, HelpText = "Root filter names")]
    public IEnumerable<string> RootFilters { get; set; }

    [Option('t', "test-filters", Required = true, HelpText = "Test filter names")]
    public IEnumerable<string> TestFilters { get; set; }

    [Option('m', "commits", HelpText = "Sha1 of last commits in current branch in service mode")]
    public IEnumerable<string> Commits { get; set; }
}

[ExcludeFromCodeCoverage]
[Verb("report", HelpText = "Send report for a request")]
internal sealed class ReportOptions : BaseOptions
{
    [Option('i', "request-id", Required = true, HelpText = "Request ID to report on")]
    public string RequestId { get; set; }

    [Option('u', "user", HelpText = "User name when working in service mode")]
    public string User { get; set; }
}

[ExcludeFromCodeCoverage]
[Verb("list", HelpText = "List requests for current user")]
internal sealed class ListOptions : BaseOptions
{
    [Option('a', "all", Required = false, HelpText = "List all requests (including completed ones)")]
    public bool All { get; set; }

    [Option('u', "user", HelpText = "User name when working in service mode")]
    public string User { get; set; }
}

[ExcludeFromCodeCoverage]
[Verb("abort", HelpText = "Abort a request")]
internal sealed class AbortOptions : BaseOptions
{
    [Option('i', "request-id", Required = true, HelpText = "Request ID to abort")]
    public string RequestId { get; set; }

    [Option('u', "user", HelpText = "User name when working in service mode")]
    public string User { get; set; }
}

[ExcludeFromCodeCoverage]
[Verb("filters", HelpText = "List jobs per filters")]
internal sealed class FiltersOptions : BaseOptions
{
}

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
