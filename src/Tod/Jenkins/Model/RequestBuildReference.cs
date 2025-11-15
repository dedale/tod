using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Tod.Git;

namespace Tod.Jenkins;

// Ref Root Build Reference : always Done = BuildReference
// Ref Test Build Reference : Pending -> Done
// OnDemand/Request Root Build Reference : Queued -> Done or Done (if reused)
// OnDemand/Request Test Build Reference : Pending -> Queued -> Done or Done (if reused)

internal abstract class RefTestBuildReference : IWithCustomSerialization<RefTestBuildReference.Serializable>, IEquatable<RefTestBuildReference>
{
    public abstract void Match(Action<JobName> onPending, Action<BuildReference> onDone);
    public abstract T Match<T>(Func<JobName, T> onPending, Func<BuildReference, T> onDone);

    private sealed class Pending(JobName jobName) : RefTestBuildReference
    {
        public override void Match(Action<JobName> onPending, Action<BuildReference> _) => onPending(jobName);
        public override T Match<T>(Func<JobName, T> onPending, Func<BuildReference, T> _) => onPending(jobName);
    }

    private sealed class Done(BuildReference reference) : RefTestBuildReference
    {
        public override void Match(Action<JobName> _, Action<BuildReference> onDone) => onDone(reference);
        public override T Match<T>(Func<JobName, T> _, Func<BuildReference, T> onDone) => onDone(reference);
    }

    public static RefTestBuildReference Create(JobName jobName) => new Pending(jobName);

    public bool IsDone => Match(
        onPending: _ => false,
        onDone: _ => true
    );

    internal sealed class Serializable : ICustomSerializable<RefTestBuildReference>
    {
        [JsonConstructor]
        private Serializable(JobName? pending, BuildReference? done)
        {
            Pending = pending;
            Done = done;
        }
        public Serializable(RefTestBuildReference reference)
        {
            reference.Match(
                onPending: job => Pending = job,
                onDone: buildRef => Done = buildRef
            );
        }
        public JobName? Pending { get; set; }
        public BuildReference? Done { get; set; }
        public RefTestBuildReference FromSerializable()
        {
            if (Pending is not null)
            {
                return new Pending(Pending);
            }
            return new Done(Done!);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }

    public bool TryGetPendingReference([NotNullWhen(true)] out JobName? jobName)
    {
        JobName? pending = null;
        var isPending = Match(
            onPending: job =>
            {
                pending = job;
                return true;
            },
            onDone: _ => false
        );
        jobName = pending;
        return isPending;
    }

    public RefTestBuildReference DoneReference(int buildNumber) => Match(
        onPending: jobName => new Done(new BuildReference(jobName, buildNumber)),
        onDone: _ => throw new InvalidOperationException("Already done")
    );

    public bool Equals(RefTestBuildReference? other)
    {
        return Match(
            onPending: jobName =>
            {
                return other!.Match(
                    onPending: otherJob => jobName.Equals(otherJob),
                    onDone: _ => false
                );
            },
            onDone: reference =>
            {
                return other!.Match(
                    onPending: _ => false,
                    onDone: otherReference => reference.Equals(otherReference)
                );
            }
        );
    }
}

internal abstract class RequestRootBuildReference : IWithCustomSerialization<RequestRootBuildReference.Serializable>, IEquatable<RequestRootBuildReference>
{
    public abstract void Match(Action<JobName, Sha1> onQueued, Action<BuildReference> onDone);
    public abstract T Match<T>(Func<JobName, Sha1, T> onQueued, Func<BuildReference, T> onDone);

    public abstract JobName JobName { get; }

    private sealed class Queued(JobName jobName, Sha1 commit) : RequestRootBuildReference
    {
        public override void Match(Action<JobName, Sha1> onQueued, Action<BuildReference> _) => onQueued(jobName, commit);
        public override T Match<T>(Func<JobName, Sha1, T> onQueued, Func<BuildReference, T> _) => onQueued(jobName, commit);

        public override JobName JobName => jobName;
    }

    private sealed class Done(BuildReference reference) : RequestRootBuildReference
    {
        public override void Match(Action<JobName, Sha1> onQueued, Action<BuildReference> onDone) => onDone(reference);
        public override T Match<T>(Func<JobName, Sha1, T> onQueued, Func<BuildReference, T> onDone) => onDone(reference);

        public override JobName JobName => reference.JobName;
    }

    public static RequestRootBuildReference Queue(JobName jobName, Sha1 commit) => new Queued(jobName, commit);

    public RequestRootBuildReference DoneQueued(int buildNumber) => Match(
        onQueued: (jobName, _) => new Done(new BuildReference(jobName, buildNumber)),
        onDone: _ => throw new InvalidOperationException("Already done")
    );

    public bool IsDone => Match(
        onQueued: (_, _) => false,
        onDone: _ => true
    );

    public int BuildNumber => Match(
        onQueued: (_, _) => throw new InvalidOperationException("Not done"),
        onDone: reference => reference.BuildNumber
    );

    internal sealed class Serializable : ICustomSerializable<RequestRootBuildReference>
    {
        [JsonConstructor]
        private Serializable(KeyValuePair<JobName, Sha1>? queued, BuildReference? done)
        {
            Queued = queued;
            Done = done;
        }

        public Serializable(RequestRootBuildReference reference)
        {
            reference.Match(
                onQueued: (job, commit) => Queued = new KeyValuePair<JobName, Sha1>(job, commit),
                onDone: buildRef => Done = buildRef
            );
        }

