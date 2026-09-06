using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using KuraStorage.Api;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;
using KuraStorage.Application.Media;
using KuraStorage.Application.Sharing;
using KuraStorage.Application.Search;
using KuraStorage.Application.Recent;
using KuraStorage.Application.Organization;
using KuraStorage.Application.Transfers;
using KuraStorage.Application.Identity;
using KuraStorage.Application.Activity;
using KuraStorage.Application.Backup;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Backup;
using KuraStorage.Domain.Sharing;
using KuraStorage.Infrastructure;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;

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
            RoleClaimType = "role",
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
    options.AddPolicy("AdminOnly", policy => policy.RequireRole("ADMIN"));
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ErrorResponse(
                "RATE_LIMIT_EXCEEDED",
                "The request could not be completed.",
                context.HttpContext.TraceIdentifier,
                new { }),
            cancellationToken);
    };
    options.AddPolicy(
        "TextVersions",
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
            }));
    options.AddPolicy(
        "Activities",
        context => RateLimitPartition.GetFixedWindowLimiter(
            context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value ??
            context.Connection.RemoteIpAddress?.ToString() ??
            "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                AutoReplenishment = true,
                PermitLimit = 120,
                QueueLimit = 0,
                Window = TimeSpan.FromMinutes(1),
            }));
});

var app = builder.Build();
const long maximumTextJsonBodyBytes = (FileVersionRecord.MaximumContentBytes * 6) + (64 * 1024);
app.Use(async (context, next) =>
{
    var isTextMutation =
        (context.Request.Method == HttpMethods.Put &&
         context.Request.Path.Value?.EndsWith("/text", StringComparison.Ordinal) == true) ||
        (context.Request.Method == HttpMethods.Post &&
         context.Request.Path.Value?.EndsWith("/restore", StringComparison.Ordinal) == true);
    if (isTextMutation)
    {
        var bodySize = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySize is { IsReadOnly: false })
        {
            bodySize.MaxRequestBodySize = maximumTextJsonBodyBytes;
        }

        if (context.Request.ContentLength > maximumTextJsonBodyBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            await context.Response.WriteAsJsonAsync(
                new ErrorResponse(
                    TextFileErrorCodes.TextSizeLimitExceeded,
                    "The request could not be completed.",
                    context.TraceIdentifier,
                    new { }));
            return;
        }
    }

    await next(context);
});
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
app.UseRateLimiter();

app.MapGet(
        "/api/v1/system/health",
        async (IStorageGuard storageGuard, CancellationToken cancellationToken) =>
        {
            var storage = await storageGuard.InspectAsync(StorageIntent.Read, cancellationToken);
            return Results.Ok(new
            {
                api = "AVAILABLE",
                protocolVersion = 2,
                storage = storage == StorageStatus.Available ? "AVAILABLE" : "UNAVAILABLE",
            });
        })
    .AllowAnonymous();

app.MapGet(
        "/api/v1/admin/storage",
        async (AdminStorageService storageService, CancellationToken cancellationToken) =>
            Results.Ok(await storageService.GetAsync(cancellationToken)))
    .RequireAuthorization("AdminOnly");

app.MapGet(
        "/api/v1/admin/media-cache",
        async (AdminMediaCacheService mediaCache, CancellationToken cancellationToken) =>
            Results.Ok(await mediaCache.GetAsync(cancellationToken)))
    .RequireAuthorization("AdminOnly");

app.MapPost(
        "/api/v1/admin/media-cache/cleanup-requests",
        async (
            HttpContext context,
            AdminMediaCacheService mediaCache,
            CancellationToken cancellationToken) =>
        {
            if (!TryAuthenticatedUserId(context, out var userId))
            {
                return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
            }

            var result = await mediaCache.RequestManualAsync(
                userId,
                context.Request.Headers["Idempotency-Key"].ToString(),
                cancellationToken);
            return result.Failure switch
            {
                MediaCleanupRequestFailure.Validation =>
                    Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context),
                MediaCleanupRequestFailure.IdempotencyConflict =>
                    Error(StatusCodes.Status409Conflict, FileErrorCodes.IdempotencyConflict, context),
                null => Results.Json(result.Run, statusCode: StatusCodes.Status202Accepted),
                _ => throw new InvalidOperationException("Unknown media cleanup request result."),
            };
        })
    .RequireAuthorization("AdminOnly");

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

app.MapGet(
    "/api/v1/shares/candidates",
    async (
        HttpContext context,
        SharingService sharing,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToSharingHttpResult(
            await sharing.ListCandidatesAsync(userId, cancellationToken),
            context);
    });

app.MapPost(
    "/api/v1/shares",
    async (
        CreateShareRequest request,
        HttpContext context,
        SharingService sharing,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (request.Members is null ||
            request.Members.Any(member => !TrySharePermission(member.Permission, out _)))
        {
            return Error(StatusCodes.Status400BadRequest, SharingErrorCodes.ValidationFailed, context);
        }

        var result = await sharing.CreateAsync(
            new CreateShareCommand(
                userId,
                deviceId,
                request.TargetEntryId,
                request.Members.Select(member =>
                    new ShareMemberInput(
                        member.UserId,
                        Enum.Parse<SharePermission>(member.Permission!, true))).ToArray(),
                context.TraceIdentifier),
            cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/shares/{result.Value!.Id}", result.Value)
            : ToSharingHttpResult(result, context);
    });

app.MapGet(
    "/api/v1/shares",
    async (
        string? scope,
        string? targetType,
        int? page,
        int? pageSize,
        HttpContext context,
        SharingService sharing,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!Enum.TryParse<ShareScope>(scope, true, out var parsedScope) ||
            !TryTargetType(targetType, out var parsedTargetType))
        {
            return Error(StatusCodes.Status400BadRequest, SharingErrorCodes.ValidationFailed, context);
        }

        return ToSharingHttpResult(
            await sharing.ListAsync(
                userId,
                parsedScope,
                parsedTargetType,
                page ?? 1,
                pageSize ?? 100,
                cancellationToken),
            context);
    });

