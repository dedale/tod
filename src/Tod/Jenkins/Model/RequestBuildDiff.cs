using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Tod.Jenkins;

internal sealed class JobDiff(JobName referenceJob, JobName onDemandJob)
{
    public JobName ReferenceJob { get; } = referenceJob;
    public JobName OnDemandJob { get; } = onDemandJob;
}

internal sealed class RequestBuildDiff : IWithCustomSerialization<RequestBuildDiff.Serializable>
{
    public RequestBuildDiff(JobName referenceJobName, JobName onDemandJobName)
        : this(RefTestBuildReference.Create(referenceJobName), RequestTestBuildReference.Create(onDemandJobName))
    {
    }

    private RequestBuildDiff(RefTestBuildReference referenceBuild, RequestTestBuildReference onDemandBuild)
    {
        ReferenceBuild = referenceBuild;
        OnDemandBuild = onDemandBuild;
    }

    public RefTestBuildReference ReferenceBuild { get; }
    public RequestTestBuildReference OnDemandBuild { get; }

    public bool IsDone => ReferenceBuild.IsDone && OnDemandBuild.IsDone;

    public bool TryGetPendingReference([NotNullWhen(true)] out JobName? jobName)
    {
        return ReferenceBuild.TryGetPendingReference(out jobName);
    }

    public RequestBuildDiff DoneReference(int buildNumber)
    {
        return new RequestBuildDiff(ReferenceBuild.DoneReference(buildNumber), OnDemandBuild);
    }

    public RequestBuildDiff QueueOnDemand()
    {
        return new RequestBuildDiff(ReferenceBuild, OnDemandBuild.Queue());
    }

    public bool TryGetQueued([NotNullWhen(true)] out JobName? testJob)
    {
        return OnDemandBuild.TryGetQueued(out testJob);
    }

    public RequestBuildDiff DoneOnDemand(int buildNumber)
    {
        return new RequestBuildDiff(ReferenceBuild, OnDemandBuild.DoneQueued(buildNumber));
    }

    public RequestBuildDiff RecycleOnDemand(int buildNumber)
    {
        return new RequestBuildDiff(ReferenceBuild, OnDemandBuild.Queue().DoneQueued(buildNumber));

    }

    internal sealed class Serializable : ICustomSerializable<RequestBuildDiff>
    {
        [JsonConstructor]
        private Serializable(RefTestBuildReference.Serializable referenceBuild, RequestTestBuildReference.Serializable onDemandBuild)
        {
            ReferenceBuild = referenceBuild;
            OnDemandBuild = onDemandBuild;
        }
        public Serializable(RequestBuildDiff buildDiff)
        {
            ReferenceBuild = buildDiff.ReferenceBuild.ToSerializable();
            OnDemandBuild = buildDiff.OnDemandBuild.ToSerializable();
        }
        public RefTestBuildReference.Serializable ReferenceBuild { get; set; }
        public RequestTestBuildReference.Serializable OnDemandBuild { get; set; }

        public RequestBuildDiff FromSerializable()
        {
            var referenceBuild = ReferenceBuild.FromSerializable();
            var onDemandBuild = OnDemandBuild.FromSerializable();
            return new RequestBuildDiff(referenceBuild, onDemandBuild);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}
