using Tod.Core;
using Tod.Jenkins;
using Tod.Tests.Jenkins;

namespace Tod.Tests.Core;

internal sealed class Dummy(List<RequestBuildReference> references) : IWithCustomSerialization<Dummy.Serializable>
{
    public List<RequestBuildReference> References { get; } = references;

    public Serializable ToSerializable()
    {
        return new Serializable
        {
            References = [.. References.Select(r => r.ToSerializable())]
        };
    }

    internal sealed class Serializable : ICustomSerializable<Dummy>
    {
        public RequestBuildReference.Serializable[] References { get; set; } = [];

        public Dummy FromSerializable()
        {
            return new Dummy([.. References.Select(r => r.FromSerializable())]);
        }
    }

    public static Dummy New()
    {
        return new Dummy([
            RequestBuildReference.Create(new JobName("MyJob")).Trigger(RandomData.NextBuildNumber).DoneTriggered(),
            RequestBuildReference.Create(new JobName("MyTestJob")),
        ]);
    }

    public void SaveNew(string path)
    {
        using var lockedJson = new LockedDummy(this, path, "Save new dummy");
        lockedJson.Value.Save();
    }
}

// Wrapper to simplify usage in tests
internal sealed class LockedDummy : IDisposable
{
    public ILockedJson<Dummy> Value { get; }
    
    public LockedDummy(Dummy dummy, string path, string reason)
    {
        Value = LockedJsonSerializer<Dummy, Dummy.Serializable>.New(dummy, path, reason, true);
    }
    
    private LockedDummy(ILockedJson<Dummy> lockedJson)
    {
        Value = lockedJson;
    }
    
    public static LockedDummy Load(string path, string reason)
    {
        return new LockedDummy(LockedJsonSerializer<Dummy, Dummy.Serializable>.Load(path, reason, true));
    }
    
    public static Dummy LoadUnlocked(string path)
    {
        return LockedJsonSerializer<Dummy, Dummy.Serializable>.LoadUnlocked(path);
    }

    public void Dispose()
    {
        Value.Dispose();
    }
}