app.MapGet(
    "/api/v1/shares/{shareId:guid}",
    async (
        Guid shareId,
        HttpContext context,
        SharingService sharing,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToSharingHttpResult(await sharing.GetAsync(userId, shareId, cancellationToken), context);
    });

app.MapPut(
    "/api/v1/shares/{shareId:guid}/members/{memberUserId:guid}",
    async (
        Guid shareId,
        Guid memberUserId,
        SetShareMemberRequest request,
        HttpContext context,
        SharingService sharing,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!TrySharePermission(request.Permission, out var permission))
        {
            return Error(StatusCodes.Status400BadRequest, SharingErrorCodes.ValidationFailed, context);
        }

        return ToSharingHttpResult(
            await sharing.SetMemberAsync(
                new SetShareMemberCommand(
                    userId,
                    deviceId,
                    shareId,
                    memberUserId,
                    permission,
                    context.TraceIdentifier),
                cancellationToken),
            context);
    });

app.MapDelete(
    "/api/v1/shares/{shareId:guid}/members/{memberUserId:guid}",
    async (
        Guid shareId,
        Guid memberUserId,
        HttpContext context,
        SharingService sharing,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await sharing.RemoveMemberAsync(
            new RemoveShareMemberCommand(
                userId, deviceId, shareId, memberUserId, context.TraceIdentifier),
            cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToSharingHttpResult(result, context);
    });

app.MapDelete(
    "/api/v1/shares/{shareId:guid}",
    async (
        Guid shareId,
        HttpContext context,
        SharingService sharing,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await sharing.DeleteAsync(
            new DeleteShareCommand(userId, deviceId, shareId, context.TraceIdentifier),
            cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToSharingHttpResult(result, context);
    });

app.MapGet(
    "/api/v1/search",
    async (
        [AsParameters] SearchHttpQuery query,
        HttpContext context,
        SearchService search,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!TryCreateSearchQuery(query, out var applicationQuery))
        {
            return Error(StatusCodes.Status400BadRequest, SearchErrorCodes.InvalidFilter, context);
        }

        return ToSearchHttpResult(
            await search.SearchAsync(userId, applicationQuery!, cancellationToken),
            context);
    });

app.MapGet(
    "/api/v1/recent-files",
    async (
        [AsParameters] RecentFilesHttpQuery query,
        HttpContext context,
        RecentFileService recentFiles,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!TryOptionalInt(query.Page, 1, out var page) ||
            !TryOptionalInt(query.PageSize, 50, out var pageSize))
        {
            return Error(StatusCodes.Status400BadRequest, RecentFileErrorCodes.InvalidRequest, context);
        }

        return ToRecentFileHttpResult(
            await recentFiles.ListAsync(userId, page, pageSize, cancellationToken),
            context);
    });

app.MapGet(
    "/api/v1/activities",
    async (
        [AsParameters] ActivitiesHttpQuery query,
        HttpContext context,
        ActivityQueryService activities,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!TryOptionalInt(query.PageSize, 50, out var pageSize))
        {
            return Error(StatusCodes.Status400BadRequest, ActivityQueryErrorCodes.InvalidRequest, context);
        }

        var result = await activities.ListAsync(
            userId,
            new ActivityListRequest(query.Type, query.Cursor, pageSize),
            cancellationToken);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Error(StatusCodes.Status400BadRequest, result.Failure!.Code, context);
    })
    .RequireRateLimiting("Activities");

app.MapPut(
    "/api/v1/recent-files/{fileId:guid}",
    async (
        Guid fileId,
        HttpContext context,
        RecentFileService recentFiles,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return Error(StatusCodes.Status400BadRequest, RecentFileErrorCodes.InvalidRequest, context);
        }

        var result = await recentFiles.RecordAsync(userId, fileId, cancellationToken);
        return result.IsSuccess
            ? Results.NoContent()
            : Error(StatusCodes.Status404NotFound, result.Failure!.Code, context);
    });

app.MapGet(
    "/api/v1/favorites",
    async (
        [AsParameters] RecentFilesHttpQuery query,
        HttpContext context,
        OrganizationService organization,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!TryOptionalInt(query.Page, 1, out var page) ||
            !TryOptionalInt(query.PageSize, 50, out var pageSize))
        {
            return Error(StatusCodes.Status400BadRequest, OrganizationErrorCodes.InvalidFavoritesRequest, context);
        }

        return ToOrganizationHttpResult(
            await organization.ListFavoritesAsync(userId, page, pageSize, cancellationToken),
            context);
    });

app.MapPut(
    "/api/v1/favorites/{entryId:guid}",
    async (Guid entryId, HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return Error(StatusCodes.Status400BadRequest, OrganizationErrorCodes.InvalidOrganizationRequest, context);
        }

        var result = await organization.AddFavoriteAsync(userId, entryId, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToOrganizationHttpResult(result, context);
    });

app.MapDelete(
    "/api/v1/favorites/{entryId:guid}",
    async (Guid entryId, HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return Error(StatusCodes.Status400BadRequest, OrganizationErrorCodes.InvalidOrganizationRequest, context);
        }

        var result = await organization.RemoveFavoriteAsync(userId, entryId, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToOrganizationHttpResult(result, context);
    });

app.MapGet(
    "/api/v1/tags",
    async (HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToOrganizationHttpResult(await organization.ListTagsAsync(userId, cancellationToken), context);
    });

