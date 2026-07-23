namespace KuraStorage.Api;

public sealed class RouteHeaderMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-KuraStorage-Route";
    public const string LocalDirect = "LOCAL_DIRECT";
    public const string RemoteSecure = "REMOTE_SECURE";

    public async Task InvokeAsync(HttpContext context)
    {
        var route = context.Request.Headers[HeaderName].ToString();
        var isUnixSocket = context.Connection.LocalIpAddress is null && context.Connection.RemoteIpAddress is null;
        context.Items[HeaderName] =
            isUnixSocket && route is LocalDirect or RemoteSecure
                ? route
                : null;
        await next(context);
    }
}
