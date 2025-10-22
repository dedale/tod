using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Tod.Jenkins;

internal interface IApiClient : IDisposable
{
    Task<JsonDocument> GetAsync(string url);
    Task<string> GetStringAsync(string url);
    Task<string> PostAsync(string crumbUrl, string url);
}

internal sealed class ApiClient : IApiClient
{
    private readonly HttpClient _httpClient;

    private static HttpMessageHandler DefaultHandler => new HttpClientHandler
    {
        UseDefaultCredentials = true
    };

    public ApiClient(string userToken)
        : this(DefaultHandler, userToken)
    {
    }

    internal ApiClient(HttpMessageHandler handler, string userToken)
    {
        _httpClient = new HttpClient(handler);
        string base64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(userToken));
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", base64);
    }

    public async Task<JsonDocument> GetAsync(string url)
    {
        var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var doc = JsonDocument.Parse(json);
        return doc;
    }

    public Task<string> GetStringAsync(string url)
    {
        return _httpClient.GetStringAsync(url);
    }

    public async Task<string> PostAsync(string crumbUrl, string url)
    {
        var crumbDoc = await GetAsync(crumbUrl).ConfigureAwait(false);
        string crumbField = crumbDoc.RootElement.GetProperty("crumbRequestField").GetString()!;
        string crumbValue = crumbDoc.RootElement.GetProperty("crumb").GetString()!;
        _httpClient.DefaultRequestHeaders.Add(crumbField, crumbValue);

        var response = await _httpClient.PostAsync(url, null).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return response.Headers.Location?.ToString()!;
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
