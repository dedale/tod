using Serilog;
using System.Text.Json;
using Tod.Git;
using Tod.Jenkins;

namespace Tod.Gerrit;

internal sealed class GerritClient(string gerritServerUrl, string userName, string gerritToken, IApiClient? apiClient = null) : IGerritClient, IDisposable
{
    private const string GerritMagicPrefix = ")]}'";
    private readonly IApiClient _apiClient = apiClient ?? new ApiClient(userName, gerritToken);

    private static string StripGerritPrefix(string jsonString)
    {
        if (jsonString.StartsWith(GerritMagicPrefix, StringComparison.Ordinal))
        {
            jsonString = jsonString.Substring(GerritMagicPrefix.Length);
        }
        return jsonString;
    }

    public async Task<bool> IsKnown(Sha1 commit)
    {
        var url = $"{gerritServerUrl}/a/changes/?q=commit:{commit.Value}";
        try
        {
            var jsonString = await _apiClient.GetStringAsync(url).ConfigureAwait(false);
            var cleanJson = StripGerritPrefix(jsonString);
            using var doc = JsonDocument.Parse(cleanJson);
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