app.MapPost(
    "/api/v1/tags",
    async (CreateTagRequest request, HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await organization.CreateTagAsync(userId, new CreateTagCommand(request.Name ?? string.Empty), cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/tags/{result.Value!.Id}", result.Value)
            : ToOrganizationHttpResult(result, context);
    });

app.MapPatch(
    "/api/v1/tags/{tagId:guid}",
    async (Guid tagId, RenameTagRequest request, HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToOrganizationHttpResult(
            await organization.RenameTagAsync(
                userId,
                new RenameTagCommand(tagId, request.Name ?? string.Empty),
                cancellationToken),
            context);
    });

app.MapDelete(
    "/api/v1/tags/{tagId:guid}",
    async (Guid tagId, HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return Error(StatusCodes.Status400BadRequest, OrganizationErrorCodes.InvalidOrganizationRequest, context);
        }

        var result = await organization.DeleteTagAsync(userId, tagId, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToOrganizationHttpResult(result, context);
    });

app.MapGet(
    "/api/v1/files/{entryId:guid}/organization",
    async (Guid entryId, HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToOrganizationHttpResult(
            await organization.GetEntryOrganizationAsync(userId, entryId, cancellationToken),
            context);
    });

app.MapPut(
    "/api/v1/files/{entryId:guid}/tags/{tagId:guid}",
    async (Guid entryId, Guid tagId, HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return Error(StatusCodes.Status400BadRequest, OrganizationErrorCodes.InvalidOrganizationRequest, context);
        }

        var result = await organization.AttachTagAsync(userId, entryId, tagId, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToOrganizationHttpResult(result, context);
    });

app.MapDelete(
    "/api/v1/files/{entryId:guid}/tags/{tagId:guid}",
    async (Guid entryId, Guid tagId, HttpContext context, OrganizationService organization, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (context.Features.Get<IHttpRequestBodyDetectionFeature>()?.CanHaveBody == true)
        {
            return Error(StatusCodes.Status400BadRequest, OrganizationErrorCodes.InvalidOrganizationRequest, context);
        }

        var result = await organization.DetachTagAsync(userId, entryId, tagId, cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToOrganizationHttpResult(result, context);
    });

app.MapGet(
    "/api/v1/files",
    async (
        Guid? parentId,
        int? page,
        int? pageSize,
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await files.ListAsync(userId, parentId, page ?? 1, pageSize ?? 100, cancellationToken);
        return ToFileHttpResult(result, context);
    });

app.MapGet(
        "/api/v1/files/{fileId:guid}",
    async (
        Guid fileId,
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToFileHttpResult(await files.GetAsync(userId, fileId, cancellationToken), context);
    });

app.MapGet(
    "/api/v1/files/{fileId:guid}/text",
    async (
        Guid fileId,
        HttpContext context,
        TextFileService textFiles,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToTextFileHttpResult(
            await textFiles.GetAsync(userId, fileId, cancellationToken),
            context);
    })
    .RequireRateLimiting("TextVersions");

app.MapPut(
    "/api/v1/files/{fileId:guid}/text",
    async (
        Guid fileId,
        HttpContext context,
        TextFileService textFiles,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!context.Request.HasJsonContentType())
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                TextFileErrorCodes.UnsupportedMediaType,
                context);
        }

        var request = await ReadJsonAsync<SaveTextRequest>(context.Request, cancellationToken);
        if (request is null || request.AcknowledgeLossySource is null || request.AdditionalProperties is { Count: > 0 })
        {
            return Error(StatusCodes.Status400BadRequest, TextFileErrorCodes.ValidationFailed, context);
        }

        return ToTextFileHttpResult(
            await textFiles.SaveAsync(
                new SaveTextFileCommand(
                    userId,
                    deviceId,
                    fileId,
                    request.Content,
                    request.ExpectedVersion,
                    request.OperationId,
                    context.TraceIdentifier,
                    request.AcknowledgeLossySource.Value),
                cancellationToken),
            context);
    })
    .RequireRateLimiting("TextVersions");

app.MapGet(
    "/api/v1/files/{fileId:guid}/versions",
    async (
        Guid fileId,
        [AsParameters] FileVersionsHttpQuery query,
        HttpContext context,
        TextFileService textFiles,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!TryOptionalInt(query.Page, 1, out var page) ||
            !TryOptionalInt(query.PageSize, 50, out var pageSize))
        {
            return Error(StatusCodes.Status400BadRequest, TextFileErrorCodes.ValidationFailed, context);
        }

        return ToTextFileHttpResult(
            await textFiles.ListVersionsAsync(userId, fileId, page, pageSize, cancellationToken),
            context);
    })
    .RequireRateLimiting("TextVersions");

app.MapGet(
    "/api/v1/files/{fileId:guid}/versions/{version:long}/text",
    async (
        Guid fileId,
        long version,
        HttpContext context,
        TextFileService textFiles,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToTextFileHttpResult(
            await textFiles.GetVersionTextAsync(userId, fileId, version, cancellationToken),
            context);
    })
    .RequireRateLimiting("TextVersions");

app.MapPost(
    "/api/v1/files/{fileId:guid}/versions/{version:long}/restore",
    async (
        Guid fileId,
        long version,
        HttpContext context,
        TextFileService textFiles,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!context.Request.HasJsonContentType())
        {
            return Error(
                StatusCodes.Status415UnsupportedMediaType,
                TextFileErrorCodes.UnsupportedMediaType,
                context);
        }

        var request = await ReadJsonAsync<RestoreTextVersionRequest>(context.Request, cancellationToken);
        if (request is null || request.AdditionalProperties is { Count: > 0 })
        {
            return Error(StatusCodes.Status400BadRequest, TextFileErrorCodes.ValidationFailed, context);
        }

        return ToTextFileHttpResult(
            await textFiles.RestoreAsync(
                new RestoreTextVersionCommand(
                    userId,
                    deviceId,
                    fileId,
                    version,
                    request.ExpectedVersion,
                    request.OperationId,
                    context.TraceIdentifier),
                cancellationToken),
            context);
    })
    .RequireRateLimiting("TextVersions");

