using NUnit.Framework;
using Serilog;
using Serilog.Sinks.TestCorrelator;
using Tod.Core;
using Tod.Jenkins;

namespace Tod.Tests.Core;

[TestFixture]
internal sealed class JobNameDestructuringPolicyTests
{
    [Test]
    public void TryDestructure_WithOneJobName()
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Destructure.With<JobNameDestructuringPolicy>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("Job {@Job} found", new JobName("folder/job-name"));

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage();
            Assert.That(message, Is.EqualTo($@"Job ""{'\x1b'}[90mfolder/{'\x1b'}[38;5;0045mjob-name"" found"));
        }
    }

    [Test]
    public void TryDestructure_WithManyJobNames()
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Destructure.With<JobNameDestructuringPolicy>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("Ref {@Job} and On-Demand {@Job} are linked", new JobName("folder/MAIN-build"), new JobName("folder/CUSTOM-build"));

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage();
            Assert.That(message, Is.EqualTo($@"Ref ""{'\x1b'}[90mfolder/{'\x1b'}[38;5;0045mCUSTOM-build"" and On-Demand ""{'\x1b'}[90mfolder/{'\x1b'}[38;5;0045mCUSTOM-build"" are linked"));
        }
    }

    [Test]
    public void TryDestructure_WhenNothingToDo()
    {
        using (TestCorrelator.CreateContext())
        {
            var logger = new LoggerConfiguration()
                .Destructure.With<JobNameDestructuringPolicy>()
                .WriteTo.TestCorrelator()
                .CreateLogger();

            logger.Information("{@Dummy}", new { Value = "Foo" });

            var events = TestCorrelator.GetLogEventsFromCurrentContext();
            var message = events.Single().RenderMessage();
            Assert.That(message, Is.EqualTo(@"{ Value: ""Foo"" }"));
        }
    }
}
