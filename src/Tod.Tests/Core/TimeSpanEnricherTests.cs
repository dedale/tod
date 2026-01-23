using NUnit.Framework;
using Tod.Core;

namespace Tod.Tests.Core;

[TestFixture]
internal sealed class TimeSpanEnricherTests
{
    [TestCase(0, 0, 0, 1, "1 ms")]
    [TestCase(0, 0, 0, 456, "456 ms")]
    [TestCase(0, 0, 0, 999, "1 s")]
    [TestCase(0, 0, 1, 0, "1 s")]
    [TestCase(0, 0, 1, 1, "1 s")]
    [TestCase(0, 0, 1, 456, "1.5 s")]
    [TestCase(0, 0, 1, 999, "2 s")]
    [TestCase(0, 0, 29, 0, "29 s")]
    [TestCase(0, 0, 29, 456, "29.5 s")]
    [TestCase(0, 0, 59, 999, "1 min")]
    [TestCase(0, 1, 1, 1, "1 min 1 s")]
    [TestCase(0, 1, 59, 999, "2 min")]
    [TestCase(0, 29, 0, 0, "29 min")]
    [TestCase(0, 29, 29, 456, "29 min 29 s")]
    [TestCase(0, 59, 59, 999, "1 h")]
    [TestCase(1, 1, 1, 1, "1 h 1 min")]
    [TestCase(1, 59, 59, 999, "2 h")]
    [TestCase(2, 0, 1, 1, "2 h")]
    public void Pretty_ShouldReturnExpectedString(int hours, int minutes, int seconds, int milliseconds, string expected)
    {
        var ts = new TimeSpan(0, hours, minutes, seconds, milliseconds);
        var pretty = TimeSpanEnricher.Pretty(ts);
        Assert.That(pretty, Is.EqualTo(expected));
    }

    [TestCase(0, 0, 29, 456, "*[38,5,0079m29.5 s*[0m")]
    public void ColoredPretty_ShouldReturnExpectedString(int hours, int minutes, int seconds, int milliseconds, string expected)
    {
        var ts = new TimeSpan(0, hours, minutes, seconds, milliseconds);
        var pretty = TimeSpanEnricher.ColoredPretty(ts);
        Assert.That(pretty, Is.EqualTo(expected.Replace("*", "\x1b")));
    }
}
