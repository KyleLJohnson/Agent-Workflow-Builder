using AgentWorkflowBuilder.Core.Engine;
using Microsoft.Extensions.Configuration;

namespace AgentWorkflowBuilder.Core.Tests.Engine;

public class CopilotProviderFactoryTests
{
    private static IConfiguration BuildConfig(Dictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();

    [Fact]
    public void WhenValidConfigThenCreatesProviderConfig()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "azure",
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = factory.CreateProviderConfig();
        Assert.Equal("azure", result.Type);
        Assert.Equal("https://myopenai.azure.com", result.BaseUrl);
        Assert.Equal("test-api-key", result.ApiKey);
    }

    [Fact]
    public void WhenAzureProviderThenSetsAzureApiVersion()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "azure",
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key",
            ["CopilotSdk:Provider:AzureApiVersion"] = "2025-01-01"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = factory.CreateProviderConfig();
        Assert.NotNull(result.Azure);
        Assert.Equal("2025-01-01", result.Azure.ApiVersion);
    }

    [Fact]
    public void WhenNonAzureProviderThenAzureConfigIsNull()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "custom",
            ["CopilotSdk:Provider:BaseUrl"] = "https://custom-llm.example.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = factory.CreateProviderConfig();
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
    public void WhenProviderTypeNotSetThenDefaultsToAzure()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = factory.CreateProviderConfig();
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
    public void WhenApiKeyMissingThenThrowsInvalidOperation()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com"
        });

        Assert.Throws<InvalidOperationException>(() => new CopilotProviderFactory(config));
    }

    [Fact]
    public void WhenNullConfigThenThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => new CopilotProviderFactory(null!));
    }

    [Fact]
    public void WhenAzureApiVersionNotSetThenDefaultsTo202410()
    {
        IConfiguration config = BuildConfig(new Dictionary<string, string?>
        {
            ["CopilotSdk:Provider:Type"] = "azure",
            ["CopilotSdk:Provider:BaseUrl"] = "https://myopenai.azure.com",
            ["CopilotSdk:Provider:ApiKey"] = "test-api-key"
        });

        CopilotProviderFactory factory = new(config);

        GitHub.Copilot.SDK.ProviderConfig result = factory.CreateProviderConfig();
        Assert.NotNull(result.Azure);
        Assert.Equal("2024-10-21", result.Azure.ApiVersion);
    }
}
