using NUnit.Framework;
using System.Diagnostics;
using System.Net;
using Tod.Jenkins;

namespace Tod.Tests.Jenkins;

[TestFixture]
internal sealed class ApiClientTests
{
    private sealed class TestHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public TestHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request));
        }
    }

    [Test]
    public void DefaultCtor_Works()
    {
        using var client = new ApiClient("user", "token");
        Assert.That(client, Is.Not.Null);
    }

    [Test]
    public async Task GetAsync_SuccessfulResponse_ReturnsJsonDocument()
    {
        const string jsonResponse = @"{""key"": ""value""}";
        var handler = new TestHttpMessageHandler(req =>
        {
            Assert.That(req.Method, Is.EqualTo(HttpMethod.Get));
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(jsonResponse)
            };
        });

        using var client = new ApiClient(handler, "user", "token");
        var result = await client.GetAsync("http://test.com/api").ConfigureAwait(false);
        Assert.That(result.RootElement.GetProperty("key").GetString(), Is.EqualTo("value"));
    }

    [Test]
    public void GetAsync_ErrorResponse_ThrowsHttpRequestException()
    {
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.NotFound,
            Content = new StringContent("Not Found")
        });

        using var client = new ApiClient(handler, "user", "token");
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetAsync("http://test.com/api").ConfigureAwait(false));
        var httpEx = ex.InnerException as HttpRequestException;
        Assert.That(httpEx, Is.Not.Null);
        Debug.Assert(httpEx != null);
        Assert.That(httpEx.StatusCode, Is.EqualTo(HttpStatusCode.NotFound));
    }

    [Test]
    public async Task GetAsync_401_RetryOnce()
    {
        var calls = 0;
        var handler = new TestHttpMessageHandler(_ =>
        {
            calls++;
            if (calls == 1)
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.Unauthorized,
                    Content = new StringContent("401")
                };
            }
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(@"{""key"": ""value""}")
            };
        });

        using var client = new ApiClient(handler, "user", "token");
        var result = await client.GetAsync("http://test.com/api").ConfigureAwait(false);
        Assert.That(result.RootElement.GetProperty("key").GetString(), Is.EqualTo("value"));
    }

    [Test]
    public async Task GetAsync_401_RetryOnlyOnce()
    {
        var handler = new TestHttpMessageHandler(_ =>
        {
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Unauthorized,
                Content = new StringContent("401")
            };
        });

        using var client = new ApiClient(handler, "user", "token");
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await client.GetAsync("http://test.com/api").ConfigureAwait(false));
        var httpEx = ex.InnerException as HttpRequestException;
        Assert.That(httpEx, Is.Not.Null);
        Debug.Assert(httpEx != null);
        Assert.That(httpEx.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
    }

    [Test]
    public async Task GetStringAsync_SuccessfulResponse_ReturnsString()
    {
        const string response = "Hello World";
        var handler = new TestHttpMessageHandler(_ => new HttpResponseMessage
        {
            StatusCode = HttpStatusCode.OK,
            Content = new StringContent(response)
        });

        using var client = new ApiClient(handler, "user", "token");
        var result = await client.GetStringAsync("http://test.com/api").ConfigureAwait(false);
        Assert.That(result, Is.EqualTo(response));
    }

    [Test]
    public async Task PostAsync_SuccessfulResponse_ReturnsCrumbAndLocation()
    {
        const string crumbJson = @"{""crumbRequestField"":""Jenkins-Crumb"",""crumb"":""abc123""}";
        var handler = new TestHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().EndsWith("crumb"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(crumbJson)
                };
            }

            // Verify crumb header was added
            Assert.That(req.Headers.Contains("Jenkins-Crumb"), Is.True);
            Assert.That(req.Headers.GetValues("Jenkins-Crumb").First(), Is.EqualTo("abc123"));

            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Created,
                Headers = { Location = new Uri("http://test.com/queue/item/123") }
            };
        });

        using var client = new ApiClient(handler, "user", "token");
        var result = await client.PostAsync("http://test.com/crumb", "http://test.com/job/test/build").ConfigureAwait(false);
        Assert.That(result, Is.EqualTo("http://test.com/queue/item/123"));
    }

    [Test]
    public void PostAsync_ErrorResponse_ThrowsHttpRequestException()
    {
        const string crumbJson = @"{""crumbRequestField"":""Jenkins-Crumb"",""crumb"":""abc123""}";
        var handler = new TestHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().EndsWith("crumb"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(crumbJson)
                };
            }
            // Verify crumb header was added
            Assert.That(req.Headers.Contains("Jenkins-Crumb"), Is.True);
            Assert.That(req.Headers.GetValues("Jenkins-Crumb").First(), Is.EqualTo("abc123"));
            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("Bad Request")
            };
        });
        using var client = new ApiClient(handler, "user", "token");
        var ex = Assert.ThrowsAsync<InvalidOperationException>(async () => await client.PostAsync("http://test.com/crumb", "http://test.com/job/test/build").ConfigureAwait(false));
        var httpEx = ex.InnerException as HttpRequestException;
        Assert.That(httpEx, Is.Not.Null);
        Debug.Assert(httpEx != null);
        Assert.That(httpEx.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
    }

    [Test]
    public async Task PostAsync_NullLocation()
    {
        const string crumbJson = @"{""crumbRequestField"":""Jenkins-Crumb"",""crumb"":""abc123""}";
        var handler = new TestHttpMessageHandler(req =>
        {
            if (req.RequestUri!.ToString().EndsWith("crumb"))
            {
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent(crumbJson)
                };
            }

            // Verify crumb header was added
            Assert.That(req.Headers.Contains("Jenkins-Crumb"), Is.True);
            Assert.That(req.Headers.GetValues("Jenkins-Crumb").First(), Is.EqualTo("abc123"));

            return new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.Created,
                Headers = { Location = (Uri)null! }
            };
        });

        using var client = new ApiClient(handler, "user", "token");
        var result = await client.PostAsync("http://test.com/crumb", "http://test.com/job/test/build").ConfigureAwait(false);
        Assert.That(result, Is.Null);
    }
}
