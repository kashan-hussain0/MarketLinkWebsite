using MarketLinkWebsite.Data;
using MarketLinkWebsite.Models.Entities;
using MarketLinkWebsite.Services;
using MarketLinkWebsite.Navigation;
using MarketLinkWebsite.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// The SMTP password is a secret, so it lives in the user secret store rather than
// in any appsettings file. Without this line the store is never read.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");

builder.Services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddHttpContextAccessor();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit = true;
        options.Password.RequiredLength = 8;
        options.Password.RequireNonAlphanumeric = true;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.User.RequireUniqueEmail = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(10);
        options.SignIn.RequireConfirmedAccount = false;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.LogoutPath = "/Account/Login";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
});

builder.Services.AddScoped<IAuthorizationHandler, ActiveFarmerRequirementHandler>();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("CustomerAccess", policy => policy.RequireRole("Customer", "Admin"));
            options.AddPolicy("CustomerOnly", policy => policy.RequireRole("Customer"));
    options.AddPolicy("FarmerAccess", policy => policy.RequireRole("Farmer"));
    options.AddPolicy("AdminAccess", policy => policy.RequireRole("Admin"));
    options.AddPolicy("ActiveFarmerAccess", policy =>
        policy.RequireRole("Farmer").AddRequirements(new ActiveFarmerRequirement()));
});

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
    options.Filters.Add<NoStoreForAuthenticatedAttribute>();
});

builder.Services.AddScoped<ICartService, CartService>();
builder.Services.AddScoped<CatalogDetailService>();
builder.Services.AddScoped<AssistantService>();
builder.Services.AddScoped<MapService>();
builder.Services.AddSingleton<IImageStorage, ImageStorage>();
builder.Services.AddScoped<GeoLocationService>();
builder.Services.AddHttpClient<AiAssistantService>(client =>
{
    client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<RestockAlertService>();
builder.Services.AddHostedService<RestockAlertHostedService>();
builder.Services.AddScoped<WeeklyStockRolloverService>();
builder.Services.AddHostedService<WeeklyStockRolloverHostedService>();
builder.Services.AddScoped<ICatalogService, CatalogService>();
builder.Services.AddScoped<ReviewAggregateService>();
builder.Services.AddScoped<IEmailSender, EmailSender>();
builder.Services.AddScoped<OrderEmailComposer>();
builder.Services.AddScoped<OrderEmailDispatcher>();
builder.Services.AddScoped<PasswordResetCodeService>();
builder.Services.AddHostedService<PasswordResetCodeCleanupService>();
builder.Services.AddScoped<MarketplaceDataSeeder>();
builder.Services.AddScoped<IDatabaseSeeder, DatabaseSeeder>();
builder.Services.AddScoped<NavBarService>();
builder.Services.AddSingleton<ServerInstanceStamp>();

var app = builder.Build();

// Said out loud at startup, because a missing key looks exactly like a broken
// chatbot and is far easier to diagnose from a log than from a support ticket.
{
    var startupLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("MarketLink.Startup");

    if (string.IsNullOrWhiteSpace(builder.Configuration["Ai:ApiKey"]))
    {
        startupLogger.LogWarning(
            "Ai:ApiKey is not set, so the assistant will answer from the database only. Set the Ai__ApiKey environment variable to switch the hosted model on.");
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration["App:BaseUrl"]))
    {
        startupLogger.LogWarning("App:BaseUrl is not set, so links in email will fall back to a relative path.");
    }

    if (string.IsNullOrWhiteSpace(builder.Configuration["Email:SmtpHost"]))
    {
        startupLogger.LogWarning(
            "Email:SmtpHost is not set, so order confirmations and reset codes will not be emailed.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}
app.UseExceptionHandler("/Home/Error");
app.UseHttpsRedirection();
app.UseStatusCodePagesWithReExecute("/Home/Error", "?code={0}");

// In production nothing ever leaks to the browser. In development the developer
// exception page is added further down so stack traces stay readable while coding,
// but the handler below still guarantees a shopper only ever sees a plain message.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var log = context.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("MarketLink.Unhandled");
        log.LogError(feature?.Error, "Unhandled exception for {Path}.", context.Request.Path);

        if (context.Request.Headers.ContainsKey("X-Requested-With"))
        {
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(
                "{\"message\":\"Something went wrong on our side. Please try again.\"}");
            return;
        }

        context.Response.Redirect("/Home/Error?code=500");
    }));
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    var logger = context.RequestServices.GetRequiredService<ILoggerFactory>()
        .CreateLogger("MarketLink.Unhandled");
    logger.LogError(feature?.Error, "Unhandled exception for {Path}.", context.Request.Path);

    if (context.Request.Headers.ContainsKey("X-Requested-With"))
    {
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(
            "{\"message\":\"Something went wrong on our side. Please try again.\"}");
        return;
    }

    context.Response.Redirect("/Home/Error?code=500");
}));

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseMiddleware<ServerInstanceStampMiddleware>();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

if (app.Configuration.GetValue<bool>("Database:ApplyMigrations"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

if (app.Configuration.GetValue<bool>("Database:Seed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<IDatabaseSeeder>();
    await seeder.SeedAsync();
}

app.Run();
