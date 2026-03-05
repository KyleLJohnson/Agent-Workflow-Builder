using GitHub.Copilot.SDK;
using Microsoft.Extensions.Configuration;

namespace AgentWorkflowBuilder.Core.Engine;

/// <summary>
/// Builds a <see cref="ProviderConfig"/> from application configuration for BYOK provider setup.
/// </summary>
public class CopilotProviderFactory
{
    private readonly ProviderConfig _providerConfig;

    public CopilotProviderFactory(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        string type = configuration["CopilotSdk:Provider:Type"] ?? "azure";
        string baseUrl = configuration["CopilotSdk:Provider:BaseUrl"]
            ?? throw new InvalidOperationException(
                "CopilotSdk:Provider:BaseUrl is required. Set it in appsettings.json or via environment variable.");
        string apiKey = configuration["CopilotSdk:Provider:ApiKey"]
            ?? Environment.GetEnvironmentVariable("COPILOT_PROVIDER_API_KEY")
            ?? throw new InvalidOperationException(
                "CopilotSdk:Provider:ApiKey is required. Set it in appsettings.json or via COPILOT_PROVIDER_API_KEY environment variable.");
        string azureApiVersion = configuration["CopilotSdk:Provider:AzureApiVersion"] ?? "2024-10-21";

        _providerConfig = new ProviderConfig
        {
            Type = type,
            BaseUrl = baseUrl,
            ApiKey = apiKey
        };

        // Set Azure-specific config when using Azure provider
        if (type.Equals("azure", StringComparison.OrdinalIgnoreCase))
        {
            _providerConfig.Azure = new() { ApiVersion = azureApiVersion };
        }
    }

    /// <summary>
    /// Returns the configured BYOK provider.
    /// </summary>
    public ProviderConfig CreateProviderConfig() => _providerConfig;

    /// <summary>
    /// Returns the default model name from configuration.
    /// </summary>
    public string DefaultModel { get; init; } = "gpt-4.1-mini";
}