app.MapPost(
    "/api/v1/files/{fileId:guid}/missing/recheck",
    async (
        Guid fileId,
        HttpContext context,
        MissingEntryService missingEntries,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToFileHttpResult(
            await missingEntries.RecheckAsync(
                new MissingFileCommand(userId, deviceId, fileId, context.TraceIdentifier),
                cancellationToken),
            context);
    });

app.MapDelete(
    "/api/v1/files/{fileId:guid}/missing-index-entry",
    async (
        Guid fileId,
        HttpContext context,
        MissingEntryService missingEntries,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await missingEntries.DeleteIndexEntryAsync(
            new MissingFileCommand(userId, deviceId, fileId, context.TraceIdentifier),
            cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToFileHttpResult(result, context);
    });

app.MapPatch(
    "/api/v1/files/{fileId:guid}",
    async (
        Guid fileId,
        UpdateFileRequest request,
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var hasName = request.Name is not null;
        var hasParent = request.ParentId is not null;
        if (hasName == hasParent ||
            request.AdditionalProperties is { Count: > 0 } ||
            (hasParent && request.ParentId == Guid.Empty))
        {
            return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
        }

        return hasName
            ? ToFileHttpResult(
                await files.RenameAsync(
                    new RenameFileCommand(
                        userId,
                        deviceId,
                        fileId,
                        request.Name!,
                        context.TraceIdentifier),
                    cancellationToken),
                context)
            : ToFileHttpResult(
                await files.MoveAsync(
                    new MoveFileCommand(
                        userId,
                        deviceId,
                        fileId,
                        request.ParentId!.Value,
                        context.TraceIdentifier),
                    cancellationToken),
                context);
    });

app.MapPost(
    "/api/v1/folders",
    async (
        CreateFolderRequest request,
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToFileHttpResult(
            await files.CreateFolderAsync(
                new CreateFolderCommand(
                    userId,
                    deviceId,
                    request.ParentId,
                    request.Name ?? string.Empty,
                    context.TraceIdentifier),
                cancellationToken),
            context);
    });

app.MapPost(
    "/api/v1/files/upload",
    async (
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return await HandleUploadAsync(userId, deviceId, context, files, cancellationToken);
    });

app.MapPost(
    "/api/v1/backup/compare",
    async (
        BackupCompareRequest request,
        HttpContext context,
        BackupCompareService backup,
        UploadSessionOptions uploadOptions,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var candidates = request.Items?.Select(item => new BackupCompareCandidate(
            item.LocalDocumentKey ?? string.Empty,
            item.RelativePath ?? string.Empty,
            item.Size,
            item.ModifiedAt,
            item.Checksum)).ToArray() ?? [];
        var result = await backup.CompareAsync(
            new BackupCompareCommand(userId, deviceId, request.DestinationFolderId, candidates),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return TransferError(result.Failure!, context, uploadOptions);
        }

        return Results.Ok(new BackupCompareResponse(result.Value!.Items.Select(item => new BackupCompareResponseItem(
            item.LocalDocumentKey,
            item.Decision switch
            {
                BackupCompareDecision.New => "NEW",
                BackupCompareDecision.Changed => "CHANGED",
                BackupCompareDecision.AlreadyUploaded => "ALREADY_UPLOADED",
                _ => "BLOCKED_CURRENT_STATE",
            },
            item.RemoteFileId,
            item.ExpectedRemoteFileVersion,
            item.ErrorCode)).ToArray()));
    });

app.MapPost(
    "/api/v1/upload-sessions",
    async (
        CreateUploadSessionRequest request,
        HttpContext context,
        UploadSessionService uploads,
        UploadSessionOptions uploadOptions,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await uploads.CreateAsync(
            new CreateUploadSessionCommand(
                userId,
                deviceId,
                request.DestinationFolderId,
                request.FileName ?? string.Empty,
                request.Size,
                request.ContentType,
                request.Sha256,
                context.Request.Headers["Idempotency-Key"].ToString(),
                context.TraceIdentifier,
                ParseBackupUpload(request.Backup)),
            cancellationToken);
        if (!result.IsSuccess)
        {
            return TransferError(result.Failure!, context, uploadOptions);
        }

        var created = result.Value!;
        context.Response.Headers.Location = $"/api/v1/upload-sessions/{created.Session.Id}";
        context.Response.Headers["Upload-Offset"] = created.Session.NextOffset.ToString(CultureInfo.InvariantCulture);
        return Results.Json(
            created.Session,
            statusCode: created.Created ? StatusCodes.Status201Created : StatusCodes.Status200OK);
    });

app.MapGet(
    "/api/v1/upload-sessions/{sessionId:guid}",
    async (
        Guid sessionId,
        HttpContext context,
        UploadSessionService uploads,
        UploadSessionOptions uploadOptions,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await uploads.GetAsync(userId, deviceId, sessionId, cancellationToken);
        if (!result.IsSuccess)
        {
            return TransferError(result.Failure!, context, uploadOptions);
        }

        context.Response.Headers["Upload-Offset"] = result.Value!.NextOffset.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(result.Value);
    });

app.MapPut(
    "/api/v1/upload-sessions/{sessionId:guid}/chunks",
    async (
        Guid sessionId,
        HttpContext context,
        UploadSessionService uploads,
        UploadSessionOptions uploadOptions,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!string.Equals(context.Request.ContentType, "application/octet-stream", StringComparison.OrdinalIgnoreCase) ||
            context.Request.ContentLength is not long length ||
            !long.TryParse(
                context.Request.Headers["Upload-Offset"].ToString(),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var offset))
        {
            return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
        }

        var result = await uploads.UploadChunkAsync(
            new UploadChunkCommand(
                userId,
                deviceId,
                sessionId,
                offset,
                length,
                context.Request.Headers["X-Chunk-Sha256"].ToString(),
                context.Request.Body,
                context.TraceIdentifier),
            cancellationToken);
        if (!result.IsSuccess)
        {
            if (result.Failure!.Code == FileErrorCodes.UploadOffsetMismatch)
            {
                var current = await uploads.GetAsync(userId, deviceId, sessionId, cancellationToken);
                if (current.IsSuccess)
                {
                    context.Response.Headers["Upload-Offset"] =
                        current.Value!.NextOffset.ToString(CultureInfo.InvariantCulture);
                }
            }

            return TransferError(result.Failure!, context, uploadOptions);
        }

        context.Response.Headers["Upload-Offset"] = result.Value!.NextOffset.ToString(CultureInfo.InvariantCulture);
        return Results.Ok(result.Value);
    });

