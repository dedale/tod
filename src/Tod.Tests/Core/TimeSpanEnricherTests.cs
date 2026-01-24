using NUnit.Framework;
using Serilog;
using Serilog.Sinks.TestCorrelator;
using System.Text.RegularExpressions;
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
    public void Enrich_ShouldReturnExpectedString_IgnoringAnsiCodes(int hours, int minutes, int seconds, int milliseconds, string expected)
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Enrich.With<TimeSpanEnricher>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("Duration: {Duration}", new TimeSpan(0, hours, minutes, seconds, milliseconds));

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage().RemoveAnsiCodes();
            Assert.That(message, Is.EqualTo($@"Duration: ""{expected}"""));
        }
    }

    [TestCase(0, 0, 29, 456, "*[38;5;0079m29.5 s*[0m")]
    public void Enrich_ShouldReturnExpectedString_WithAnsiCodes(int hours, int minutes, int seconds, int milliseconds, string expected)
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Enrich.With<TimeSpanEnricher>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("Duration: {Duration}", new TimeSpan(0, hours, minutes, seconds, milliseconds));

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage();
            Assert.That(message, Is.EqualTo($@"Duration: ""{expected.Replace("*", "\x1b")}"""));
        }
    }
}
