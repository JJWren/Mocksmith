using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mocksmith.Components;
using Mocksmith.Core.Data;
using Mocksmith.Core.Security;

if (args is ["hash-password", var passwordToHash])
{
    Console.WriteLine(PasswordHasher.Hash(passwordToHash));
    return;
}

var builder = WebApplication.CreateBuilder(args);

var configuredUsername = builder.Configuration["MOCKSMITH_USERNAME"];
var configuredPasswordHash = builder.Configuration["MOCKSMITH_PASSWORD_HASH"];
if (string.IsNullOrWhiteSpace(configuredUsername) || string.IsNullOrWhiteSpace(configuredPasswordHash))
{
    throw new InvalidOperationException(
        "MOCKSMITH_USERNAME and MOCKSMITH_PASSWORD_HASH must be set. " +
        "Generate a hash with: dotnet run --project src/Mocksmith -- hash-password 'your-password'");
}

var dataDirectory = builder.Configuration["MOCKSMITH_DATA_DIR"]
    ?? Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataDirectory);

builder.Services.AddDbContextFactory<MocksmithDbContext>(options =>
    options.UseSqlite($"Data Source={Path.Combine(dataDirectory, "mocksmith.db")}"));

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "mocksmith.auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services.AddHealthChecks()
    .AddDbContextCheck<MocksmithDbContext>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MocksmithDbContext>();
    db.Database.Migrate();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

// TLS terminates at the reverse proxy in deployment; the app itself serves plain HTTP.
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapHealthChecks("/healthz").AllowAnonymous();

app.MapPost("/auth/login", async Task<IResult> (
    HttpContext context,
    IConfiguration config,
    IAntiforgery antiforgery,
    [FromForm] string username,
    [FromForm] string password,
    [FromForm] string? returnUrl) =>
{
    // The [FromForm] binding already makes UseAntiforgery reject token-less requests before this
    // delegate runs (verified); this explicit check is defense-in-depth against binding changes.
    if (!await IsAntiforgeryValidAsync(context, antiforgery))
    {
        return Results.BadRequest("Invalid antiforgery token.");
    }

    var safeReturnUrl = returnUrl is ['/', ..] && !returnUrl.StartsWith("//") ? returnUrl : "/";
    if (username == config["MOCKSMITH_USERNAME"]
        && PasswordHasher.Verify(password, config["MOCKSMITH_PASSWORD_HASH"]!))
    {
        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, username)],
            CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
        return Results.LocalRedirect(safeReturnUrl);
    }

    return Results.Redirect($"/login?error=1&returnUrl={Uri.EscapeDataString(safeReturnUrl)}");
}).AllowAnonymous()
  // Surface auth-endpoint 4xx directly instead of re-executing into the /not-found page.
  .WithMetadata(new SkipStatusCodePagesAttribute());

app.MapPost("/auth/logout", async Task<IResult> (HttpContext context, IAntiforgery antiforgery) =>
{
    if (!await IsAntiforgeryValidAsync(context, antiforgery))
    {
        return Results.BadRequest("Invalid antiforgery token.");
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
}).RequireAuthorization()
  .WithMetadata(new SkipStatusCodePagesAttribute());

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .RequireAuthorization();

app.Run();

static async Task<bool> IsAntiforgeryValidAsync(HttpContext context, IAntiforgery antiforgery)
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
        return true;
    }
    catch (AntiforgeryValidationException)
    {
        return false;
    }
}
