using NUnit.Framework;
using Serilog;
using Serilog.Sinks.TestCorrelator;
using Tod.Core;
using Tod.Jenkins;

namespace Tod.Tests.Core;

[TestFixture]
internal sealed class BuildResultInfoDestructuringPolicyTests
{
    [TestCase("Success", true, @"""*[38;5;34mSuccess*[0m""")]
    [TestCase("Failure", false, @"""*[38;5;160mFailure*[0m""")]
    public void TryDestructure_Works(string value, bool success, string expected)
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Destructure.With<BuildResultInfoDestructuringPolicy>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("{@Result}", success ? BuildResultInfo.Success(value) : BuildResultInfo.Failure(value));

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage();
            Assert.That(message, Is.EqualTo(expected.Replace("*", "\x1b")));
        }
    }

    [Test]
    public void TryDestructure_WhenNothingToDo()
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Destructure.With<BuildResultInfoDestructuringPolicy>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("{@Dummy}", new { Value = "Foo" });

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage();
            Assert.That(message, Is.EqualTo(@"{ Value: ""Foo"" }"));
        }
    }
}
