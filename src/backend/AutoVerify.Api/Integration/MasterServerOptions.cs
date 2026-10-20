namespace AutoVerify.Api.Integration;

public sealed class MasterServerOptions
{
    public const string SectionName = "MasterServer";

    /// <summary>Master Server root, e.g. http://master:336. Empty disables the integration (AI verdict stays Unavailable).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>The access_clients.ClientId registered on the Master for this AutoVerify host (MachineType REQUEST).</summary>
    public string ClientId { get; set; } = "autoverify";

    /// <summary>Per-client API key issued by the Master. Keep it in appsettings.Local.json or an environment variable, never in git.</summary>
    public string ApiKey { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 60;
    public int MaxItemsPerJob { get; set; } = 50;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(BaseUrl) && !string.IsNullOrWhiteSpace(ClientId);
}
