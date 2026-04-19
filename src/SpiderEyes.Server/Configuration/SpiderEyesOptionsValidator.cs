using Microsoft.Extensions.Options;

namespace SpiderEyes.Server.Configuration;

public sealed class SpiderEyesOptionsValidator : IValidateOptions<SpiderEyesOptions>
{
    public ValidateOptionsResult Validate(string? name, SpiderEyesOptions options)
    {
        if (!options.Server.Route.StartsWith('/'))
        {
            return ValidateOptionsResult.Fail("SpiderEyes:Server:Route must start with '/'.");
        }

        if (options.Security.Mode == SpiderEyesSecurityMode.RemoteNoAuth && !options.Security.DangerousAllowRemoteNoAuth)
        {
            return ValidateOptionsResult.Fail("RemoteNoAuth mode requires SpiderEyes:Security:DangerousAllowRemoteNoAuth=true.");
        }

        if (options.Security.Mode == SpiderEyesSecurityMode.RemoteBearer && string.IsNullOrWhiteSpace(options.Security.BearerToken))
        {
            return ValidateOptionsResult.Fail("RemoteBearer mode requires SpiderEyes:Security:BearerToken.");
        }

        if (options.Session.MaxTabs < 1)
        {
            return ValidateOptionsResult.Fail("SpiderEyes:Session:MaxTabs must be at least 1.");
        }

        return ValidateOptionsResult.Success;
    }
}
