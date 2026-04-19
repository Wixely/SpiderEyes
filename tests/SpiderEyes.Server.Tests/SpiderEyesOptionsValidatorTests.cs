using Microsoft.Extensions.Options;
using SpiderEyes.Server.Configuration;

namespace SpiderEyes.Server.Tests;

public sealed class SpiderEyesOptionsValidatorTests
{
    private readonly SpiderEyesOptionsValidator _validator = new();

    [Fact]
    public void Validate_Fails_ForRemoteNoAuthWithoutDangerousFlag()
    {
        var options = new SpiderEyesOptions
        {
            Security = new SecurityOptions
            {
                Mode = SpiderEyesSecurityMode.RemoteNoAuth,
                DangerousAllowRemoteNoAuth = false,
            },
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
        Assert.Contains("DangerousAllowRemoteNoAuth", result.FailureMessage);
    }

    [Fact]
    public void Validate_Fails_ForRemoteBearerWithoutToken()
    {
        var options = new SpiderEyesOptions
        {
            Security = new SecurityOptions
            {
                Mode = SpiderEyesSecurityMode.RemoteBearer,
            },
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
        Assert.Contains("BearerToken", result.FailureMessage);
    }

    [Fact]
    public void Validate_Succeeds_ForLocalDefaults()
    {
        var result = _validator.Validate(Options.DefaultName, new SpiderEyesOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_Ignores_HttpRouteShape_ForStdioTransport()
    {
        var options = new SpiderEyesOptions
        {
            Server = new ServerOptions
            {
                Transport = SpiderEyesTransportMode.Stdio,
                Route = "mcp",
            },
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }
}