app.MapPost(
    "/api/v1/upload-sessions/{sessionId:guid}/complete",
    async (
        Guid sessionId,
        HttpContext context,
        UploadSessionService uploads,
        UploadSessionOptions uploadOptions,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await uploads.CompleteAsync(
            userId,
            deviceId,
            sessionId,
            context.TraceIdentifier,
            cancellationToken);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : TransferError(result.Failure!, context, uploadOptions);
    });

app.MapDelete(
    "/api/v1/upload-sessions/{sessionId:guid}",
    async (
        Guid sessionId,
        HttpContext context,
        UploadSessionService uploads,
        UploadSessionOptions uploadOptions,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await uploads.CancelAsync(
            userId,
            deviceId,
            sessionId,
            context.TraceIdentifier,
            cancellationToken);
        return result.IsSuccess
            ? Results.NoContent()
            : TransferError(result.Failure!, context, uploadOptions);
    });

app.MapMethods(
    "/api/v1/files/{fileId:guid}/content",
    [HttpMethods.Get, HttpMethods.Head],
    async (
        Guid fileId,
        string? variant,
        string? disposition,
        HttpContext context,
        FileService files,
        PreviewService previews,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        if (!MediaContractRules.TryParseVariant(variant, out var parsedVariant))
        {
            return Error(StatusCodes.Status400BadRequest, MediaErrorCodes.VariantUnsupported, context);
        }

        if (!MediaContractRules.TryParseDisposition(disposition, out var parsedDisposition))
        {
            return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
        }

        if (parsedVariant != MediaVariant.Original)
        {
            var preview = await previews.RequestAsync(
                new MediaContentRequest(userId, fileId, variant, disposition), cancellationToken);
            if (!preview.IsSuccess)
            {
                return MediaError(preview.Failure!, context);
            }

            if (preview.Value!.Status == MediaRequestStatus.Ready)
            {
                return new LeasedMediaResult(preview.Value.Content!);
            }

            if (preview.Value.Status == MediaRequestStatus.Failed)
            {
                return MediaCodeError(preview.Value.ErrorCode!, context);
            }

            context.Response.Headers.RetryAfter = preview.Value.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            var jobId = preview.Value.JobId!.Value;
            context.Response.Headers.Location = $"/api/v1/media-jobs/{jobId}";
            context.Response.Headers["X-Kura-Media-Job-Id"] = jobId.ToString();
            if (HttpMethods.IsHead(context.Request.Method))
            {
                return Results.StatusCode(StatusCodes.Status202Accepted);
            }
            return Results.Json(
                new MediaAcceptedResponse(
                    "GENERATING",
                    jobId,
                    $"/api/v1/media-jobs/{jobId}",
                    preview.Value.RetryAfterSeconds),
                statusCode: StatusCodes.Status202Accepted);
        }

        var result = await files.DownloadAsync(userId, fileId, cancellationToken);
        if (!result.IsSuccess)
        {
            return ToFileHttpResult(result, context);
        }

        if (!ValidSingleRange(context.Request.Headers.Range.ToString(), result.Value!.Item.Size))
        {
            await result.Value.Content.DisposeAsync();
            context.Response.Headers.ContentRange = $"bytes */{result.Value.Item.Size}";
            return Error(StatusCodes.Status416RangeNotSatisfiable, "RANGE_NOT_SATISFIABLE", context);
        }

        if (disposition is not null && parsedDisposition == MediaDisposition.Inline)
        {
            context.Response.Headers.ContentDisposition = MediaContentDisposition.Format(
                parsedDisposition, result.Value.Item.Name);
        }

        return Results.File(
            result.Value.Content,
            result.Value.Item.MimeType ?? "application/octet-stream",
            disposition is not null && parsedDisposition == MediaDisposition.Inline ? null : result.Value.Item.Name,
            enableRangeProcessing: true);
    });

app.MapGet(
    "/api/v1/media/thumbnail-jobs/summary",
    async (
        HttpContext context,
        ThumbnailJobSummaryService summaries,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return Results.Ok(await summaries.GetAsync(userId, cancellationToken));
    });

