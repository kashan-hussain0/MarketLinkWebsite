using Microsoft.AspNetCore.Mvc.Filters;

namespace MarketLinkWebsite.Security;

public sealed class NoStoreForAuthenticatedAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        var user = context.HttpContext.User;
        if (user.Identity?.IsAuthenticated == true)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";
            context.HttpContext.Response.Headers.Pragma = "no-cache";
            context.HttpContext.Response.Headers.Expires = "0";
        }

        base.OnActionExecuting(context);
    }
}
