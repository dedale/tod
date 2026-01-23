using NUnit.Framework;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class FilterJobsTests
{
    [Test]
    public void TotalDuration_SingleJob_ReturnsDuration()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.FromMinutes(5)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void TotalDuration_MultipleJobs_ReturnsSumOfDurations()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.FromMinutes(5),
            [new JobName("job2")] = TimeSpan.FromMinutes(10),
            [new JobName("job3")] = TimeSpan.FromMinutes(15)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void TotalDuration_ZeroDuration_ReturnsZero()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.Zero
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TotalDuration_MixedDurations_ReturnsCorrectSum()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.FromSeconds(30),
            [new JobName("job2")] = TimeSpan.FromMinutes(2),
            [new JobName("job3")] = TimeSpan.FromHours(1)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        var totalDuration = filterJobs.TotalDuration;

        var expected = TimeSpan.FromSeconds(30) + TimeSpan.FromMinutes(2) + TimeSpan.FromHours(1);
        Assert.That(totalDuration, Is.EqualTo(expected));
    }

    [Test]
    public void TotalDuration_MultipleZeroDurations_ReturnsZero()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.Zero,
            [new JobName("job2")] = TimeSpan.Zero,
            [new JobName("job3")] = TimeSpan.Zero
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void TotalDuration_LargeDurations_HandlesCorrectly()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.FromHours(10),
            [new JobName("job2")] = TimeSpan.FromHours(20),
            [new JobName("job3")] = TimeSpan.FromHours(30)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.FromHours(60)));
    }

    [Test]
    public void TotalDuration_AfterAdd_UpdatesTotal()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.FromMinutes(5)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        filterJobs.Add(new JobName("job2"), TimeSpan.FromMinutes(10));
        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.FromMinutes(15)));
    }

    [Test]
    public void TotalDuration_AfterMultipleAdds_UpdatesTotal()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.FromMinutes(5)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        filterJobs.Add(new JobName("job2"), TimeSpan.FromMinutes(10));
        filterJobs.Add(new JobName("job3"), TimeSpan.FromMinutes(15));
        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.FromMinutes(30)));
    }

    [Test]
    public void TotalDuration_WithSubSecondPrecision_CalculatesAccurately()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.FromMilliseconds(500),
            [new JobName("job2")] = TimeSpan.FromMilliseconds(750)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.FromMilliseconds(1250)));
    }

    [Test]
    public void Jobs_ReturnsAllJobNames()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var job1 = new JobName("job1");
        var job2 = new JobName("job2");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [job1] = TimeSpan.FromMinutes(5),
            [job2] = TimeSpan.FromMinutes(10)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        var jobs = filterJobs.Jobs.ToList();

        Assert.That(jobs, Has.Count.EqualTo(2));
        Assert.That(jobs, Does.Contain(job1));
        Assert.That(jobs, Does.Contain(job2));
    }

    [Test]
    public void Filter_ReturnsFilter()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>
        {
            [new JobName("job1")] = TimeSpan.FromMinutes(5)
        };
        var filterJobs = new FilterJobs(filter, durationByJob);

        Assert.That(filterJobs.Filter, Is.EqualTo(filter));
    }

    [Test]
    public void Add_NewJob_AddsToJobs()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>();
        var filterJobs = new FilterJobs(filter, durationByJob);
        var job = new JobName("job1");

        filterJobs.Add(job, TimeSpan.FromMinutes(5));

        Assert.That(filterJobs.Jobs, Does.Contain(job));
    }

    [Test]
    public void Add_NewJob_AddsDuration()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>();
        var filterJobs = new FilterJobs(filter, durationByJob);

        filterJobs.Add(new JobName("job1"), TimeSpan.FromMinutes(5));

        Assert.That(filterJobs.TotalDuration, Is.EqualTo(TimeSpan.FromMinutes(5)));
    }

    [Test]
    public void Constructor_EmptyDictionary_TotalDurationIsZero()
    {
        var filter = new TestFilter("test", "test-pattern", "group");
        var durationByJob = new Dictionary<JobName, TimeSpan>();
        var filterJobs = new FilterJobs(filter, durationByJob);

        var totalDuration = filterJobs.TotalDuration;

        Assert.That(totalDuration, Is.EqualTo(TimeSpan.Zero));
    }
}