app.MapGet(
    "/api/v1/media-jobs/{jobId:guid}",
    async (Guid jobId, HttpContext context, PreviewService previews, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await previews.GetJobAsync(userId, jobId, cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : MediaError(result.Failure!, context);
    });

app.MapPost(
    "/api/v1/media-jobs/{jobId:guid}/retry",
    async (Guid jobId, HttpContext context, PreviewService previews, CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var result = await previews.RetryJobAsync(userId, jobId, cancellationToken);
        if (!result.IsSuccess)
        {
            return MediaError(result.Failure!, context);
        }

        context.Response.Headers.RetryAfter = result.Value!.RetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Results.Json(result.Value, statusCode: StatusCodes.Status202Accepted);
    });

app.MapDelete(
    "/api/v1/files/{fileId:guid}",
    async (
        Guid fileId,
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToFileHttpResult(
            await files.TrashAsync(
                new TrashFileCommand(userId, deviceId, fileId, context.TraceIdentifier),
                cancellationToken),
            context);
    });

app.MapGet(
    "/api/v1/trash",
    async (
        int? page,
        int? pageSize,
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToFileHttpResult(
            await files.ListTrashAsync(userId, page ?? 1, pageSize ?? 100, cancellationToken),
            context);
    });

app.MapDelete(
    "/api/v1/trash/{fileId:guid}",
    async (
        Guid fileId,
        HttpContext context,
        TrashPurgeService trashPurge,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId) ||
            !TryClaimGuid(context.User, "device_id", out var deviceId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
        if (!Guid.TryParse(idempotencyKey, out _))
        {
            return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
        }

        var result = await trashPurge.PurgeAsync(
            new PurgeFileCommand(
                userId,
                deviceId,
                fileId,
                idempotencyKey,
                context.TraceIdentifier),
            cancellationToken);
        return result.IsSuccess ? Results.NoContent() : ToFileHttpResult(result, context);
    });

app.MapPost(
    "/api/v1/files/{fileId:guid}/restore",
    async (
        Guid fileId,
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToFileHttpResult(await files.RestoreAsync(userId, fileId, cancellationToken), context);
    });

app.Run();

static bool TryClaimGuid(System.Security.Claims.ClaimsPrincipal? principal, string claimType, out Guid value) =>
    Guid.TryParse(principal?.FindFirst(claimType)?.Value, out value);

static bool TryAuthenticatedUserId(HttpContext context, out Guid value) =>
    TryClaimGuid(context.User, JwtRegisteredClaimNames.Sub, out value);

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

static IResult ToFileHttpResult<T>(FileResult<T> result, HttpContext context)
{
    if (result.IsSuccess)
    {
        return Results.Ok(result.Value);
    }

    var status = result.Failure!.Kind switch
    {
        FileFailureKind.BadRequest => StatusCodes.Status400BadRequest,
        FileFailureKind.NotFound => StatusCodes.Status404NotFound,
        FileFailureKind.Conflict => StatusCodes.Status409Conflict,
        FileFailureKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        FileFailureKind.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        FileFailureKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        FileFailureKind.StorageUnavailable => StatusCodes.Status503ServiceUnavailable,
        FileFailureKind.CapacityInsufficient => StatusCodes.Status507InsufficientStorage,
        _ => StatusCodes.Status500InternalServerError,
    };
    return Error(status, result.Failure.Code, context);
}

static IResult ToTextFileHttpResult<T>(TextFileResult<T> result, HttpContext context)
{
    if (result.IsSuccess)
    {
        return Results.Ok(result.Value);
    }

    var status = result.Failure!.Kind switch
    {
        TextFileFailureKind.BadRequest => StatusCodes.Status400BadRequest,
        TextFileFailureKind.NotFound => StatusCodes.Status404NotFound,
        TextFileFailureKind.Conflict => StatusCodes.Status409Conflict,
        TextFileFailureKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        TextFileFailureKind.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        TextFileFailureKind.UnsupportedMediaType => StatusCodes.Status415UnsupportedMediaType,
        TextFileFailureKind.StorageUnavailable => StatusCodes.Status503ServiceUnavailable,
        TextFileFailureKind.CapacityInsufficient => StatusCodes.Status507InsufficientStorage,
        _ => StatusCodes.Status500InternalServerError,
    };
    return Error(status, result.Failure.Code, context);
}

static IResult ToSharingHttpResult<T>(SharingResult<T> result, HttpContext context)
{
    if (result.IsSuccess)
    {
        return Results.Ok(result.Value);
    }

    var status = result.Failure!.Kind switch
    {
        SharingFailureKind.BadRequest => StatusCodes.Status400BadRequest,
        SharingFailureKind.NotFound => StatusCodes.Status404NotFound,
        SharingFailureKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status500InternalServerError,
    };
    return Error(status, result.Failure.Code, context);
}

static IResult ToSearchHttpResult<T>(SearchResult<T> result, HttpContext context) =>
    result.IsSuccess
        ? Results.Ok(result.Value)
        : Error(StatusCodes.Status400BadRequest, result.Failure!.Code, context);

static IResult ToRecentFileHttpResult<T>(RecentFileResult<T> result, HttpContext context) =>
    result.IsSuccess
        ? Results.Ok(result.Value)
        : Error(
            result.Failure!.Kind == RecentFileFailureKind.NotFound
                ? StatusCodes.Status404NotFound
                : StatusCodes.Status400BadRequest,
            result.Failure.Code,
            context);

static IResult ToOrganizationHttpResult<T>(OrganizationResult<T> result, HttpContext context)
{
    if (result.IsSuccess)
    {
        return Results.Ok(result.Value);
    }

    var status = result.Failure!.Kind switch
    {
        OrganizationFailureKind.NotFound => StatusCodes.Status404NotFound,
        OrganizationFailureKind.Conflict => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status400BadRequest,
    };
    return Error(status, result.Failure.Code, context);
}

static bool TryCreateSearchQuery(SearchHttpQuery query, out SearchQuery? result)
{
    result = null;
    if (!TryOptionalDateTime(query.UpdatedFrom, out var updatedFrom) ||
        !TryOptionalDateTime(query.UpdatedTo, out var updatedTo) ||
        !TryOptionalLong(query.MinSize, out var minSize) ||
        !TryOptionalLong(query.MaxSize, out var maxSize) ||
        !TryOptionalGuid(query.OwnerUserId, out var ownerUserId) ||
        !TryOptionalGuid(query.ShareTargetId, out var shareTargetId) ||
        !TryTagIds(query.TagId, out var tagIds) ||
        !TryOptionalInt(query.Page, 1, out var page) ||
        !TryOptionalInt(query.PageSize, 50, out var pageSize))
    {
        return false;
    }

    result = new SearchQuery(
        query.Q,
        query.EntryType,
        query.FileCategory,
        query.Status,
        updatedFrom,
        updatedTo,
        minSize,
        maxSize,
        ownerUserId,
        shareTargetId,
        page,
        pageSize,
        tagIds);
    return true;
}

static bool TryTagIds(string[]? values, out IReadOnlyList<Guid> tagIds)
{
    tagIds = [];
    if (values is null || values.Length == 0)
    {
        return true;
    }

    var parsed = new List<Guid>(values.Length);
    foreach (var value in values)
    {
        if (!Guid.TryParse(value, out var tagId) || tagId == Guid.Empty)
        {
            return false;
        }

        parsed.Add(tagId);
    }

    tagIds = parsed;
    return true;
}

static bool TryOptionalDateTime(string? value, out DateTimeOffset? parsed)
{
    parsed = null;
    if (value is null)
    {
        return true;
    }

    if (!DateTimeOffset.TryParseExact(
            value,
            ["O", "yyyy-MM-dd'T'HH:mm:ss'Z'", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF'Z'"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var dateTime) ||
        dateTime.Offset != TimeSpan.Zero)
    {
        return false;
    }

    parsed = dateTime;
    return true;
}

static bool TryOptionalLong(string? value, out long? parsed)
{
    parsed = null;
    if (value is null)
    {
        return true;
    }

    if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
    {
        return false;
    }

    parsed = number;
    return true;
}

static bool TryOptionalGuid(string? value, out Guid? parsed)
{
    parsed = null;
    if (value is null)
    {
        return true;
    }

    if (!Guid.TryParseExact(value, "D", out var identifier))
    {
        return false;
    }

    parsed = identifier;
    return true;
}

static bool TryOptionalInt(string? value, int defaultValue, out int parsed)
{
    parsed = defaultValue;
    return value is null || int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out parsed);
}

static bool TrySharePermission(string? value, out SharePermission permission) =>
    Enum.TryParse(value, true, out permission) && Enum.IsDefined(permission);

static bool TryTargetType(string? value, out FileEntryType? targetType)
{
    targetType = null;
    if (string.IsNullOrWhiteSpace(value))
    {
        return true;
    }

    if (!Enum.TryParse<FileEntryType>(value, true, out var parsed) || !Enum.IsDefined(parsed))
    {
        return false;
    }

    targetType = parsed;
    return true;
}

static BackupUploadRequest? ParseBackupUpload(BackupUploadRequestBody? request)
{
    if (request is null)
    {
        return null;
    }

    var decision = request.Decision?.ToUpperInvariant() switch
    {
        "NEW" => BackupUploadDecision.New,
        "CHANGED" => BackupUploadDecision.Changed,
        _ => (BackupUploadDecision)(-1),
    };
    return new BackupUploadRequest(
        request.LocalDocumentKey ?? string.Empty,
        request.RelativePath ?? string.Empty,
        request.ModifiedAt,
        decision,
        request.ExpectedRemoteFileId,
        request.ExpectedRemoteFileVersion);
}

static IResult TransferError(
    FileFailure failure,
    HttpContext context,
    UploadSessionOptions options)
{
    if (failure.Kind == FileFailureKind.TooManyRequests)
    {
        context.Response.Headers.RetryAfter = options.OverloadRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
    }

    var status = failure.Kind switch
    {
        FileFailureKind.BadRequest => StatusCodes.Status400BadRequest,
        FileFailureKind.NotFound => StatusCodes.Status404NotFound,
        FileFailureKind.Conflict => StatusCodes.Status409Conflict,
        FileFailureKind.Unprocessable => StatusCodes.Status422UnprocessableEntity,
        FileFailureKind.PayloadTooLarge => StatusCodes.Status413PayloadTooLarge,
        FileFailureKind.TooManyRequests => StatusCodes.Status429TooManyRequests,
        FileFailureKind.StorageUnavailable => StatusCodes.Status503ServiceUnavailable,
        FileFailureKind.CapacityInsufficient => StatusCodes.Status507InsufficientStorage,
        _ => StatusCodes.Status500InternalServerError,
    };
    return Error(status, failure.Code, context);
}

static IResult MediaError(MediaFailure failure, HttpContext context)
{
    var status = failure.Kind switch
    {
        MediaFailureKind.BadRequest => StatusCodes.Status400BadRequest,
        MediaFailureKind.NotFound => StatusCodes.Status404NotFound,
        MediaFailureKind.Conflict => StatusCodes.Status409Conflict,
        MediaFailureKind.StorageUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status500InternalServerError,
    };
    return Error(status, failure.Code, context);
}

static IResult MediaCodeError(string code, HttpContext context) => code switch
{
    FileErrorCodes.FileNotFound => Error(StatusCodes.Status404NotFound, code, context),
    FileErrorCodes.StorageUnavailable => Error(StatusCodes.Status503ServiceUnavailable, code, context),
    MediaErrorCodes.VariantUnsupported => Error(StatusCodes.Status400BadRequest, code, context),
    _ => Error(StatusCodes.Status409Conflict, code, context),
};

static async Task<IResult> HandleUploadAsync(
    Guid userId,
    Guid deviceId,
    HttpContext context,
    FileService files,
    CancellationToken cancellationToken)
{
    var contentType = context.Request.ContentType;
    if (string.IsNullOrWhiteSpace(contentType) ||
        !MediaTypeHeaderValue.TryParse(contentType, out var mediaType) ||
        !string.Equals(mediaType.MediaType.Value, "multipart/form-data", StringComparison.OrdinalIgnoreCase))
    {
        return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
    }

    var boundary = HeaderUtilities.RemoveQuotes(mediaType.Boundary).Value;
    var idempotencyKey = context.Request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > 256 || string.IsNullOrWhiteSpace(idempotencyKey))
    {
        return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
    }

    var reader = new MultipartReader(boundary, context.Request.Body);
    var fields = new Dictionary<string, string>(StringComparer.Ordinal);
    FileResult<FileItem>? uploadResult = null;
    MultipartSection? section;
    while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
    {
        if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition))
        {
            return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
        }

        var fieldName = HeaderUtilities.RemoveQuotes(disposition.Name).Value;
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
        }

        var isFile = disposition.FileName.HasValue || disposition.FileNameStar.HasValue;
        if (!isFile)
        {
            using var textReader = new StreamReader(section.Body, leaveOpen: true);
            var value = await textReader.ReadToEndAsync(cancellationToken);
            if (value.Length > 2048)
            {
                return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
            }

            fields[fieldName] = value;
            continue;
        }

        if (uploadResult is not null ||
            !Guid.TryParse(GetField(fields, "destinationFolderId"), out var destinationFolderId) ||
            !long.TryParse(
                GetField(fields, "size"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var size))
        {
            return Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context);
        }

        uploadResult = await files.UploadAsync(
            new UploadFileCommand(
                userId,
                deviceId,
                destinationFolderId,
                GetField(fields, "fileName") ?? string.Empty,
                size,
                GetField(fields, "contentType") ?? section.ContentType,
                GetField(fields, "sha256"),
                idempotencyKey,
                context.TraceIdentifier,
                section.Body),
            cancellationToken);
    }

    return uploadResult is null
        ? Error(StatusCodes.Status400BadRequest, FileErrorCodes.ValidationFailed, context)
        : ToFileHttpResult(uploadResult, context);
}

