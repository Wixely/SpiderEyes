using Microsoft.Extensions.Options;
using PlaywrightMCPSharp.Server.Configuration;

namespace PlaywrightMCPSharp.Server.Tests;

public sealed class PlaywrightMCPSharpOptionsValidatorTests
{
    private readonly PlaywrightMCPSharpOptionsValidator _validator = new();

    [Fact]
    public void Validate_Fails_ForRemoteNoAuthWithoutDangerousFlag()
    {
        var options = new PlaywrightMCPSharpOptions
        {
            Security = new SecurityOptions
            {
                Mode = PlaywrightMCPSharpSecurityMode.RemoteNoAuth,
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
        var options = new PlaywrightMCPSharpOptions
        {
            Security = new SecurityOptions
            {
                Mode = PlaywrightMCPSharpSecurityMode.RemoteBearer,
            },
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.False(result.Succeeded);
        Assert.Contains("BearerToken", result.FailureMessage);
    }

    [Fact]
    public void Validate_Succeeds_ForLocalDefaults()
    {
        var result = _validator.Validate(Options.DefaultName, new PlaywrightMCPSharpOptions());

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_Ignores_HttpRouteShape_ForStdioTransport()
    {
        var options = new PlaywrightMCPSharpOptions
        {
            Server = new ServerOptions
            {
                Transport = PlaywrightMCPSharpTransportMode.Stdio,
                Route = "mcp",
            },
        };

        var result = _validator.Validate(Options.DefaultName, options);

        Assert.True(result.Succeeded);
    }
}
