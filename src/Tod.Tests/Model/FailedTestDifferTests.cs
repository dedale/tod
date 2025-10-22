using NUnit.Framework;
using Tod.Tests.Jenkins;

namespace Tod.Tests;

[TestFixture]
internal sealed class FailedTestDifferTests
{
    private static (FailedTest[] reference, FailedTest[] onDemand) GetSampleData(
        Action<List<FailedTest>>? updateReference = null,
        Action<List<FailedTest>>? updateOnDemand = null)
    {
        var reference = new List<FailedTest>
        {
            new("Class1", "Test", "Error"),
            new("Class3", "Test", "Error"),
        };
        var onDemand = new List<FailedTest>
        {
            new("Class1", "Test", "Error"),
            new("Class3", "Test", "Error"),
        };
        updateReference?.Invoke(reference);
        updateOnDemand?.Invoke(onDemand);
        reference.Shuffle();
        onDemand.Shuffle();
        return (reference.ToArray(), onDemand.ToArray());
    }

    private static void TestDiffAdded(FailedTest added)
    {
        var (reference, onDemand) = GetSampleData(updateOnDemand: onDemand => onDemand.Add(added));
        var diff = FailedTestDiffer.Diff(reference, onDemand);
        Assert.That(diff.Status, Is.EqualTo(TestBuildDiffStatus.NewFailures | TestBuildDiffStatus.SameFailures));
        Assert.That(diff.Added, Is.EqualTo([added]));
        Assert.That(diff.Updated, Is.Empty);
    }

    private static void TestDiffUpdated(FailedTest old, FailedTest updated)
    {
        var (reference, onDemand) = GetSampleData(
            updateReference: reference => reference.Add(old),
            updateOnDemand: onDemand => onDemand.Add(updated));
        var diff = FailedTestDiffer.Diff(reference, onDemand);
        Assert.That(diff.Status, Is.EqualTo(TestBuildDiffStatus.UpdatedFailures | TestBuildDiffStatus.SameFailures));
        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Updated, Is.EqualTo([updated]));
    }

    private static void TestDiffRemoved(FailedTest removed)
    {
        var (reference, onDemand) = GetSampleData(updateReference: reference => reference.Add(removed));
        var diff = FailedTestDiffer.Diff(reference, onDemand);
        Assert.That(diff.Status, Is.EqualTo(TestBuildDiffStatus.SameFailures));
        Assert.That(diff.Added, Is.Empty);
        Assert.That(diff.Updated, Is.Empty);
    }

    [Test]
    public void Diff_AddedBeginning_ReturnsAdded()
    {
        var added = new FailedTest("Class0", "Test", "New error");
        TestDiffAdded(added: added);
    }

    [Test]
    public void Diff_AddedMiddle_ReturnsAdded()
    {
        var added = new FailedTest("Class2", "Test", "New error");
        TestDiffAdded(added: added);
    }

    [Test]
    public void Diff_AddedEnd_ReturnsAdded()
    {
        var added = new FailedTest("Class4", "Test", "New error");
        TestDiffAdded(added: added);
    }

    [Test]
    public void Diff_UpdateBegin_ReturnsUpdated()
    {
        var old = new FailedTest("Class0", "Test", "Old error");
        var updated = new FailedTest("Class0", "Test", "New error");
        TestDiffUpdated(old: old, updated: updated);
    }

    [Test]
    public void Diff_UpdateMiddle_ReturnsUpdated()
    {
        var old = new FailedTest("Class2", "Test", "Old error");
        var updated = new FailedTest("Class2", "Test", "New error");
        TestDiffUpdated(old: old, updated: updated);
    }

    [Test]
    public void Diff_UpdateEnd_ReturnsUpdated()
    {
        var old = new FailedTest("Class4", "Test", "Old error");
        var updated = new FailedTest("Class4", "Test", "New error");
        TestDiffUpdated(old: old, updated: updated);
    }

    [Test]
    public void Diff_RemovedBegin_ReturnsRemoved()
    {
        var removed = new FailedTest("Class0", "Test", "Old error");
        TestDiffRemoved(removed: removed);
    }

    [Test]
    public void Diff_RemovedMiddle_ReturnsRemoved()
    {
        var removed = new FailedTest("Class2", "Test", "Old error");
        TestDiffRemoved(removed: removed);
    }

    [Test]
    public void Diff_RemovedEnd_ReturnsRemoved()
    {
        var removed = new FailedTest("Class4", "Test", "Old error");
        TestDiffRemoved(removed: removed);
    }

    public enum TestDiff
    {
        Removed,
        Upated,
        Added
    }

    [TestCase(TestDiff.Added, TestDiff.Upated, TestDiff.Removed)]
    [TestCase(TestDiff.Added, TestDiff.Removed, TestDiff.Upated)]
    [TestCase(TestDiff.Upated, TestDiff.Added, TestDiff.Removed)]
    [TestCase(TestDiff.Upated, TestDiff.Removed, TestDiff.Added)]
    [TestCase(TestDiff.Removed, TestDiff.Added, TestDiff.Upated)]
    [TestCase(TestDiff.Removed, TestDiff.Upated, TestDiff.Added)]
    public void Diff_AddedUpdatedRemovedTogether_Works(TestDiff first, TestDiff second, TestDiff third)
    {
        var reference = new List<FailedTest>();
        var onDemand = new List<FailedTest>();
        var allAdded = new List<FailedTest>();
        var allUpdated = new List<FailedTest>();
        var testDiffs = new[] { first, second, third };
        var i = 0;
        AddTestDiffs(i++);
        AddSameTests(i++);
        AddTestDiffs(i++);
        AddSameTests(i++);
        AddTestDiffs(i++);
        Assert.That(i, Is.EqualTo(5));

        reference.Shuffle();
        onDemand.Shuffle();

        var diff = FailedTestDiffer.Diff(reference, onDemand);
        Assert.That(diff.Status, Is.EqualTo(TestBuildDiffStatus.NewFailures | TestBuildDiffStatus.UpdatedFailures | TestBuildDiffStatus.SameFailures));
        Assert.That(diff.Added, Is.EquivalentTo(allAdded));
        Assert.That(diff.Updated, Is.EquivalentTo(allUpdated));

        void AddTestDiffs(int i)
        {
            for (var j = 0; j < testDiffs.Length; j++, i++)
            {
                var testDiff = testDiffs[j];
                switch (testDiff)
                {
                    case TestDiff.Added:
                        var added = new FailedTest($"Class{i}", $"Test{j}", "New error");
                        allAdded.Add(added);
                        onDemand.Add(added);
                        break;
                    case TestDiff.Upated:
                        reference.Add(new FailedTest($"Class{i}", $"Test{j}", "Old error"));
                        var updated = new FailedTest($"Class{i}", $"Test{j}", "New error");
                        onDemand.Add(updated);
                        allUpdated.Add(updated);
                        break;
                    case TestDiff.Removed:
                        reference.Add(new FailedTest($"Class{i}", $"Test{j}", "Old error"));
                        break;
                }
            }
        }

        void AddSameTests(int i)
        {
            var test = new FailedTest($"Class{i}", "Test", "Error");
            reference.Add(test);
            onDemand.Add(test);
        }
    }
}