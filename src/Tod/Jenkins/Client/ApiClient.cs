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

    private async Task<JsonDocument> GetAsync(string url, bool retry401)
    {
        try
        {
            var response = await _httpClient.GetAsync(url).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var doc = JsonDocument.Parse(json);
            return doc;
        }
        catch (Exception ex)
        {
            if (retry401 && ex is HttpRequestException httpEx && httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return await GetAsync(url, false).ConfigureAwait(false);
            }
            throw new InvalidOperationException(url, ex);
        }
    }

    public Task<JsonDocument> GetAsync(string url)
    {
        return GetAsync(url, true);
    }

    public Task<string> GetStringAsync(string url)
    {
        return _httpClient.GetStringAsync(url);
    }

    public async Task<string> PostAsync(string crumbUrl, string url)
    {
        try
        {
            var crumbDoc = await GetAsync(crumbUrl).ConfigureAwait(false);
            string crumbField = crumbDoc.RootElement.GetProperty("crumbRequestField").GetString()!;
            string crumbValue = crumbDoc.RootElement.GetProperty("crumb").GetString()!;
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add(crumbField, crumbValue);

            var response = await _httpClient.SendAsync(request).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            return response.Headers.Location?.ToString()!;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(url, ex);
        }
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
