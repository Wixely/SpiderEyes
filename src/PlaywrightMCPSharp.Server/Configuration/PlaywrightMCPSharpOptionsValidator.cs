using Microsoft.Extensions.Options;

namespace PlaywrightMCPSharp.Server.Configuration;

public sealed class PlaywrightMCPSharpOptionsValidator : IValidateOptions<PlaywrightMCPSharpOptions>
{
    public ValidateOptionsResult Validate(string? name, PlaywrightMCPSharpOptions options)
    {
        if (options.Server.Transport == PlaywrightMCPSharpTransportMode.Http && !options.Server.Route.StartsWith('/'))
        {
            return ValidateOptionsResult.Fail("PlaywrightMCPSharp:Server:Route must start with '/'.");
        }

        if (options.Security.Mode == PlaywrightMCPSharpSecurityMode.RemoteNoAuth && !options.Security.DangerousAllowRemoteNoAuth)
        {
            return ValidateOptionsResult.Fail("RemoteNoAuth mode requires PlaywrightMCPSharp:Security:DangerousAllowRemoteNoAuth=true.");
        }

        if (options.Security.Mode == PlaywrightMCPSharpSecurityMode.RemoteBearer && string.IsNullOrWhiteSpace(options.Security.BearerToken))
        {
            return ValidateOptionsResult.Fail("RemoteBearer mode requires PlaywrightMCPSharp:Security:BearerToken.");
        }

        if (options.Session.MaxTabs < 1)
        {
            return ValidateOptionsResult.Fail("PlaywrightMCPSharp:Session:MaxTabs must be at least 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