        // (JobName Job, Sha1 Commit)? not supported by System.Text.Json
        public KeyValuePair<JobName, Sha1>? Queued { get; set; }
        public BuildReference? Done { get; set; }

        public RequestRootBuildReference FromSerializable()
        {
            if (Queued is not null)
            {
                return new Queued(Queued.Value.Key, Queued.Value.Value);
            }
            return new Done(Done!);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }

    public bool Equals(RequestRootBuildReference? other)
    {
        if (other is null)
        {
            return false;
        }
        return Match(
            onQueued: (jobName, commit) =>
            {
                return other!.Match(
                    onQueued: (otherJob, otherCommit) => jobName.Equals(otherJob) && commit.Equals(otherCommit),
                    onDone: _ => false
                );
            },
            onDone: reference =>
            {
                return other!.Match(
                    onQueued: (_, _) => false,
                    onDone: otherReference => reference.Equals(otherReference)
                );
            }
        );
    }
}

internal abstract class RequestTestBuildReference : IWithCustomSerialization<RequestTestBuildReference.Serializable>, IEquatable<RequestTestBuildReference>
{
    public abstract void Match(Action<JobName> onPending, Action<JobName> onQueued, Action<BuildReference> onDone);
    public abstract T Match<T>(Func<JobName, T> onPending, Func<JobName, T> onQueued, Func<BuildReference, T> onDone);

    public abstract JobName JobName { get; }

    private sealed class Pending(JobName jobName) : RequestTestBuildReference
    {
        public override void Match(Action<JobName> onPending, Action<JobName> onQueued, Action<BuildReference> _) => onPending(jobName);
        public override T Match<T>(Func<JobName, T> onPending, Func<JobName, T> onQueued, Func<BuildReference, T> _) => onPending(jobName);

        public override JobName JobName => jobName;
    }

    private sealed class Queued(JobName jobName) : RequestTestBuildReference
    {
        public override void Match(Action<JobName> onPending, Action<JobName> onQueued, Action<BuildReference> _) => onQueued(jobName);
        public override T Match<T>(Func<JobName, T> onPending, Func<JobName, T> onQueued, Func<BuildReference, T> _) => onQueued(jobName);

        public override JobName JobName => jobName;
    }

    private sealed class Done(BuildReference reference) : RequestTestBuildReference
    {
        public override void Match(Action<JobName> _, Action<JobName> onQueued, Action<BuildReference> onDone) => onDone(reference);
        public override T Match<T>(Func<JobName, T> _, Func<JobName, T> onQueued, Func<BuildReference, T> onDone) => onDone(reference);

        public override JobName JobName => reference.JobName;
    }

    public static RequestTestBuildReference Create(JobName jobName) => new Pending(jobName);

    public bool TryGetPendingReference([NotNullWhen(true)] out JobName? jobName)
    {
        JobName? pending = null;
        var isPending = Match(
            onPending: jobName =>
            {
                pending = jobName;
                return true;
            },
            onQueued: _ => false,
            onDone: _ => false
        );
        jobName = pending;
        return isPending;
    }

    public bool TryGetQueued([NotNullWhen(true)] out JobName? jobName)
    {
        JobName? queued = null;
        var isQueued = Match(
            onPending: _ => false,
            onQueued: job =>
            {
                queued = job;
                return true;
            },
            onDone: _ => false
        );
        jobName = queued;
        return isQueued;
    }

    public RequestTestBuildReference Queue() => Match(
        onPending: jobName => new Queued(jobName),
        onQueued: _ => throw new InvalidOperationException("Already queued"),
        onDone: _ => throw new InvalidOperationException("Already done")
    );

    public RequestTestBuildReference DoneQueued(int buildNumber) => Match(
        onPending: _ => throw new InvalidOperationException("Not triggered"),
        onQueued: jobName => new Done(new BuildReference(jobName, buildNumber)),
        onDone: _ => throw new InvalidOperationException("Already done")
    );

    public bool IsDone => Match(
        onPending: _ => false,
        onQueued: _ => false,
        onDone: _ => true
    );

    internal sealed class Serializable : ICustomSerializable<RequestTestBuildReference>
    {
        [JsonConstructor]
        private Serializable(JobName? pending, JobName? queued, BuildReference? done)
        {
            Pending = pending;
            Queued = queued;
            Done = done;
        }

        public Serializable(RequestTestBuildReference reference)
        {
            reference.Match(
                onPending: job => Pending = job,
                onQueued: job => Queued = job,
                onDone: buildRef => Done = buildRef
            );
        }

        public JobName? Pending { get; set; }
        public JobName? Queued { get; set; }
        public BuildReference? Done { get; set; }

        public RequestTestBuildReference FromSerializable()
        {
            if (Pending is not null)
            {
                return Create(Pending);
            }
            if (Queued is not null)
            {
                return new Queued(Queued);
            }
            return new Done(Done!);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }

    public bool Equals(RequestTestBuildReference? other)
    {
        return Match(
            onPending: jobName =>
            {
                return other!.Match(
                    onPending: otherJob => jobName.Equals(otherJob),
                    onQueued: _ => false,
                    onDone: _ => false
                );
            },
            onQueued: jobName =>
            {
                return other!.Match(
                    onPending: _ => false,
                    onQueued: otherJob => jobName.Equals(otherJob),
                    onDone: _ => false
                );
            },
            onDone: reference =>
            {
                return other!.Match(
                    onPending: _ => false,
                    onQueued: _ => false,
                    onDone: otherReference => reference.Equals(otherReference)
                );
            }
        );
    }
}
