using System.Threading.RateLimiting;
using Erp.Api;
using Erp.Api.Endpoints;
using Erp.Infrastructure;
using Erp.Infrastructure.Seed;
using Erp.Application.Identity;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = builder.Environment.IsDevelopment() ? "Erp.Antiforgery.Dev" : "__Host-Erp.Antiforgery";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
    options.AddPolicy("customer-search", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true,
        }));
});
builder.Services.AddAuthorization();
builder.Services.AddErpInfrastructure(builder.Configuration, builder.Environment);

var app = builder.Build();

app.UseExceptionHandler(exceptionHandler => exceptionHandler.Run(async context =>
{
    var exception = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    ApiLog.UnhandledRequest(app.Logger, context.TraceIdentifier, exception);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new ProblemDetails
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "系统暂时无法完成请求",
        Detail = "请稍后重试；如问题持续，请提供请求追踪号。",
        Extensions = { ["traceId"] = context.TraceIdentifier },
    });
}));

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.XFrameOptions = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    await next();
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    var isApiRequest = context.Request.Path.StartsWithSegments("/api/v1");
    var isPasswordBootstrapEndpoint = context.Request.Path.StartsWithSegments("/api/v1/auth/login")
        || context.Request.Path.StartsWithSegments("/api/v1/auth/logout")
        || context.Request.Path.StartsWithSegments("/api/v1/auth/me")
        || context.Request.Path.StartsWithSegments("/api/v1/auth/change-password")
        || context.Request.Path.StartsWithSegments("/api/v1/security/csrf");
    if (isApiRequest && !isPasswordBootstrapEndpoint && context.User.Identity?.IsAuthenticated == true)
    {
        var current = await context.RequestServices.GetRequiredService<IIdentityService>()
            .GetCurrentAsync(context.RequestAborted);
        if (current?.MustChangePassword == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "PASSWORD_CHANGE_REQUIRED", message = "首次登录必须先修改初始密码" },
                traceId = context.TraceIdentifier,
            });
            return;
        }
    }

    await next();
});

app.Use(async (context, next) =>
{
    var unsafeMethod = HttpMethods.IsPost(context.Request.Method)
        || HttpMethods.IsPut(context.Request.Method)
        || HttpMethods.IsPatch(context.Request.Method)
        || HttpMethods.IsDelete(context.Request.Method);
    var shouldValidate = context.Request.Path.StartsWithSegments("/api/v1")
        && unsafeMethod
        && !context.Request.Path.StartsWithSegments("/api/v1/security/csrf");
    if (shouldValidate)
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
        }
        catch (AntiforgeryValidationException)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new
            {
                error = new { code = "INVALID_ANTIFORGERY_TOKEN", message = "页面安全令牌已失效，请刷新后重试" },
                traceId = context.TraceIdentifier,
            });
            return;
        }
    }

    await next();
});

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    using var scope = app.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<DevelopmentSeeder>().SeedAsync(CancellationToken.None);
}

app.MapGet("/health/live", () => Results.Ok(new { status = "ok", version = typeof(Program).Assembly.GetName().Version?.ToString() }))
    .AllowAnonymous();
app.MapSecurityEndpoints();
app.MapIdentityEndpoints();
app.MapEmployeeEndpoints();
app.MapCatalogEndpoints();
app.MapFacilityEndpoints();
app.MapCustomerEndpoints();
app.MapMemberTopupEndpoints();
app.MapCashierEndpoints();
app.MapPaymentEndpoints();
app.MapAuditEndpoints();
app.MapReportEndpoints();

app.Run();

public partial class Program;
