using EsheChatService.Components;
using EsheChatService.Data;
using EsheChatService.Hubs;
using EsheChatService.Services;
using EsheChatService.Services.Folders;
using EsheChatService.Services.Messages;
using EsheChatService.Services.Repositories;
using EsheChatService.Services.Sessions;
using EsheChatService.Services.Sharing;
using EsheChatService.Services.User;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System.Security.Claims;

// ── Bootstrap Serilog (before anything else) ──
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System", Serilog.Events.LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting Eshe Chat Service");

    var builder = WebApplication.CreateBuilder(args);

    // Replace default logging with Serilog
    builder.Host.UseSerilog();

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddScoped<ChatService>();
    builder.Services.AddScoped<IChatRepository, ChatRepository>();
    builder.Services.AddScoped<ISessionService, SessionService>();
    builder.Services.AddScoped<IFolderService, FolderService>();
    builder.Services.AddScoped<IMessageService, MessageService>();
    builder.Services.AddScoped<IShareService, ShareService>();
    builder.Services.AddScoped<ChatSessionManager>();
    builder.Services.AddScoped<ToastService>();
    builder.Services.AddHttpClient<ChatService>()
        .AddStandardResilienceHandler();
    builder.Services.AddScoped<IUserManager, UserManager>();
    builder.Services.AddHttpContextAccessor();
    builder.Services.AddScoped<ICurrentUser, CurrentUser>();
    builder.Services.AddSignalR();
    builder.Services.AddDbContextFactory<ChatDbContext>(options =>
        options.UseSqlServer(
            builder.Configuration.GetConnectionString("ChatDb")));
    builder.Services
        .AddAuthentication(options =>
        {
            options.DefaultScheme = "Cookies";
            options.DefaultChallengeScheme = "Google";
        })
        .AddCookie("Cookies")
        .AddGoogle("Google", options =>
        {
            options.ClientId = builder.Configuration["Auth:Google:ClientId"];
            options.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"];

            options.ClaimActions.MapJsonKey("picture", "picture", "url");
            options.Events.OnCreatingTicket = async ctx =>
            {
                var email = ctx.Principal!.FindFirst(ClaimTypes.Email)?.Value;
                var sub = ctx.Principal!.FindFirst(ClaimTypes.NameIdentifier)?.Value;

                if (email == null || sub == null)
                {
                    Log.Warning("Google OAuth failed: missing email or sub claim");
                    ctx.Fail("Invalid Google login");
                    return;
                }

                var userManager = ctx.HttpContext.RequestServices
                    .GetRequiredService<IUserManager>();

                try
                {
                    await userManager.ValidateAndUpdateGoogleUserAsync(email, sub);
                    Log.Information("User authenticated via Google OAuth: {Email}", email);
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Google OAuth rejected for unregistered user: {Email}", email);
                    ctx.Fail("User is not registered");
                }
            };
        });


    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
    }

    // Request logging middleware (Serilog)
    app.UseSerilogRequestLogging();

    app.UseHttpsRedirection();

    app.UseStaticFiles();
    app.UseAntiforgery();
    app.UseAuthentication();
    app.UseAuthorization();
    app.MapHub<ChatHub>("/chathub");
    app.MapGet("/login", async (HttpContext ctx) =>
    {
        Log.Information("Login initiated from {RemoteIp}", ctx.Connection.RemoteIpAddress);
        await ctx.ChallengeAsync("Google", new AuthenticationProperties
        {
            RedirectUri = "/"
        });
    });
    app.MapGet("/logout", async (HttpContext ctx) =>
    {
        var email = ctx.User.FindFirst(ClaimTypes.Email)?.Value;
        Log.Information("User logged out: {Email}", email ?? "unknown");
        await ctx.SignOutAsync("Cookies");
        ctx.Response.Redirect("/");
    });

    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
