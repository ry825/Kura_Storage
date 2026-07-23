using System.Net;
using KuraStorage.Api;
using Microsoft.AspNetCore.Http;

namespace KuraStorage.IntegrationTests;

public sealed class RouteHeaderMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenRequestUsesTcp_DoesNotTrustSpoofedRouteHeader()
    {
        var context = new DefaultHttpContext();
        context.Connection.LocalIpAddress = IPAddress.Loopback;
        context.Connection.RemoteIpAddress = IPAddress.Loopback;
        context.Request.Headers[RouteHeaderMiddleware.HeaderName] = RouteHeaderMiddleware.LocalDirect;
        var middleware = new RouteHeaderMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Null(context.Items[RouteHeaderMiddleware.HeaderName]);
    }

    [Theory]
    [InlineData(RouteHeaderMiddleware.LocalDirect)]
    [InlineData(RouteHeaderMiddleware.RemoteSecure)]
    public async Task InvokeAsync_WhenRequestUsesUnixSocket_TrustsOnlyKnownRoute(string route)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[RouteHeaderMiddleware.HeaderName] = route;
        var middleware = new RouteHeaderMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal(route, context.Items[RouteHeaderMiddleware.HeaderName]);
    }
}
