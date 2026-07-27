using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using SourcingOps.Api.Filters;

namespace SourcingOps.Api.Tests.Filters;

/// <summary>
/// Direct unit coverage of E1-08's core rule, independent of any real controller: no
/// permission-gated feature endpoint exists yet this milestone to exercise the 403 path
/// end-to-end over HTTP (see build report), so this exercises the filter directly against
/// a synthetic action descriptor instead.
/// </summary>
public class RequirePasswordChangeFilterTests
{
    private static ActionExecutingContext CreateContext(ClaimsPrincipal user, IList<object> endpointMetadata)
    {
        var httpContext = new DefaultHttpContext { User = user };
        var actionDescriptor = new ActionDescriptor { EndpointMetadata = endpointMetadata };
        var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);

        return new ActionExecutingContext(
            actionContext,
            new List<IFilterMetadata>(),
            new Dictionary<string, object?>(),
            controller: new object());
    }

    private static ClaimsPrincipal AuthenticatedUser(bool mustChangePassword)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()), new("must_change_password", mustChangePassword ? "true" : "false") };
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal AnonymousUser() => new(new ClaimsIdentity()); // IsAuthenticated == false

    private static ActionExecutionDelegate MakeNext(ActionExecutingContext context, Action markCalled) => () =>
    {
        markCalled();
        return Task.FromResult(new ActionExecutedContext(context, context.Filters, context.Controller));
    };

    [Fact]
    public async Task AllowAnonymousEndpoint_IsNeverBlocked_EvenIfSomehowAuthenticated()
    {
        var sut = new RequirePasswordChangeFilter();
        var context = CreateContext(AuthenticatedUser(mustChangePassword: true), [new AllowAnonymousAttribute()]);
        var nextCalled = false;

        await sut.OnActionExecutionAsync(context, MakeNext(context, () => nextCalled = true));

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task AllowMustChangePasswordEndpoint_IsReachableEvenWhileFlagIsTrue()
    {
        var sut = new RequirePasswordChangeFilter();
        var context = CreateContext(AuthenticatedUser(mustChangePassword: true), [new AllowMustChangePasswordAttribute()]);
        var nextCalled = false;

        await sut.OnActionExecutionAsync(context, MakeNext(context, () => nextCalled = true));

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task OrdinaryAuthorizedEndpoint_WithMustChangePasswordTrue_Returns403AndDoesNotCallNext()
    {
        var sut = new RequirePasswordChangeFilter();
        var context = CreateContext(AuthenticatedUser(mustChangePassword: true), []);
        var nextCalled = false;

        await sut.OnActionExecutionAsync(context, MakeNext(context, () => nextCalled = true));

        nextCalled.Should().BeFalse();
        context.Result.Should().BeOfType<ObjectResult>();
        ((ObjectResult)context.Result!).StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        ((ObjectResult)context.Result!).Value.Should().BeOfType<ProblemDetails>();
    }

    [Fact]
    public async Task OrdinaryAuthorizedEndpoint_WithMustChangePasswordFalse_CallsNextNormally()
    {
        var sut = new RequirePasswordChangeFilter();
        var context = CreateContext(AuthenticatedUser(mustChangePassword: false), []);
        var nextCalled = false;

        await sut.OnActionExecutionAsync(context, MakeNext(context, () => nextCalled = true));

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }

    [Fact]
    public async Task UnauthenticatedCaller_IsNotBlockedByThisFilter()
    {
        // Authentication/authorization middleware is responsible for rejecting
        // unauthenticated callers on [Authorize] endpoints — this filter only enforces
        // the must-change-password scoping for callers who ARE authenticated.
        var sut = new RequirePasswordChangeFilter();
        var context = CreateContext(AnonymousUser(), []);
        var nextCalled = false;

        await sut.OnActionExecutionAsync(context, MakeNext(context, () => nextCalled = true));

        nextCalled.Should().BeTrue();
        context.Result.Should().BeNull();
    }
}
