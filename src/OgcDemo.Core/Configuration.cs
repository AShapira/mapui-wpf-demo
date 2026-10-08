namespace OgcDemo.Core;

public sealed class DemoOptions
{
    // Configuration binding appends to initialized arrays; keep this empty until JSON is bound.
    public double[] InitialExtent { get; set; } = [];
    public List<ServiceOptions> Services { get; set; } = [];
}

public sealed class ServiceOptions
{
    public string Name { get; set; } = "Internal service";
    public string Kind { get; set; } = "WMS";
    public bool Enabled { get; set; }
    public string Url { get; set; } = "https://ogc.example.invalid/wms";
    public string Version { get; set; } = "1.3.0";
    public string Layer { get; set; } = "demo:features";
    public string Style { get; set; } = "";
    public string Crs { get; set; } = "EPSG:3857";
    public string MatrixSet { get; set; } = "";
    public string Format { get; set; } = "image/png";
    public string Authentication { get; set; } = "Anonymous";
    public string CredentialPrefix { get; set; } = "OGC_DEMO";
    public int TimeoutSeconds { get; set; } = 20;
    public int MaxFeatures { get; set; } = 1000;
    public int MaxResponseBytes { get; set; } = 8 * 1024 * 1024;

    public void Validate()
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new DemoException("Configure an absolute HTTPS service URL.");
        if (!string.IsNullOrEmpty(uri.UserInfo)) throw new DemoException("Credentials must not be embedded in URLs.");
        if (Kind is not ("WMS" or "WMTS" or "WFS")) throw new DemoException("Kind must be WMS, WMTS, or WFS.");
        if (Kind == "WMS" && Version is not ("1.1.1" or "1.3.0") ||
            Kind == "WMTS" && Version != "1.0.0" || Kind == "WFS" && Version is not ("1.1.0" or "2.0.0"))
            throw new DemoException("Unsupported protocol version. See the configuration reference.");
        if (Crs != "EPSG:3857") throw new DemoException("Service layers must offer EPSG:3857 in this demo.");
        if (string.IsNullOrWhiteSpace(Layer)) throw new DemoException("A layer or feature type name is required.");
        if (Kind == "WMTS" && string.IsNullOrWhiteSpace(MatrixSet)) throw new DemoException("Select an advertised WMTS matrix set.");
        if (TimeoutSeconds is < 1 or > 120 || MaxFeatures is < 1 or > 10000 || MaxResponseBytes is < 1024 or > 33554432)
            throw new DemoException("Request limits are outside the documented demo bounds.");
        if (Authentication is not ("Anonymous" or "Basic")) throw new DemoException("Authentication must be Anonymous or Basic.");
    }
}

// These messages are authored locally. Never expose an arbitrary server response or exception message in logs.
public sealed class DemoException(string message) : Exception(message);
