using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Tod.Jenkins;

internal abstract class RequestBuildReference : IWithCustomSerialization<RequestBuildReference.Serializable>, IEquatable<RequestBuildReference>
{
    public abstract void Match(Action<JobName> onPending, Action<BuildReference> onTriggered, Action<BuildReference> onDone);
    public abstract T Match<T>(Func<JobName, T> onPending, Func<BuildReference, T> onTriggered, Func<BuildReference, T> onDone);

    public abstract JobName JobName { get; }

    private sealed class Pending(JobName jobName) : RequestBuildReference
    {
        public override void Match(Action<JobName> onPending, Action<BuildReference> onTriggered, Action<BuildReference> _) => onPending(jobName);
        public override T Match<T>(Func<JobName, T> onPending, Func<BuildReference, T> onTriggered, Func<BuildReference, T> _) => onPending(jobName);

        public override JobName JobName => jobName;
    }

    private sealed class Triggered(BuildReference reference) : RequestBuildReference
    {
        public override void Match(Action<JobName> _, Action<BuildReference> onTriggered, Action<BuildReference> onDone) => onTriggered(reference);
        public override T Match<T>(Func<JobName, T> _, Func<BuildReference, T> onTriggered, Func<BuildReference, T> onDone) => onTriggered(reference);

        public override JobName JobName => reference.JobName;
    }

    private sealed class Done(BuildReference reference) : RequestBuildReference
    {
        public override void Match(Action<JobName> _, Action<BuildReference> onTriggered, Action<BuildReference> onDone) => onDone(reference);
        public override T Match<T>(Func<JobName, T> _, Func<BuildReference, T> onTriggered, Func<BuildReference, T> onDone) => onDone(reference);

        public override JobName JobName => reference.JobName;
    }

    public static RequestBuildReference Create(JobName jobName) => new Pending(jobName);

    public bool TryGetPendingReference([NotNullWhen(true)] out JobName? jobName)
    {
        JobName? pending = null;
        var isPending = Match(
            onPending: jobName =>
            {
                pending = jobName;
                return true;
            },
            onTriggered: _ => false,
            onDone: _ => false
        );
        jobName = pending;
        return isPending;
    }

    public bool TryGetTriggered([NotNullWhen(true)] out BuildReference? buildReference)
    {
        BuildReference? triggered = null;
        var isTriggered = Match(
            onPending: _ => false,
            onTriggered: reference =>
            {
                triggered = reference;
                return true;
            },
            onDone: _ => false
        );
        buildReference = triggered;
        return isTriggered;
    }

    public RequestBuildReference Trigger(int buildNumber) => Match(
        onPending: jobName => new Triggered(new BuildReference(jobName, buildNumber)),
        onTriggered: _ => throw new InvalidOperationException("Already triggered"),
        onDone: _ => throw new InvalidOperationException("Already done")
    );

    public RequestBuildReference DoneReference(int buildNumber) => Match(
        onPending: jobName => new Done(new BuildReference(jobName, buildNumber)),
        onTriggered: _ => throw new InvalidOperationException("Reference builds are not triggered"),
        onDone: _ => throw new InvalidOperationException("Already done")
    );

    public RequestBuildReference DoneTriggered() => Match(
        onPending: _ => throw new InvalidOperationException("Not triggered"),
        onTriggered: reference => new Done(reference),
        onDone: _ => throw new InvalidOperationException("Already done")
    );

    public bool IsDone => Match(
        onPending: _ => false,
        onTriggered: _ => false,
        onDone: _ => true
    );

    internal sealed class Serializable : ICustomSerializable<RequestBuildReference>
    {
        [JsonConstructor]
        private Serializable(JobName? pending, BuildReference? triggered, BuildReference? done)
        {
            Pending = pending;
            Triggered = triggered;
            Done = done;
        }

        public Serializable(RequestBuildReference reference)
        {
            reference.Match(
                onPending: jobName => Pending = jobName,
                onTriggered: buildRef => Triggered = buildRef,
                onDone: buildRef => Done = buildRef
            );
        }

        public JobName? Pending { get; set; }
        public BuildReference? Triggered { get; set; }
        public BuildReference? Done { get; set; }

        public RequestBuildReference FromSerializable()
        {
            if (Pending is not null)
            {
                return Create(Pending);
            }
            if (Triggered is not null)
            {
                return new Triggered(Triggered);
            }
            return new Done(Done!);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }

    public bool Equals(RequestBuildReference? other)
    {
        return Match(
            onPending: jobName =>
            {
                return other!.Match(
                    onPending: otherJobName => jobName.Equals(otherJobName),
                    onTriggered: _ => false,
                    onDone: _ => false
                );
            },
            onTriggered: reference =>
            {
                return other!.Match(
                    onPending: _ => false,
                    onTriggered: otherReference => reference.Equals(otherReference),
                    onDone: _ => false
                );
            },
            onDone: reference =>
            {
                return other!.Match(
                    onPending: _ => false,
                    onTriggered: _ => false,
                    onDone: otherReference => reference.Equals(otherReference)
                );
            }
        );
    }
}
