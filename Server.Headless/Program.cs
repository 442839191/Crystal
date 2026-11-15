using System.Reflection;
using log4net;
using log4net.Config;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Server;
using Server.Headless;
using Server.MirEnvir;

Packet.IsServer = true;

foreach (var logDirectory in new[] { "Logs/Server", "Logs/Chat", "Logs/Spawn", "Logs/Player", "Logs/Debug", "Logs/Web" })
    Directory.CreateDirectory(logDirectory);

var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
XmlConfigurator.Configure(logRepository, new FileInfo("log4net.config"));

Settings.Load();

var webSettings = WebAdminSettings.LoadOrCreate();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://{webSettings.BindAddress}:{webSettings.Port}");
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

var keysPath = Path.Combine("Configs", "DataProtectionKeys");
Directory.CreateDirectory(keysPath);
builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keysPath));

builder.Services.AddRazorPages();
builder.Services.AddSingleton(webSettings);

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "mir2admin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.LoginPath = "/login";
        options.AccessDeniedPath = "/login";
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            else
                context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();

var app = builder.Build();

app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api") &&
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        !string.Equals(context.Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.Ordinal))
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { message = "缺少请求头 X-Requested-With" });
        return;
    }

    await next();
});

app.MapRazorPages();
app.MapAdminApi();

if (webSettings.AutoStartServer)
{
    Envir.Main.Start();
    AdminRuntime.StartedAt = DateTime.UtcNow;
}

app.Lifetime.ApplicationStopping.Register(() =>
{
    try
    {
        if (Envir.Main.Running) Envir.Main.Stop();
    }
    catch
    {
    }

    try
    {
        Settings.Save();
    }
    catch
    {
    }
});

app.Logger.LogInformation("Web admin listening on http://{Host}:{Port}", webSettings.BindAddress, webSettings.Port);
app.Run();
