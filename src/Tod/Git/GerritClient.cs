using Serilog;
using Tod.Git;
using Tod.Jenkins;

namespace Tod.Gerrit;

internal sealed class GerritClient(string gerritServerUrl, string gerritToken, IApiClient? apiClient = null) : IGerritClient, IDisposable
{
    private readonly IApiClient _apiClient = apiClient ?? new ApiClient(gerritToken);

    public async Task<bool> IsKnown(Sha1 commit)
    {
        var url = $"{gerritServerUrl}/a/changes/?q=commit:{commit.Value}";
        try
        {
            var doc = await _apiClient.GetAsync(url).ConfigureAwait(false);
            var changes = doc.RootElement.EnumerateArray();
            var hasChanges = changes.Any();
            if (!hasChanges)
            {
                Log.Debug("Commit {Commit} not found in Gerrit", commit);
            }
            return hasChanges;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to query Gerrit for commit {Commit}", commit);
            return false;
        }
    }

    public void Dispose()
    {
        _apiClient.Dispose();
    }
}
