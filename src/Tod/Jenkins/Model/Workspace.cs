using System.Text.Json.Serialization;
using Tod.Core;

namespace Tod.Jenkins;

internal sealed class Workspace(string jsonPath, List<BranchReference> branchReferences, OnDemandBuilds onDemandBuilds, OnDemandRequests onDemandRequests) : IWithCustomSerialization<Workspace.Serializable>
{
    public string JsonPath { get; } = jsonPath;
    public List<BranchReference> BranchReferences { get; } = branchReferences;
    public OnDemandBuilds OnDemandBuilds { get; } = onDemandBuilds;
    public OnDemandRequests OnDemandRequests { get; } = onDemandRequests;

    public static Workspace New(string path, JobGroups jobGroups)
    {
        var branchReferences = new List<BranchReference>();
        var onDemandRootBuilds = new List<BuildCollection<RootBuild>>();
        foreach (var rootGroup in jobGroups.ByRoot.Values)
        {
            onDemandRootBuilds.Add(new BuildCollection<RootBuild>(rootGroup.OnDemandJob));
            branchReferences.AddRange(
                rootGroup.ReferenceJobByBranch.Select(kvp => new BranchReference(kvp.Key, kvp.Value))
            );
        }
        var onDemandBuilds = new OnDemandBuilds(onDemandRootBuilds, []);
        var onDemandRequests = new OnDemandRequests(Path.Combine(Path.GetDirectoryName(path)!, "Requests"));

        var jobNamesByBranch = jobGroups.ByTest.Values
            .SelectMany(x => x.ReferenceJobByBranch)
            .GroupBy(kvp => kvp.Key)
            .ToDictionary(g => g.Key, g => g.Select(kvp => kvp.Value).ToList());
        foreach (var branchReference in branchReferences)
        {
            if (jobNamesByBranch.TryGetValue(branchReference.BranchName, out var jobNames))
            {
                foreach (var jobName in jobNames)
                {
                    branchReference.TryAdd(jobName);
                }
            }
        }

        var onDemandJobNames = jobGroups.ByTest.Values
            .Select(x => x.OnDemandJob)
            .ToList();
        foreach (var jobName in onDemandJobNames)
        {
            onDemandBuilds.TryAdd(jobName);
        }

        var workspace = new Workspace(path, branchReferences, onDemandBuilds, onDemandRequests);
        using var lockedJson = LockedJsonSerializer<Workspace, Serializable>.New(workspace, path, "Cached New", false);
        lockedJson.Save();
        return workspace;
    }

    public static ILockedJson<Workspace> Load(string path, string reason)
    {
        return LockedJsonSerializer<Workspace, Serializable>.Load(path, reason, false);
    }

    public static Workspace LoadUnlocked(string path)
    {
        return LockedJsonSerializer<Workspace, Serializable>.LoadUnlocked(path);
    }

    internal sealed class Serializable : ICustomSerializable<Workspace>
    {
        [JsonConstructor]
        private Serializable(string jsonPath, List<BranchReference.Serializable> branchReferences, OnDemandBuilds.Serializable onDemandBuilds, OnDemandRequests onDemandRequests)
        {
            JsonPath = jsonPath;
            BranchReferences = branchReferences;
            OnDemandBuilds = onDemandBuilds;
            OnDemandRequests = onDemandRequests;
        }
        public Serializable(Workspace workspace)
            : this(workspace.JsonPath, [.. workspace.BranchReferences.Select(b => b.ToSerializable())], workspace.OnDemandBuilds.ToSerializable(), workspace.OnDemandRequests)
        {
        }
        public string JsonPath { get; set; }
        public List<BranchReference.Serializable> BranchReferences { get; set; }
        public OnDemandBuilds.Serializable OnDemandBuilds { get; set; }
        public OnDemandRequests OnDemandRequests { get; set; }
        public Workspace FromSerializable()
        {
            return new Workspace(JsonPath, [.. BranchReferences.Select(b => b.FromSerializable())], OnDemandBuilds.FromSerializable(), OnDemandRequests);
        }
    }

    public Serializable ToSerializable()
    {
        return new Serializable(this);
    }
}

