using MaNoir.Agents.Sarah.Awtrix;
using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MaNoir.HomeAutomation.UnitTests;

[TestClass]
public sealed class AwtrixHttpClientTests
{
    [TestMethod]
    public async Task ClearDisplayAsync_ShouldPostEmptyBodyToDisplayApp()
    {
        RecordingHandler handler = new();
        using HttpClient httpClient = new(handler);
        AwtrixHttpClient client = new("192.168.1.50", httpClient);

        await client.ClearDisplayAsync();

        Assert.AreEqual(HttpMethod.Post, handler.Method);
        Assert.AreEqual("/api/custom", handler.RequestUri.AbsolutePath);
        Assert.AreEqual("?name=manoir_display", handler.RequestUri.Query);
        Assert.IsNull(handler.Body);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public HttpMethod Method { get; private set; }

        public Uri RequestUri { get; private set; }

        public string Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Method = request.Method;
            RequestUri = request.RequestUri;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}