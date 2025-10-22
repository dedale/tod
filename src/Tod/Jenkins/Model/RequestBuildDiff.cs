using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal sealed class RootDiff(JobName referenceJob, JobName onDemandJob)
{
    public JobName ReferenceJob { get; } = referenceJob;
    public JobName OnDemandJob { get; } = onDemandJob;
}

internal sealed class RequestBuildDiff : IWithCustomSerialization<RequestBuildDiff.Serializable>
{
    public RequestBuildDiff(JobName referenceJobName, JobName onDemandJobName)
        : this(RequestBuildReference.Create(referenceJobName), RequestBuildReference.Create(onDemandJobName))
    {
    }

    private RequestBuildDiff(RequestBuildReference referenceBuild, RequestBuildReference onDemandBuild)
    {
        ReferenceBuild = referenceBuild;
        OnDemandBuild = onDemandBuild;
    }

    public RequestBuildReference ReferenceBuild { get; }
    public RequestBuildReference OnDemandBuild { get; }

    public bool IsDone => ReferenceBuild.IsDone && OnDemandBuild.IsDone;

    public bool TryGetPendingReference([NotNullWhen(true)] out JobName? jobName)
    {
        return ReferenceBuild.TryGetPendingReference(out jobName);
    }

    public RequestBuildDiff DoneReference(int buildNumber)
    {
        return new RequestBuildDiff(ReferenceBuild.DoneReference(buildNumber), OnDemandBuild);
    }

    public RequestBuildDiff TriggerOnDemand(int buildNumber)
    {
        return new RequestBuildDiff(ReferenceBuild, OnDemandBuild.Trigger(buildNumber));
    }

    public bool TryGetTriggered([NotNullWhen(true)] out BuildReference? testBuild)
    {
        return OnDemandBuild.TryGetTriggered(out testBuild);
    }

    public RequestBuildDiff DoneOnDemand()
    {
        return new RequestBuildDiff(ReferenceBuild, OnDemandBuild.DoneTriggered());
    }

    internal sealed class Serializable : ICustomSerializable<RequestBuildDiff>
    {
        [JsonConstructor]
        private Serializable(RequestBuildReference.Serializable referenceBuild, RequestBuildReference.Serializable onDemandBuild)
        {
            ReferenceBuild = referenceBuild;
            OnDemandBuild = onDemandBuild;
        }
        public Serializable(RequestBuildDiff buildDiff)
        {
            ReferenceBuild = buildDiff.ReferenceBuild.ToSerializable();
            OnDemandBuild = buildDiff.OnDemandBuild.ToSerializable();
        }
        public RequestBuildReference.Serializable ReferenceBuild { get; set; }
        public RequestBuildReference.Serializable OnDemandBuild { get; set; }

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