static string? GetField(IReadOnlyDictionary<string, string> fields, string key) =>
    fields.TryGetValue(key, out var value) ? value : null;

static bool ValidSingleRange(string rangeHeader, long length)
{
    if (string.IsNullOrWhiteSpace(rangeHeader))
    {
        return true;
    }

    if (!System.Net.Http.Headers.RangeHeaderValue.TryParse(rangeHeader, out var parsed) ||
        !string.Equals(parsed.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
        parsed.Ranges.Count != 1)
    {
        return false;
    }

    var range = parsed.Ranges.Single();
    if (range.From is null)
    {
        return range.To is > 0;
    }

    return range.From.Value < length && (range.To is null || range.To >= range.From);
}

static IResult Error(int status, string code, HttpContext context) =>
    Results.Json(
        new ErrorResponse(code, "The request could not be completed.", context.TraceIdentifier, new { }),
        statusCode: status);

static async Task<T?> ReadJsonAsync<T>(HttpRequest request, CancellationToken cancellationToken)
{
    try
    {
        return await JsonSerializer.DeserializeAsync<T>(
            request.Body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web),
            cancellationToken);
    }
    catch (JsonException)
    {
        return default;
    }
}

public sealed record RegisterDeviceRequest(string? Username, string? Password, string? DeviceName);

