namespace Tod;

[Flags]
internal enum TestBuildDiffStatus
{
    OK = 0,
    NewFailures = 1,
    UpdatedFailures = 1 << 1,
    SameFailures = 1 << 2
}

internal sealed class FailedTestDiff(TestBuildDiffStatus status, FailedTest[] updated, FailedTest[] added)
{
    public TestBuildDiffStatus Status { get; } = status;
    public FailedTest[] Updated { get; } = updated;
    public FailedTest[] Added { get; } = added;
}

internal static class FailedTestDiffer
{
    private sealed class KeyedFailedTest(FailedTest failedTest)
    {
        public string Key { get; } = $"{failedTest.ClassName}::{failedTest.TestName}";
        public FailedTest FailedTest { get; } = failedTest;
    }

    public static FailedTestDiff Diff(
        IReadOnlyCollection<FailedTest> referenceFailedTests,
        IReadOnlyCollection<FailedTest> onDemandFailedTests)
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
        var updated = new List<FailedTest>();
        var added = new List<FailedTest>();
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
                    updated.Add(onDemand.FailedTest);
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
                added.Add(onDemand.FailedTest);
                status |= TestBuildDiffStatus.NewFailures;
                j++;
            }
        }
        // Handle remaining items in onDemand that come after all reference items
        while (j < sortedOnDemand.Length)
        {
            added.Add(sortedOnDemand[j].FailedTest);
            status |= TestBuildDiffStatus.NewFailures;
            j++;
        }
        return new FailedTestDiff(status, [.. updated], [.. added]);
    }
}
