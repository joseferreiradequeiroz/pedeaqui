namespace PedeAqui.Settings;

public class ApiEdgeSettings
{
    public const string SECTION = "ApiEdge";

    public long MaxRequestBodyBytes { get; set; }
    public int RequestTimeoutSeconds { get; set; }
    public int ShutdownTimeoutSeconds { get; set; }
}
