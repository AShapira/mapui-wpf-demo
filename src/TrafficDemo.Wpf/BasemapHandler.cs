using System.Net.Http;
using TrafficDemo.Core;
namespace TrafficDemo.Wpf;
/// <summary>Count actual image HTTP responses independently of capabilities connection status.</summary>
internal sealed class BasemapHandler(TrafficMetrics metrics) : DelegatingHandler(new HttpClientHandler { AllowAutoRedirect = false })
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode && response.Content.Headers.ContentType?.MediaType?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            Interlocked.Increment(ref metrics.BasemapImageResponses);
        return response;
    }
}
