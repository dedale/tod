using NUnit.Framework;
using Serilog;
using Serilog.Sinks.TestCorrelator;
using Tod.Core;
using Tod.Jenkins;

namespace Tod.Tests.Core;

[TestFixture]
internal sealed class BuildReferenceDestructuringPolicyTests
{
    [Test]
    public void TryDestructure_Works()
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Destructure.With<BuildReferenceDestructuringPolicy>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("Build {@Build} done", new BuildReference(new JobName("folder/job-name"), 1234));

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage();
            Assert.That(message, Is.EqualTo($@"Build ""{'\x1b'}[90mfolder/{'\x1b'}[38;5;0045mjob-name{'\x1b'}[38;5;0015m #{'\x1b'}[38;5;0200m1234"" done"));
        }
    }

    [Test]
    public void TryDestructure_WhenNothingToDo()
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Destructure.With<BuildReferenceDestructuringPolicy>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("{@Dummy}", new { Value = "Foo" });

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage();
            Assert.That(message, Is.EqualTo(@"{ Value: ""Foo"" }"));
        }
    }
}
