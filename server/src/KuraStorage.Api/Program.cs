using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using KuraStorage.Api;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Identity;
using KuraStorage.Infrastructure;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddEnvironmentVariables("KURASTORAGE_");
var secretsDirectory = Environment.GetEnvironmentVariable("KURASTORAGE_SECRETS_DIR");
if (!string.IsNullOrWhiteSpace(secretsDirectory))
{
    builder.Configuration.AddKeyPerFile(secretsDirectory, optional: false);
}

builder.Services.AddProblemDetails();
builder.Services.AddKuraStorageInfrastructure(builder.Configuration);

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer();
builder.Services
    .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<AuthenticationOptions>>((options, configuredAuthentication) =>
    {
        var authentication = configuredAuthentication.Value;
        var validationKey = ECDsa.Create();
        validationKey.ImportFromPem(File.ReadAllText(authentication.JwtSigningKeyFile));
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new ECDsaSecurityKey(validationKey),
            ValidateIssuer = true,
            ValidIssuer = authentication.JwtIssuer,
            ValidateAudience = true,
            ValidAudience = authentication.JwtAudience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero,
            NameClaimType = JwtRegisteredClaimNames.Sub,
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = async context =>
            {
                var principal = context.Principal;
                if (!TryClaimGuid(principal, JwtRegisteredClaimNames.Sub, out var userId) ||
                    !TryClaimGuid(principal, "device_id", out var deviceId) ||
                    !TryClaimGuid(principal, "session_family_id", out var familyId) ||
                    !await context.HttpContext.RequestServices
                        .GetRequiredService<IdentityService>()
                        .ValidateSessionAsync(userId, deviceId, familyId, context.HttpContext.RequestAborted))
                {
                    context.Fail("The user, device, or session is inactive.");
                }
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(
                    new ErrorResponse(
                        "AUTHENTICATION_REQUIRED",
                        "The request could not be completed.",
                        context.HttpContext.TraceIdentifier,
                        new { }));
            },
            OnForbidden = async context =>
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    new ErrorResponse(
                        "DEVICE_REVOKED",
                        "The request could not be completed.",
                        context.HttpContext.TraceIdentifier,
                        new { }));
            },
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

var app = builder.Build();
app.UseExceptionHandler(exceptionHandlerApp =>
{
    exceptionHandlerApp.Run(async context =>
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(
            new ErrorResponse(
                "INTERNAL_ERROR",
                "The request could not be completed.",
                context.TraceIdentifier,
                new { }));
    });
});
app.UseMiddleware<RouteHeaderMiddleware>();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet(
        "/api/v1/system/health",
        async (IStorageGuard storageGuard, CancellationToken cancellationToken) =>
        {
            var storage = await storageGuard.InspectAsync(false, cancellationToken);
            return Results.Ok(new
            {
                api = "AVAILABLE",
                protocolVersion = 1,
                storage = storage == StorageStatus.Available ? "AVAILABLE" : "UNAVAILABLE",
            });
        })
    .AllowAnonymous();

app.MapPost(
        "/api/v1/auth/register-device",
        async (
            RegisterDeviceRequest request,
            HttpContext context,
            IdentityService identity,
            CancellationToken cancellationToken) =>
        {
            if (!string.Equals(
                    context.Items[RouteHeaderMiddleware.HeaderName] as string,
                    RouteHeaderMiddleware.LocalDirect,
                    StringComparison.Ordinal))
            {
                return Error(
                    StatusCodes.Status403Forbidden,
                    "DEVICE_REGISTRATION_REQUIRES_LOCAL_DIRECT",
                    context);
            }

            if (!ValidUsernamePassword(request.Username, request.Password) || !ValidText(request.DeviceName, 128))
            {
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", context);
            }

            var result = await identity.RegisterDeviceAsync(
                request.Username!,
                request.Password!,
                request.DeviceName!,
                context.Connection.RemoteIpAddress?.ToString(),
                context.TraceIdentifier,
                cancellationToken);
            return ToHttpResult(result, context);
        })
    .AllowAnonymous();