public sealed record LoginRequest(string? Username, string? Password, Guid DeviceId);

public sealed record RefreshRequest(Guid DeviceId, string? RefreshToken);

public sealed record LogoutRequest(Guid DeviceId, string? RefreshToken);

public sealed record CreateFolderRequest(Guid? ParentId, string? Name);

public sealed record CreateShareRequest(Guid TargetEntryId, IReadOnlyList<CreateShareMemberRequest>? Members);

public sealed record CreateShareMemberRequest(Guid UserId, string? Permission);

public sealed record SetShareMemberRequest(string? Permission);

public sealed record CreateTagRequest(string? Name);

public sealed record RenameTagRequest(string? Name);

public sealed class SearchHttpQuery
{
    public string? Q { get; init; }
    public string? EntryType { get; init; }
    public string? FileCategory { get; init; }
    public string? Status { get; init; }
    public string? UpdatedFrom { get; init; }
    public string? UpdatedTo { get; init; }
    public string? MinSize { get; init; }
    public string? MaxSize { get; init; }
    public string? OwnerUserId { get; init; }
    public string? ShareTargetId { get; init; }
    public string[]? TagId { get; init; }
    public string? Page { get; init; }
    public string? PageSize { get; init; }
}

public sealed class RecentFilesHttpQuery
{
    public string? Page { get; init; }
    public string? PageSize { get; init; }
}

public sealed class ActivitiesHttpQuery
{
    public string? Type { get; init; }
    public string? Cursor { get; init; }
    public string? PageSize { get; init; }
}

public sealed class FileVersionsHttpQuery
{
    public string? Page { get; init; }
    public string? PageSize { get; init; }
}

public sealed record CreateUploadSessionRequest(
    Guid DestinationFolderId,
    string? FileName,
    long Size,
    string? ContentType,
    string? Sha256,
    BackupUploadRequestBody? Backup = null);

public sealed record BackupUploadRequestBody(
    string? LocalDocumentKey,
    string? RelativePath,
    DateTimeOffset ModifiedAt,
    string? Decision,
    Guid? ExpectedRemoteFileId,
    long? ExpectedRemoteFileVersion);

public sealed record BackupCompareRequest(
    Guid DestinationFolderId,
    IReadOnlyList<BackupCompareRequestItem>? Items);

public sealed record BackupCompareRequestItem(
    string? LocalDocumentKey,
    string? RelativePath,
    long Size,
    DateTimeOffset ModifiedAt,
    string? Checksum);

public sealed record BackupCompareResponse(IReadOnlyList<BackupCompareResponseItem> Items);

public sealed record BackupCompareResponseItem(
    string LocalDocumentKey,
    string Decision,
    Guid? RemoteFileId,
    long? ExpectedRemoteFileVersion,
    string? ErrorCode);

public sealed record MediaAcceptedResponse(
    string Status,
    Guid JobId,
    string JobStatusUrl,
    int RetryAfterSeconds);

public sealed class UpdateFileRequest
{
    public string? Name { get; init; }

    public Guid? ParentId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class SaveTextRequest
{
    public string? Content { get; init; }

    public long ExpectedVersion { get; init; }

    public Guid OperationId { get; init; }

    public bool? AcknowledgeLossySource { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class RestoreTextVersionRequest
{
    public long ExpectedVersion { get; init; }

    public Guid OperationId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record ErrorResponse(string Code, string Message, string RequestId, object Details);

public partial class Program;
