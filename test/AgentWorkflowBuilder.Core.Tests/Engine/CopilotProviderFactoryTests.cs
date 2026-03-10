using AgentWorkflowBuilder.Core.Engine;
using Azure.Core;
using Microsoft.Extensions.Configuration;

namespace AgentWorkflowBuilder.Core.Tests.Engine;

public class CopilotProviderFactoryTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    /// <summary>
    /// Fake token credential that returns a fixed token for testing.
    /// </summary>
    private sealed class FakeTokenCredential : TokenCredential
    {
        private readonly string _token;

        public FakeTokenCredential(string token = "fake-bearer-token")
        {
            _token = token;
        }

        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(_token, DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
            => new(new AccessToken(_token, DateTimeOffset.UtcNow.AddHours(1)));
    }

    [Fact]
    public async Task WhenValidConfigThenCreatesProviderConfig()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "azure",
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = await factory.CreateProviderConfigAsync();
        Assert.Equal("azure", result.Type);
        Assert.Equal("https://myopenai.azure.com", result.BaseUrl);
        Assert.Equal("test-api-key", result.ApiKey);
    }

    [Fact]
    public async Task WhenAzureProviderThenSetsAzureApiVersion()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "azure",
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key",
            ["CopilotSdk:Provider:AzureApiVersion"] = "2025-01-01"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = await factory.CreateProviderConfigAsync();
        Assert.NotNull(result.Azure);
        Assert.Equal("2025-01-01", result.Azure.ApiVersion);
    }

    [Fact]
    public async Task WhenNonAzureProviderThenAzureConfigIsNull()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "custom",
            ["CopilotSdk:Provider:BaseUrl"] = "https://custom-llm.example.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = await factory.CreateProviderConfigAsync();
        Assert.Null(result.Azure);
    }

    [Fact]
    public void WhenDefaultModelNotSetThenFallsBackToDefault()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        Assert.Equal("gpt-4.1-mini", factory.DefaultModel);
    }

    [Fact]
    public void WhenDefaultModelSetThenUsesConfiguredValue()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key",
            ["CopilotSdk:DefaultModel"] = "gpt-4o"
        });

        CopilotProviderFactory factory = new(config);

        Assert.Equal("gpt-4o", factory.DefaultModel);
    }

    [Fact]
    public async Task WhenProviderTypeNotSetThenDefaultsToAzure()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = await factory.CreateProviderConfigAsync();
        Assert.Equal("azure", result.Type);
        Assert.NotNull(result.Azure);
    }

    [Fact]
    public void WhenBaseUrlMissingThenThrowsInvalidOperation()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        Assert.Throws<InvalidOperationException>(() => new CopilotProviderFactory(config));
    }

    [Fact]
    public void WhenApiKeyMissingForNonAzureThenThrowsInvalidOperation()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "custom",
            ["CopilotSdk:Provider:BaseUrl"] = "https://custom-llm.example.com"
        });

        Assert.Throws<InvalidOperationException>(() => new CopilotProviderFactory(config));
    }

    [Fact]
    public void WhenNullConfigThenThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CopilotProviderFactory(null!));
    }

    [Fact]
    public async Task WhenAzureApiVersionNotSetThenDefaultsTo202410()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "azure",
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = await factory.CreateProviderConfigAsync();
        Assert.NotNull(result.Azure);
        Assert.Equal("2024-10-21", result.Azure.ApiVersion);
    }

    [Fact]
    public async Task WhenAzureWithNoApiKeyThenUsesBearerToken()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "azure",
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com"
        });

        CopilotProviderFactory factory = new(config, new FakeTokenCredential("test-bearer-token"));

        GitHub.Copilot.SDK.ProviderConfig result = await factory.CreateProviderConfigAsync();
        Assert.Null(result.ApiKey);
        Assert.Equal("test-bearer-token", result.BearerToken);
        Assert.Equal("azure", result.Type);
        Assert.NotNull(result.Azure);
    }

    [Fact]
    public async Task WhenAzureWithApiKeyThenUsesApiKeyNotBearer()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "azure",
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = await factory.CreateProviderConfigAsync();
        Assert.Equal("test-api-key", result.ApiKey);
        Assert.Null(result.BearerToken);
    }
}