app.MapPost(
        "/api/v1/auth/login",
        async (
            LoginRequest request,
            HttpContext context,
            IdentityService identity,
            CancellationToken cancellationToken) =>
        {
            if (!ValidUsernamePassword(request.Username, request.Password) || request.DeviceId == Guid.Empty)
            {
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", context);
            }

            var result = await identity.LoginAsync(
                request.Username!,
                request.Password!,
                request.DeviceId,
                context.Connection.RemoteIpAddress?.ToString(),
                context.TraceIdentifier,
                cancellationToken);
            return ToHttpResult(result, context);
        })
    .AllowAnonymous();

app.MapPost(
        "/api/v1/auth/refresh",
        async (
            RefreshRequest request,
            HttpContext context,
            IdentityService identity,
            CancellationToken cancellationToken) =>
        {
            if (request.DeviceId == Guid.Empty || !ValidRefreshToken(request.RefreshToken))
            {
                return Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", context);
            }

            var result = await identity.RefreshAsync(
                request.DeviceId,
                request.RefreshToken!,
                context.TraceIdentifier,
                cancellationToken);
            return ToHttpResult(result, context);
        })
    .AllowAnonymous();

app.MapPost(
    "/api/v1/auth/logout",
    async (
        LogoutRequest request,
        HttpContext context,
        IdentityService identity,
        CancellationToken cancellationToken) =>
    {
        if (!TryClaimGuid(context.User, "device_id", out var authenticatedDeviceId) ||
            authenticatedDeviceId != request.DeviceId ||
            !ValidRefreshToken(request.RefreshToken))
        {
            return Error(StatusCodes.Status400BadRequest, "VALIDATION_FAILED", context);
        }

        await identity.LogoutAsync(
            request.DeviceId,
            request.RefreshToken!,
            context.TraceIdentifier,
            cancellationToken);
        return Results.NoContent();
    });

app.Run();

static bool TryClaimGuid(System.Security.Claims.ClaimsPrincipal? principal, string claimType, out Guid value) =>
    Guid.TryParse(principal?.FindFirst(claimType)?.Value, out value);

static bool ValidUsernamePassword(string? username, string? password) =>
    ValidText(username, 128) && password is not null && password.Length is >= 1 and <= 1024;

static bool ValidText(string? value, int maximumLength) =>
    !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

static bool ValidRefreshToken(string? value) => value?.Length is >= 32 and <= 2048;

static IResult ToHttpResult(IdentityResult<TokenPair> result, HttpContext context)
{
    if (result.IsSuccess)
    {
        return Results.Ok(result.Value);
    }

    var status = result.Failure!.Kind switch
    {
        IdentityFailureKind.BadRequest => StatusCodes.Status400BadRequest,
        IdentityFailureKind.Unauthorized => StatusCodes.Status401Unauthorized,
        IdentityFailureKind.Forbidden => StatusCodes.Status403Forbidden,
        IdentityFailureKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
    var publicCode = result.Failure.Code switch
    {
        IdentityErrorCodes.InvalidCredentials or
        IdentityErrorCodes.AccountLocked or
        IdentityErrorCodes.RefreshTokenInvalid => "AUTHENTICATION_REQUIRED",
        IdentityErrorCodes.DeviceLimitReached => "VALIDATION_FAILED",
        _ => result.Failure.Code,
    };
    return Error(status, publicCode, context);
}

static IResult Error(int status, string code, HttpContext context) =>
    Results.Json(
        new ErrorResponse(code, "The request could not be completed.", context.TraceIdentifier, new { }),
        statusCode: status);

public sealed record RegisterDeviceRequest(string? Username, string? Password, string? DeviceName);

public sealed record LoginRequest(string? Username, string? Password, Guid DeviceId);

public sealed record RefreshRequest(Guid DeviceId, string? RefreshToken);

public sealed record LogoutRequest(Guid DeviceId, string? RefreshToken);

public sealed record ErrorResponse(string Code, string Message, string RequestId, object Details);

public partial class Program;
