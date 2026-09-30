using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;

namespace MarketLinkWebsite.Security;

public sealed class ServerInstanceStamp
{
    public const string ClaimType = "ml:instance";

    public string Value { get; } = Guid.NewGuid().ToString("N");
}

public sealed class ServerInstanceStampMiddleware
{
    private static readonly string[] AlwaysAllowed =
    {
        "/Account/Login",
        "/Account/AccessDenied",
        "/Account/PendingApproval",
        "/Account/Register",
        "/Account/ForgotPassword",
        "/Account/ResetPassword",
        "/Home/Error"
    };

    private readonly RequestDelegate next;
    private readonly ServerInstanceStamp stamp;

    public ServerInstanceStampMiddleware(RequestDelegate next, ServerInstanceStamp stamp)
    {
        this.next = next;
        this.stamp = stamp;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true && !IsAlwaysAllowed(context.Request.Path))
        {
            var issued = context.User.FindFirst(ServerInstanceStamp.ClaimType)?.Value;

            if (!string.Equals(issued, stamp.Value, StringComparison.Ordinal))
            {
                await context.SignOutAsync(IdentityConstants.ApplicationScheme);
                context.Response.Cookies.Delete(IdentityConstants.ExternalScheme, BuildCookieOptions(context));
                context.Response.Cookies.Delete(".MarketLink.Cart", BuildCookieOptions(context));

                var returnUrl = context.Request.Path + context.Request.QueryString;
                context.Response.Redirect("/Account/Login?ReturnUrl=" + Uri.EscapeDataString(returnUrl));
                return;
            }
        }

        await next(context);
    }

    private static bool IsAlwaysAllowed(PathString path) =>
        AlwaysAllowed.Any(allowed => path.StartsWithSegments(allowed, StringComparison.OrdinalIgnoreCase));

    private static CookieOptions BuildCookieOptions(HttpContext context) => new()
    {
        HttpOnly = true,
        IsEssential = true,
        SameSite = SameSiteMode.Lax,
        Path = "/",
        Secure = context.Request.IsHttps
    };
}
