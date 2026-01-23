using Serilog;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal sealed record TestId(string ClassName, string TestName) : IComparable<TestId>
{
    public int CompareTo(TestId? other)
    {
        if (ReferenceEquals(this, other))
        {
            return 0;
        }
        if (other is null)
        {
            return 1;
        }
        var c = string.Compare(ClassName, other.ClassName, StringComparison.Ordinal);
        if (c == 0)
        {
            c = string.Compare(TestName, other.TestName, StringComparison.Ordinal);
        }
        return c;
    }
}

internal interface IFlakyTests
{
    void Update(IEnumerable<BranchReference> branchReferences);
    bool IsFlaky(JobName jobName, TestId testId);
}

internal static class IFlakyTestsExtensions
{
    public static bool IsFlaky(this IFlakyTests flakyTests, JobName jobName, FailedTest failedTest)
    {
        return flakyTests.IsFlaky(jobName, new TestId(failedTest.ClassName, failedTest.TestName));
    }
}

internal sealed class FlakyTests : IFlakyTests
{
    private readonly Dictionary<JobName, HashSet<TestId>> _flakiesByJob;
    private readonly IFlakyStore _flakyStore;

    public FlakyTests(IFlakyStore flakyStore)
        : this([], flakyStore)
    {
    }

    private FlakyTests(Dictionary<JobName, HashSet<TestId>> flakiesByJob, IFlakyStore flakyStore)
    {
        _flakiesByJob = flakiesByJob;
        _flakyStore = flakyStore;
    }

    private sealed class TestHistory
    {
        private const int MinPeriodsForFlaky = 5;

        private readonly bool _latestFailed;
        private readonly List<int> _periodLengths;

        private TestHistory(bool latestFailed, int failures, int periods, List<int> periodLengths)
        {
            _latestFailed = latestFailed;
            Failures = failures;
            Periods = periods;
            _periodLengths = periodLengths;
        }

        public TestHistory(bool latestFailed, int count)
            : this(latestFailed, latestFailed ? 1 : 0, 1, [count])
        {
        }

        public int Failures { get; }
        public int Periods { get; }

        public bool IsFlaky => Periods >= MinPeriodsForFlaky && Periods * 2 > Failures;

        public TestHistory Add(bool nextFailed)
        {
            if (_latestFailed == nextFailed)
            {
                var newLengths = new List<int>(_periodLengths);
                newLengths[^1]++;
                return new TestHistory(_latestFailed, Failures + (nextFailed ? 1 : 0), Periods, newLengths);
            }
            else
            {
                var newLengths = new List<int>(_periodLengths) { 1 };
                return new TestHistory(nextFailed, Failures + (nextFailed ? 1 : 0), Periods + 1, newLengths);
            }
        }
    }

    public void Update(IEnumerable<BranchReference> branchReferences)
    {
        _flakiesByJob.Clear();
        foreach (var branchReference in branchReferences)
        {
            foreach (var collection in branchReference.TestBuilds)
            {
                if (collection.Count == 0)
                {
                    continue;
                }
                var stopwatch = Stopwatch.StartNew();
                var firstNumber = collection[0].BuildNumber;
                var historyByTest = new Dictionary<TestId, TestHistory>();
                foreach (var build in collection)
                {
                    var otherTests = historyByTest.Keys.ToHashSet();
                    foreach (var failedTest in build.FailedTests)
                    {
                        var testId = new TestId(failedTest.ClassName, failedTest.TestName);
                        if (historyByTest.TryGetValue(testId, out var history))
                        {
                            historyByTest[testId] = history.Add(true);
                        }
                        else
                        {
                            var newHistory = build.BuildNumber > firstNumber ? new TestHistory(false, build.BuildNumber - firstNumber).Add(true) : new TestHistory(true, 1);
                            historyByTest.Add(testId, newHistory);
                        }
                        otherTests.Remove(testId);
                    }
                    foreach (var testId in otherTests)
                    {
                        historyByTest[testId] = historyByTest[testId].Add(false);
                    }
                }
                var flakies = historyByTest
                    .Where(kvp => kvp.Value.IsFlaky)
                    .Select(kvp => kvp.Key)
                    .ToHashSet();
                Log.Debug("Found {FlakyCount} flaky tests in {@JobName} in {ElapsedMilliseconds} ms", flakies.Count, collection.JobName, stopwatch.ElapsedMilliseconds);
                _flakiesByJob.Add(collection.JobName, flakies);
            }
        }
        _flakyStore.Save(this);
    }

    public bool IsFlaky(JobName jobName, TestId testId)
    {
        return _flakiesByJob.TryGetValue(jobName, out var flakies) && flakies.Contains(testId);
    }

    internal sealed class Serializable
    {
        public List<KeyValuePair<JobName, List<TestId>>> FlakyTests { get; }

        [JsonConstructor]
        private Serializable(List<KeyValuePair<JobName, List<TestId>>> flakyTests)
        {
            FlakyTests = flakyTests;
        }

        public Serializable(FlakyTests flakyTests)
            : this([.. flakyTests._flakiesByJob.Select(kvp => new KeyValuePair<JobName, List<TestId>>(kvp.Key, [.. kvp.Value]))])
        {
        }

        public FlakyTests FromSerializable(IFlakyStore flakyStore)
        {
            return new FlakyTests(FlakyTests.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ToHashSet()), flakyStore);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}
