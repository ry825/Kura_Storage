using System.IdentityModel.Tokens.Jwt;
using System.Globalization;
using System.Security.Cryptography;
using KuraStorage.Api;
using KuraStorage.Application.Abstractions;
using KuraStorage.Application.Files;
using KuraStorage.Application.Identity;
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
        FileFailureKind.StorageUnavailable => StatusCodes.Status503ServiceUnavailable,
        FileFailureKind.CapacityInsufficient => StatusCodes.Status507InsufficientStorage,
        _ => StatusCodes.Status500InternalServerError,
    };
    return Error(status, result.Failure.Code, context);
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

public sealed record ErrorResponse(string Code, string Message, string RequestId, object Details);

public partial class Program;
