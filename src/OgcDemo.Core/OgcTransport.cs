using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace OgcDemo.Core;

public sealed class OgcTransport : IDisposable
{
    private readonly HttpClient _client;
    private readonly ServiceOptions _options;
    private readonly Uri _origin;
    public OgcTransport(ServiceOptions options, HttpMessageHandler? testHandler = null, bool allowLoopbackHttp = false)
    {
        _options = options; _origin = new Uri(options.Url);
        if (!(_origin.Scheme == "https" || allowLoopbackHttp && _origin.IsLoopback))
            throw new DemoException("OGC connections require HTTPS.");
        _client = new HttpClient(testHandler ?? new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds) };
        _client.DefaultRequestHeaders.UserAgent.ParseAdd("OgcDemo/1.0");
        if (options.Authentication == "Basic")
        {
            var user = Environment.GetEnvironmentVariable(options.CredentialPrefix + "_USERNAME");
            var password = Environment.GetEnvironmentVariable(options.CredentialPrefix + "_PASSWORD");
            if (string.IsNullOrEmpty(user) || password is null || user.Contains(':'))
                throw new DemoException("Set the configured credential-prefix _USERNAME and _PASSWORD environment variables.");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes(user + ":" + password)));
        }
    }
    public async Task<byte[]> GetAsync(string url, CancellationToken token = default)
    {
        var target = new Uri(url);
        // Capabilities can advertise other endpoints. Do not forward credentials to a different origin.
        if (target.Scheme != _origin.Scheme || target.Host != _origin.Host || target.Port != _origin.Port || target.UserInfo.Length != 0)
            throw new DemoException("An advertised endpoint has a different origin. Configure a same-origin service gateway.");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
        var requestToken = deadline.Token;
        try
        {
            using var response = await _client.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new DemoException($"OGC HTTP {(int)response.StatusCode}. Check endpoint, permissions, and credentials.");
            if (response.Content.Headers.ContentLength > _options.MaxResponseBytes)
                throw new DemoException("Response exceeds MaxResponseBytes. Zoom in or raise the configured limit.");
            using var input = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
            using var output = new MemoryStream(); var buffer = new byte[16384]; int read;
            while ((read = await input.ReadAsync(buffer, requestToken).ConfigureAwait(false)) > 0)
            {
                if (output.Length + read > _options.MaxResponseBytes) throw new DemoException("Response exceeds MaxResponseBytes.");
                output.Write(buffer, 0, read);
            }
            var bytes = output.ToArray();
            if (response.Content.Headers.ContentType?.MediaType?.Contains("xml", StringComparison.OrdinalIgnoreCase) == true ||
                bytes.AsSpan().TrimStart((byte)' ').StartsWith("<"u8))
            {
                var xml = ParseXml(bytes);
                if (xml.Root?.Name.LocalName is "ExceptionReport" or "ServiceExceptionReport" or "ServiceException")
                    throw new DemoException("OGC service exception. Check capabilities, layer name, CRS, format, and permissions.");
            }
            return bytes;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new DemoException("OGC request timed out."); }
        catch (HttpRequestException) { throw new DemoException("OGC connection failed. Check network access and the trusted certificate chain."); }
    }
    public static XDocument ParseXml(byte[] bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 33554432 });
            return XDocument.Load(reader);
        }
        catch (XmlException) { throw new DemoException("The service returned malformed or unsupported XML."); }
    }
    public static string Query(string url, params (string Key, string Value)[] parameters)
    {
        var builder = new UriBuilder(url);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in builder.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = pair.Split('=', 2); values[Uri.UnescapeDataString(kv[0])] = kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : "";
        }
        foreach (var (key, value) in parameters) values[key] = value;
        builder.Query = string.Join("&", values.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)));
        return builder.Uri.AbsoluteUri;
    }
    public void Dispose() => _client.Dispose();
}
