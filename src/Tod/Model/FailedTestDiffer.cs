using Tod.Jenkins;

namespace Tod;

[Flags]
internal enum TestBuildDiffStatus
{
    OK = 0,
    NewFailures = 1,
    UpdatedFailures = 1 << 1,
    SameFailures = 1 << 2
}

internal enum Newness
{
    New,
    Updated,
}

internal sealed record FailedTestResult(FailedTest Test, Newness Newness, bool IsFlaky);

internal sealed class FailedTestDiff(TestBuildDiffStatus status, FailedTestResult[] failedTests)
{
    public TestBuildDiffStatus Status { get; } = status;
    public FailedTestResult[] FailedTests { get; } = failedTests;
}

internal static class FailedTestDiffer
{
    private sealed class KeyedFailedTest(FailedTest failedTest)
    {
        public string Key { get; } = $"{failedTest.ClassName}::{failedTest.TestName}";
        public FailedTest FailedTest { get; } = failedTest;
    }

    public static FailedTestDiff Diff(
        JobName referenceJob,
        IReadOnlyCollection<FailedTest> referenceFailedTests,
        IReadOnlyCollection<FailedTest> onDemandFailedTests,
        IFlakyTests flakyTests)
    {
        var status = TestBuildDiffStatus.OK;
        var sortedReference = referenceFailedTests
            .Select(t => new KeyedFailedTest(t))
            .OrderBy(x => x.Key)
            .ToArray();
        var sortedOnDemand = onDemandFailedTests
            .Select(t => new KeyedFailedTest(t))
            .OrderBy(x => x.Key)
            .ToArray();
        var testResults = new List<FailedTestResult>();
        var (i, j) = (0, 0);
        while (i < sortedReference.Length && j < sortedOnDemand.Length)
        {
            var reference = sortedReference[i];
            var onDemand = sortedOnDemand[j];
            var comparison = string.Compare(reference.Key, onDemand.Key, StringComparison.Ordinal);
            if (comparison == 0)
            {
                // Common
                if (!reference.FailedTest.Equals(onDemand.FailedTest))
                {
                    // Updated
                    testResults.Add(new FailedTestResult(onDemand.FailedTest, Newness.Updated, flakyTests.IsFlaky(referenceJob, onDemand.FailedTest)));
                    status |= TestBuildDiffStatus.UpdatedFailures;
                }
                else
                {
                    status |= TestBuildDiffStatus.SameFailures;
                }
                i++;
                j++;
            }
            else if (comparison < 0)
            {
                // Reference only
                i++;
            }
            else
            {
                // OnDemand only
                testResults.Add(new FailedTestResult(onDemand.FailedTest, Newness.New, flakyTests.IsFlaky(referenceJob, onDemand.FailedTest)));
                status |= TestBuildDiffStatus.NewFailures;
                j++;
            }
        }
        // Handle remaining items in onDemand that come after all reference items
        while (j < sortedOnDemand.Length)
        {
            testResults.Add(new FailedTestResult(sortedOnDemand[j].FailedTest, Newness.New, flakyTests.IsFlaky(referenceJob, sortedOnDemand[j].FailedTest)));
            status |= TestBuildDiffStatus.NewFailures;
            j++;
        }
        return new FailedTestDiff(status, [.. testResults]);
    }
}
