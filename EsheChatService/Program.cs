using EsheChatService.Components;
using EsheChatService.Data;
using EsheChatService.Hubs;
using EsheChatService.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<ChatService>();
builder.Services.AddScoped<EsheChatService.Services.Repositories.IChatRepository, EsheChatService.Services.Repositories.ChatRepository>();
builder.Services.AddScoped<ChatSessionManager>();
builder.Services.AddScoped<ToastService>();
builder.Services.AddHttpClient<ChatService>();
builder.Services.AddScoped<UserManager>();
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
                ctx.Fail("Invalid Google login");
                return;
            }

            var userManager = ctx.HttpContext.RequestServices
                .GetRequiredService<UserManager>();

            try
            {
                await userManager.ValidateAndUpdateGoogleUserAsync(email, sub);
            }
            catch
            {
                ctx.Fail("User is not registered");
            }
        };
    });


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();
app.UseAuthentication();
app.UseAuthorization();
app.MapHub<ChatHub>("/chathub");
app.MapGet("/login", async (HttpContext ctx) =>
{
    await ctx.ChallengeAsync("Google", new AuthenticationProperties
    {
        RedirectUri = "/"
    });
});
app.MapGet("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync("Cookies");
    ctx.Response.Redirect("/");
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
