using KuraStorage.Application.Abstractions;
using KuraStorage.Domain.Activity;
using KuraStorage.Domain.Audit;
using KuraStorage.Application.Identity;
using KuraStorage.Application.Maintenance;
using KuraStorage.Application.Activity;
using Xunit;

namespace KuraStorage.Application.Tests;

public sealed class UserActivitySeparationTests
{
    [Fact]
    public void UserActivityRepositoryContract_CannotQuerySecurityAudit()
    {
        var exposedTypes = typeof(IUserActivityRepository)
            .GetMethods()
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .ToArray();

        Assert.DoesNotContain(exposedTypes, type => Contains(type, typeof(AuditLog)));
    }

    [Fact]
    public void UserActivityModel_DoesNotExposeAuditOrSecretFields()
    {
        var propertyNames = typeof(UserActivity).GetProperties().Select(property => property.Name).ToHashSet();
        Assert.DoesNotContain("RequestId", propertyNames);
        Assert.DoesNotContain("ActorOsUser", propertyNames);
        Assert.DoesNotContain("ResultCode", propertyNames);
        Assert.DoesNotContain("PhysicalPath", propertyNames);
        Assert.DoesNotContain("Content", propertyNames);
        Assert.DoesNotContain("Token", propertyNames);
        Assert.DoesNotContain("Metadata", propertyNames);
    }

    [Theory]
    [InlineData(typeof(IdentityService))]
    [InlineData(typeof(AdminStorageService))]
    public void SecurityAndAdminServices_DoNotDependOnUserActivityRecording(Type serviceType)
    {
        var dependencies = serviceType.GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IUserActivityRepository), dependencies);
        Assert.DoesNotContain(typeof(UserActivityFactory), dependencies);
    }

    private static bool Contains(Type candidate, Type prohibited) =>
        candidate == prohibited ||
        candidate.IsGenericType && candidate.GetGenericArguments().Any(argument => Contains(argument, prohibited));
}
