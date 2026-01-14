using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal sealed class JobDiff(string chain, JobName referenceJob, JobName onDemandJob)
{
    public string Chain { get; } = chain;
    public JobName ReferenceJob { get; } = referenceJob;
    public JobName OnDemandJob { get; } = onDemandJob;
}

internal sealed class RequestBuildDiff : IWithCustomSerialization<RequestBuildDiff.Serializable>
{
    public RequestBuildDiff(JobName referenceJobName, JobName onDemandJobName)
        : this(referenceJobName, onDemandJobName, TimeSpan.Zero)
    {
    }

    public RequestBuildDiff(JobName referenceJobName, JobName onDemandJobName, TimeSpan testDuration)
        : this(RefTestBuildReference.Create(referenceJobName), RequestTestBuildReference.Create(onDemandJobName), testDuration)
    {
    }

    private RequestBuildDiff(RefTestBuildReference referenceBuild, RequestTestBuildReference onDemandBuild, TimeSpan testDuration)
    {
        ReferenceBuild = referenceBuild;
        OnDemandBuild = onDemandBuild;
        TestDuration = testDuration;
    }

    public RefTestBuildReference ReferenceBuild { get; }
    public RequestTestBuildReference OnDemandBuild { get; }
    public TimeSpan TestDuration { get; }

    public bool IsDone => ReferenceBuild.IsDone && OnDemandBuild.IsDone;

    public bool TryGetPendingReference([NotNullWhen(true)] out JobName? jobName)
    {
        return ReferenceBuild.TryGetPendingReference(out jobName);
    }

    public RequestBuildDiff DoneReference(int buildNumber)
    {
        return new RequestBuildDiff(ReferenceBuild.DoneReference(buildNumber), OnDemandBuild, TestDuration);
    }

    public RequestBuildDiff QueueOnDemand()
    {
        return new RequestBuildDiff(ReferenceBuild, OnDemandBuild.Queue(), TestDuration);
    }

    public bool TryGetQueued([NotNullWhen(true)] out JobName? testJob)
    {
        return OnDemandBuild.TryGetQueued(out testJob);
    }

    public RequestBuildDiff DoneOnDemand(int buildNumber)
    {
        return new RequestBuildDiff(ReferenceBuild, OnDemandBuild.DoneQueued(buildNumber), TestDuration);
    }

    public RequestBuildDiff RecycleOnDemand(int buildNumber)
    {
        return new RequestBuildDiff(ReferenceBuild, OnDemandBuild.Queue().DoneQueued(buildNumber), TestDuration);

    }

    internal sealed class Serializable : ICustomSerializable<RequestBuildDiff>
    {
        [JsonConstructor]
        private Serializable(RefTestBuildReference.Serializable referenceBuild, RequestTestBuildReference.Serializable onDemandBuild, TimeSpan testDuration)
        {
            ReferenceBuild = referenceBuild;
            OnDemandBuild = onDemandBuild;
            TestDuration = testDuration;
        }
        public Serializable(RequestBuildDiff buildDiff)
        {
            ReferenceBuild = buildDiff.ReferenceBuild.ToSerializable();
            OnDemandBuild = buildDiff.OnDemandBuild.ToSerializable();
            TestDuration = buildDiff.TestDuration;
        }
        public RefTestBuildReference.Serializable ReferenceBuild { get; set; }
        public RequestTestBuildReference.Serializable OnDemandBuild { get; set; }
        public TimeSpan TestDuration { get; set; }

        public RequestBuildDiff FromSerializable()
        {
            var referenceBuild = ReferenceBuild.FromSerializable();
            var onDemandBuild = OnDemandBuild.FromSerializable();
            return new RequestBuildDiff(referenceBuild, onDemandBuild, TestDuration);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}
