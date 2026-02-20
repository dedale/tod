using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal sealed class JobDiff(string chain, JobName baselineJob, JobName onDemandJob)
{
    public string Chain { get; } = chain;
    public JobName BaselineJob { get; } = baselineJob;
    public JobName OnDemandJob { get; } = onDemandJob;
}

internal sealed class RequestBuildDiff : IWithCustomSerialization<RequestBuildDiff.Serializable>
{
    public RequestBuildDiff(JobName baselineJobName, JobName onDemandJobName)
        : this(baselineJobName, onDemandJobName, TimeSpan.Zero)
    {
    }

    public RequestBuildDiff(JobName baselineJobName, JobName onDemandJobName, TimeSpan testDuration)
        : this(BaseTestBuildReference.Create(baselineJobName), RequestTestBuildReference.Create(onDemandJobName), testDuration)
    {
    }

    private RequestBuildDiff(BaseTestBuildReference baselineBuild, RequestTestBuildReference onDemandBuild, TimeSpan testDuration)
    {
        BaselineBuild = baselineBuild;
        OnDemandBuild = onDemandBuild;
        TestDuration = testDuration;
    }

    public BaseTestBuildReference BaselineBuild { get; }
    public RequestTestBuildReference OnDemandBuild { get; }
    public TimeSpan TestDuration { get; }

    public bool IsDone => BaselineBuild.IsDone && OnDemandBuild.IsDone;

    public bool TryGetPendingReference([NotNullWhen(true)] out JobName? jobName)
    {
        return BaselineBuild.TryGetPendingReference(out jobName);
    }

    public RequestBuildDiff DoneBaseline(int buildNumber)
    {
        return new RequestBuildDiff(BaselineBuild.DoneBaseline(buildNumber), OnDemandBuild, TestDuration);
    }

    public RequestBuildDiff QueueOnDemand()
    {
        return new RequestBuildDiff(BaselineBuild, OnDemandBuild.Queue(), TestDuration);
    }

    public bool TryGetQueued([NotNullWhen(true)] out JobName? testJob)
    {
        return OnDemandBuild.TryGetQueued(out testJob);
    }

    public RequestBuildDiff DoneOnDemand(int buildNumber)
    {
        return new RequestBuildDiff(BaselineBuild, OnDemandBuild.DoneQueued(buildNumber), TestDuration);
    }

    public RequestBuildDiff RecycleOnDemand(int buildNumber)
    {
        return new RequestBuildDiff(BaselineBuild, OnDemandBuild.Queue().DoneQueued(buildNumber), TestDuration);

    }

    internal sealed class Serializable : ICustomSerializable<RequestBuildDiff>
    {
        [JsonConstructor]
        private Serializable(BaseTestBuildReference.Serializable baselineBuild, RequestTestBuildReference.Serializable onDemandBuild, TimeSpan testDuration)
        {
            BaselineBuild = baselineBuild;
            OnDemandBuild = onDemandBuild;
            TestDuration = testDuration;
        }
        public Serializable(RequestBuildDiff buildDiff)
        {
            BaselineBuild = buildDiff.BaselineBuild.ToSerializable();
            OnDemandBuild = buildDiff.OnDemandBuild.ToSerializable();
            TestDuration = buildDiff.TestDuration;
        }
        public BaseTestBuildReference.Serializable BaselineBuild { get; set; }
        public RequestTestBuildReference.Serializable OnDemandBuild { get; set; }
        public TimeSpan TestDuration { get; set; }

        public RequestBuildDiff FromSerializable()
        {
            var baselineBuild = BaselineBuild.FromSerializable();
            var onDemandBuild = OnDemandBuild.FromSerializable();
            return new RequestBuildDiff(baselineBuild, onDemandBuild, TestDuration);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}
