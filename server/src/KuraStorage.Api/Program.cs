using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using KuraStorage.Api;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Maintenance;
using KuraStorage.Application.Sharing;
using KuraStorage.Application.Transfers;
using KuraStorage.Application.Identity;
using KuraStorage.Domain.Files;
using KuraStorage.Domain.Sharing;
using KuraStorage.Infrastructure;
using KuraStorage.Infrastructure.Configuration;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
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
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return ToFileHttpResult(
            await files.CreateFolderAsync(userId, request.ParentId, request.Name ?? string.Empty, cancellationToken),
            context);
    });

app.MapPost(
    "/api/v1/files/upload",
    async (
        HttpContext context,
        FileService files,
        CancellationToken cancellationToken) =>
    {
        if (!TryAuthenticatedUserId(context, out var userId))
        {
            return Error(StatusCodes.Status401Unauthorized, "AUTHENTICATION_REQUIRED", context);
        }

        return await HandleUploadAsync(userId, context, files, cancellationToken);
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
                context.TraceIdentifier),
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

app.MapGet(
    "/api/v1/files/{fileId:guid}/content",
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

        return Results.File(
            result.Value.Content,
            result.Value.Item.MimeType ?? "application/octet-stream",
            result.Value.Item.Name,
            enableRangeProcessing: true);
    });

app.MapDelete(
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

        return ToFileHttpResult(await files.TrashAsync(userId, fileId, cancellationToken), context);
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

static async Task<IResult> HandleUploadAsync(
    Guid userId,
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
            userId,
            new UploadFileCommand(
                destinationFolderId,
                GetField(fields, "fileName") ?? string.Empty,
                size,
                GetField(fields, "contentType") ?? section.ContentType,
                GetField(fields, "sha256"),
                idempotencyKey,
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

public sealed record RegisterDeviceRequest(string? Username, string? Password, string? DeviceName);

public sealed record LoginRequest(string? Username, string? Password, Guid DeviceId);

public sealed record RefreshRequest(Guid DeviceId, string? RefreshToken);

public sealed record LogoutRequest(Guid DeviceId, string? RefreshToken);

public sealed record CreateFolderRequest(Guid? ParentId, string? Name);

public sealed record CreateShareRequest(Guid TargetEntryId, IReadOnlyList<CreateShareMemberRequest>? Members);

public sealed record CreateShareMemberRequest(Guid UserId, string? Permission);

public sealed record SetShareMemberRequest(string? Permission);

public sealed record CreateUploadSessionRequest(
    Guid DestinationFolderId,
    string? FileName,
    long Size,
    string? ContentType,
    string? Sha256);

public sealed class UpdateFileRequest
{
    public string? Name { get; init; }

    public Guid? ParentId { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed record ErrorResponse(string Code, string Message, string RequestId, object Details);

public partial class Program;
