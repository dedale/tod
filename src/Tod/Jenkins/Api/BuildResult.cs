namespace Tod.Jenkins;

public enum BuildResult
{
    Success,
    Failure,
    Aborted,
    Unstable,
    NotBuilt
}

internal static class BuildResultExtensions
{
    public static BuildResult ToBuildResult(this string value) => value switch
    {
        "SUCCESS" => BuildResult.Success,
        "FAILURE" => BuildResult.Failure,
        "ABORTED" => BuildResult.Aborted,
        "UNSTABLE" => BuildResult.Unstable,
        "NOT_BUILT" => BuildResult.NotBuilt,
        _ => throw new ArgumentException($"Unknown build result: '{value}'", nameof(value))
    };

    public static string ToJenkinsString(this BuildResult result) => result switch
    {
        BuildResult.Success => "SUCCESS",
        BuildResult.Failure => "FAILURE",
        BuildResult.Aborted => "ABORTED",
        BuildResult.Unstable => "UNSTABLE",
        BuildResult.NotBuilt => "NOT_BUILT",
        _ => throw new ArgumentOutOfRangeException(nameof(result), $"Unknown build result: '{result}'")
    };
}
