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

    [Option("no-cache", Required = false, HelpText = "Reload job list from Jenkins API")]
    public bool NoCache { get; set; }
}

[ExcludeFromCodeCoverage]
[Verb("sync", HelpText = "Sync builds")]
internal sealed class SyncOptions : BaseOptions
{
    [Option('u', "user-token", Required = true, HelpText = "User token for Jenkins authentication")]
    public string UserToken { get; set; }
}

[ExcludeFromCodeCoverage]
[Verb("new", HelpText = "Create a new request")]
internal sealed class NewOptions : BaseOptions
{
    [Option('b', "branch", Required = false, HelpText = "Reference branch")]
    public string? BranchName { get; set; }

    [Option('f', "filters", Required = true, HelpText = "Filter names")]
    public IEnumerable<string> Filters { get; set; }

    [Option('u', "user-token", Required = true, HelpText = "User token for Jenkins authentication")]
    public string UserToken { get; set; }
}

#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider adding the 'required' modifier or declaring as nullable.
