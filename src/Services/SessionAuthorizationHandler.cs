using Microsoft.AspNetCore.Authorization;

namespace VsngrpCoreBe.Services;

public sealed class ActiveSessionRequirement : IAuthorizationRequirement;

public sealed class SessionAuthorizationHandler(ISessionService sessionService)
    : AuthorizationHandler<ActiveSessionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ActiveSessionRequirement requirement)
    {
        var sessionId = context.User.FindFirst("sid")?.Value;
        if (string.IsNullOrEmpty(sessionId))
        {
            return;
        }

        var accountId = await sessionService.GetAccountIdAsync(sessionId);
        if (accountId is null)
        {
            return;
        }

        context.Succeed(requirement);
    }
}
