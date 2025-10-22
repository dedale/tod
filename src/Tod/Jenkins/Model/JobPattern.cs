using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace Tod.Jenkins;

internal interface IJobPattern<TMatch> where TMatch : class
{
    bool IsMatch(JobName jobName, [NotNullWhen(true)] out TMatch? jobMatch);
}

internal abstract class ReferenceJobMatch
{
    public abstract void Match(Action<BranchName, RootName> onRoot, Action<BranchName, TestName> onTest);
    public abstract T Match<T>(Func<BranchName, RootName, T> onRoot, Func<BranchName, TestName, T> onTest);

    private sealed class Root(BranchName branchName, RootName rootName) : ReferenceJobMatch
    {
        public override void Match(Action<BranchName, RootName> onRoot, Action<BranchName, TestName> _) => onRoot(branchName, rootName);
        public override T Match<T>(Func<BranchName, RootName, T> onRoot, Func<BranchName, TestName, T> _) => onRoot(branchName, rootName);
    }

    private sealed class Test(BranchName branchName, TestName testName) : ReferenceJobMatch
    {
        public override void Match(Action<BranchName, RootName> onRoot, Action<BranchName, TestName> onTest) => onTest(branchName, testName);
        public override T Match<T>(Func<BranchName, RootName, T> onRoot, Func<BranchName, TestName, T> onTest) => onTest(branchName, testName);
    }

    public static ReferenceJobMatch NewRoot(BranchName branch, RootName rootName) => new Root(branch, rootName);

    public static ReferenceJobMatch NewTest(BranchName branch, TestName testName) => new Test(branch, testName);
}

internal sealed class ReferenceJobPattern(ReferenceJobConfig config) : IJobPattern<ReferenceJobMatch>
{
    private readonly Regex _regex = new(config.Pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public bool IsMatch(JobName jobName, [NotNullWhen(true)] out ReferenceJobMatch? jobMatch)
    {
        var match = _regex.Match(jobName.Value);
        if (match.Success)
        {
            jobMatch = config.IsRoot ?
                ReferenceJobMatch.NewRoot(config.BranchName, new(match.Groups["root"].Value)) :
                ReferenceJobMatch.NewTest(config.BranchName, new(match.Groups["test"].Value));
            return true;
        }
        jobMatch = null;
        return false;
    }
}

internal abstract class OnDemandJobMatch
{
    public abstract void Match(Action<RootName> onRoot, Action<TestName> onTest);
    public abstract T Match<T>(Func<RootName, T> onRoot, Func<TestName, T> onTest);

    private sealed class Root(RootName rootName) : OnDemandJobMatch
    {
        public override void Match(Action<RootName> onRoot, Action<TestName> _) => onRoot(rootName);
        public override T Match<T>(Func<RootName, T> onRoot, Func<TestName, T> _) => onRoot(rootName);
    }

    private sealed class Test(TestName testName) : OnDemandJobMatch
    {
        public override void Match(Action<RootName> onRoot, Action<TestName> onTest) => onTest(testName);
        public override T Match<T>(Func<RootName, T> onRoot, Func<TestName, T> onTest) => onTest(testName);
    }

    public static OnDemandJobMatch NewRoot(RootName rootName) => new Root(rootName);

    public static OnDemandJobMatch NewTest(TestName testName) => new Test(testName);
}

internal sealed class OnDemandJobPattern(OnDemandJobConfig config) : IJobPattern<OnDemandJobMatch>
{
    private readonly Regex _regex = new(config.Pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

    public bool IsMatch(JobName jobName, [NotNullWhen(true)] out OnDemandJobMatch? jobMatch)
    {
        var match = _regex.Match(jobName.Value);
        if (match.Success)
        {
            jobMatch = config.IsRoot ?
                OnDemandJobMatch.NewRoot(new(match.Groups["root"].Value)) :
                OnDemandJobMatch.NewTest(new(match.Groups["test"].Value));
            return true;
        }
        jobMatch = null;
        return false;
    }
}

internal sealed class JobMatchCollection<TMatch, TPattern>(IEnumerable<TPattern> jobPatterns)
    where TMatch : class
    where TPattern : IJobPattern<TMatch>
{
    private readonly TPattern[] _jobPatterns = [.. jobPatterns];

    public bool FindFirst(JobName jobName, [NotNullWhen(true)] out TMatch? jobMatch)
    {
        foreach (var pattern in _jobPatterns)
        {
            if (pattern.IsMatch(jobName, out jobMatch))
            {
                return true;
            }
        }
        jobMatch = null;
        return false;
    }
}
