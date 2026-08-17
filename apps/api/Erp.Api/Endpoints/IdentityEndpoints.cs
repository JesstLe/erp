using Erp.Application.Identity;

namespace Erp.Api.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/auth").WithTags("Identity");

        group.MapPost("/login", async (LoginRequest request, IIdentityService identity, CancellationToken cancellationToken) =>
            EndpointResults.From(await identity.LoginAsync(
                new LoginCommand(request.Account ?? string.Empty, request.Password ?? string.Empty, request.RememberMe),
                cancellationToken)))
            .AllowAnonymous()
            .RequireRateLimiting("login");

        group.MapPost("/logout", async (IIdentityService identity, CancellationToken cancellationToken) =>
        {
            await identity.LogoutAsync(cancellationToken);
            return Results.NoContent();
        }).RequireAuthorization();

        group.MapGet("/me", async (IIdentityService identity, CancellationToken cancellationToken) =>
        {
            var current = await identity.GetCurrentAsync(cancellationToken);
            return current is null ? Results.Unauthorized() : Results.Ok(current);
        }).RequireAuthorization();

        return endpoints;
    }

    private sealed record LoginRequest(string? Account, string? Password, bool RememberMe);
}

