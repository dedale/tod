using System.Diagnostics.CodeAnalysis;

namespace Tod;

internal sealed record FailedTest(string ClassName, string TestName, string ErrorDetails)
{
    [ExcludeFromCodeCoverage]
    public override string ToString()
    {
        return $"{ClassName}.{TestName}";
    }
}
