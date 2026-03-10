using Azure.Core;
using Azure.Identity;
using GitHub.Copilot.SDK;
using Microsoft.Extensions.Configuration;

namespace AgentWorkflowBuilder.Core.Engine;

/// <summary>
/// Builds a <see cref="ProviderConfig"/> from application configuration for BYOK provider setup.
/// When the Azure OpenAI resource has key auth disabled, falls back to Entra ID bearer token auth.
/// </summary>
public class CopilotProviderFactory
{
    private static readonly string[] AzureCognitiveServicesScope = ["https://cognitiveservices.azure.com/.default"];

    private readonly string _type;
    private readonly string _baseUrl;
    private readonly string? _apiKey;
    private readonly string _azureApiVersion;
    private readonly TokenCredential? _tokenCredential;

    public CopilotProviderFactory(IConfiguration configuration)
        : this(configuration, tokenCredential: null)
    {
    }

    internal CopilotProviderFactory(IConfiguration configuration, TokenCredential? tokenCredential)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        _type = configuration["CopilotSdk:Provider:Type"] ?? "azure";
        _baseUrl = configuration["CopilotSdk:Provider:BaseUrl"]
            ?? throw new InvalidOperationException(
                "CopilotSdk:Provider:BaseUrl is required. Set it in appsettings.json or via environment variable.");

        string? configApiKey = configuration["CopilotSdk:Provider:ApiKey"];
        _apiKey = !string.IsNullOrWhiteSpace(configApiKey)
            ? configApiKey
            : Environment.GetEnvironmentVariable("COPILOT_PROVIDER_API_KEY");

        bool isAzure = _type.Equals("azure", StringComparison.OrdinalIgnoreCase);

        if (string.IsNullOrWhiteSpace(_apiKey) && isAzure)
        {
            // No API key available — use Entra ID bearer token auth
            string? tenantId = configuration["CopilotSdk:Provider:TenantId"];
            _tokenCredential = tokenCredential
                ?? new DefaultAzureCredential(new DefaultAzureCredentialOptions
                {
                    TenantId = tenantId,
                    ExcludeVisualStudioCredential = true,
                    ExcludeVisualStudioCodeCredential = true
                });
        }
        else if (string.IsNullOrWhiteSpace(_apiKey))
        {
            throw new InvalidOperationException(
                "CopilotSdk:Provider:ApiKey is required for non-Azure providers. Set it in appsettings.json or via COPILOT_PROVIDER_API_KEY environment variable.");
        }

        _azureApiVersion = configuration["CopilotSdk:Provider:AzureApiVersion"] ?? "2024-10-21";
        DefaultModel = configuration["CopilotSdk:DefaultModel"] ?? "gpt-4.1-mini";
    }

    /// <summary>
    /// Returns the configured BYOK provider. When using Entra ID auth, acquires a fresh bearer token.
    /// </summary>
    public async Task<ProviderConfig> CreateProviderConfigAsync(CancellationToken ct = default)
    {
        ProviderConfig providerConfig = new()
        {
            Type = _type,
            BaseUrl = _baseUrl
        };

        if (_tokenCredential is not null)
        {
            AccessToken token = await _tokenCredential.GetTokenAsync(
                new TokenRequestContext(AzureCognitiveServicesScope), ct);
            providerConfig.BearerToken = token.Token;
        }
        else
        {
            providerConfig.ApiKey = _apiKey;
        }

        if (_type.Equals("azure", StringComparison.OrdinalIgnoreCase))
        {
            providerConfig.Azure = new() { ApiVersion = _azureApiVersion };
        }

        return providerConfig;
    }

    /// <summary>
    /// Returns the default model name from configuration.
    /// </summary>
    public string DefaultModel { get; }
}
