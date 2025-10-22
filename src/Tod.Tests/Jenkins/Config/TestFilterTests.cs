using NUnit.Framework;
using System.Text.RegularExpressions;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class TestFilterTests
{
    [Test]
    public void Constructor_ValidInput_SetsProperties()
    {
        const string name = "MyFilter";
        const string pattern = "Test.*";
        const string group = "tests";
        var filter = new TestFilter(name, pattern, group);
        Assert.That(filter.Name, Is.EqualTo(name));
        Assert.That(filter.Pattern, Is.EqualTo(pattern));
        Assert.That(filter.Group, Is.EqualTo(group));
    }

    [Test]
    public void Constructor_InvalidRegexPattern_ThrowsArgumentException()
    {
        const string name = "MyFilter";
        const string invalidPattern = "["; // Invalid regex pattern
        const string group = "tests";
        Assert.That(() => new TestFilter(name, invalidPattern, group), Throws.TypeOf<RegexParseException>());
    }

    [Test]
    public void Matches_PatternMatches_ReturnsTrue()
    {
        var filter = new TestFilter("MyFilter", @"Test\d+", "tests");
        var testName = new TestName("Test123");
        var result = filter.Matches(testName);
        Assert.That(result, Is.True);
    }

    [Test]
    public void Matches_PatternDoesNotMatch_ReturnsFalse()
    {
        var filter = new TestFilter("MyFilter", @"Test\d+", "tests");
        var testName = new TestName("TestABC");
        var result = filter.Matches(testName);
        Assert.That(result, Is.False);
    }

    [Test]
    public void Matches_EmptyPattern_HandlesCorrectly()
    {
        var filter = new TestFilter("MyFilter", "", "tests");
        var testName = new TestName("Test123");
        var result = filter.Matches(testName);
        Assert.That(result, Is.True);
    }

    [Test]
    public void GetHashCode_SameValues_ReturnsSameHashCode()
    {
        var filter1 = new TestFilter("MyFilter", "Test.*", "tests");
        var filter2 = new TestFilter("MyFilter", "Test.*", "tests");

        Assert.That(filter1.GetHashCode(), Is.EqualTo(filter2.GetHashCode()));
    }

    [Test]
    public void GetHashCode_DifferentName_ReturnsDifferentHashCode()
    {
        var filter1 = new TestFilter("MyFilter1", "Test.*", "tests");
        var filter2 = new TestFilter("MyFilter2", "Test.*", "tests");

        Assert.That(filter1.GetHashCode(), Is.Not.EqualTo(filter2.GetHashCode()));
    }

    [Test]
    public void GetHashCode_DifferentPattern_ReturnsDifferentHashCode()
    {
        var filter1 = new TestFilter("MyFilter", "Test.*", "tests");
        var filter2 = new TestFilter("MyFilter", "Test\\d+", "tests");

        Assert.That(filter1.GetHashCode(), Is.Not.EqualTo(filter2.GetHashCode()));
    }

    [Test]
    public void GetHashCode_DifferentGroup_ReturnsDifferentHashCode()
    {
        var filter1 = new TestFilter("MyFilter", "Test.*", "tests");
        var filter2 = new TestFilter("MyFilter", "Test.*", "integration");

        Assert.That(filter1.GetHashCode(), Is.Not.EqualTo(filter2.GetHashCode()));
    }

    [Test]
    public void GetHashCode_CalledMultipleTimes_ReturnsSameValue()
    {
        var filter = new TestFilter("MyFilter", "Test.*", "tests");

        var hashCode1 = filter.GetHashCode();
        var hashCode2 = filter.GetHashCode();
        var hashCode3 = filter.GetHashCode();

        Assert.That(hashCode1, Is.EqualTo(hashCode2));
        Assert.That(hashCode2, Is.EqualTo(hashCode3));
    }

    [Test]
    public void GetHashCode_ConsistentWithEquals_EqualObjectsHaveSameHashCode()
    {
        var filter1 = new TestFilter("MyFilter", "Test.*", "tests");
        var filter2 = new TestFilter("MyFilter", "Test.*", "tests");

        Assert.That(filter1.Equals(filter2), Is.True);
        Assert.That(filter1.GetHashCode(), Is.EqualTo(filter2.GetHashCode()));
    }

    [Test]
    public void GetHashCode_DifferentCombinationsOfProperties_ProducesDifferentHashCodes()
    {
        var filter1 = new TestFilter("Filter1", "Pattern1", "Group1");
        var filter2 = new TestFilter("Filter1", "Pattern1", "Group2");
        var filter3 = new TestFilter("Filter1", "Pattern2", "Group1");
        var filter4 = new TestFilter("Filter2", "Pattern1", "Group1");

        var hashCodes = new[]
        {
            filter1.GetHashCode(),
            filter2.GetHashCode(),
            filter3.GetHashCode(),
            filter4.GetHashCode()
        };

        // All hash codes should be different
        Assert.That(hashCodes.Distinct().Count(), Is.EqualTo(4));
    }

    [Test]
    public void GetHashCode_UsedInHashSet_WorksCorrectly()
    {
        var filter1 = new TestFilter("MyFilter", "Test.*", "tests");
        var filter2 = new TestFilter("MyFilter", "Test.*", "tests");
        var filter3 = new TestFilter("OtherFilter", "Test.*", "tests");

        var hashSet = new HashSet<TestFilter> { filter1, filter2, filter3 };

        // filter1 and filter2 are equal, so only 2 items should be in the set
        Assert.That(hashSet.Count, Is.EqualTo(2));
        Assert.That(hashSet.Contains(filter1), Is.True);
        Assert.That(hashSet.Contains(filter2), Is.True);
        Assert.That(hashSet.Contains(filter3), Is.True);
    }

    [Test]
    public void GetHashCode_UsedInDictionary_WorksCorrectly()
    {
        var filter1 = new TestFilter("MyFilter", "Test.*", "tests");
        var filter2 = new TestFilter("MyFilter", "Test.*", "tests");

        var dictionary = new Dictionary<TestFilter, string>
        {
            { filter1, "value1" }
        };

        // filter2 should be found because it has the same hash code and is equal to filter1
        Assert.That(dictionary.ContainsKey(filter2), Is.True);
        Assert.That(dictionary[filter2], Is.EqualTo("value1"));
    }
}