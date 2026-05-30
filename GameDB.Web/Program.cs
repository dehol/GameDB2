using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Hosting;
using Serilog;
using GameDB.Application.Services;
using GameDB.Application.Options;
using GameDB.Application.Interfaces;
using GameDB.Infrastructure.Data;
using GameDB.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;
using GameDB.Infrastructure.Data.Repositories;
using GameDB.Infrastructure.ExternalProviders;
using Microsoft.AspNetCore.Authentication.Cookies;
using GameDB.Infrastructure.Catalog;

// Bootstrap-логер
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Запуск сервера GameDB...");

    var builder = WebApplication.CreateBuilder(args);

    // ── Razor Pages + Controllers ────────────────────────────────────────────
    builder.Services.AddRazorPages();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    // ── Cookie-аутентифікація ────────────────────────────────────────────────
    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath        = "/Auth/Login";
            options.LogoutPath       = "/auth/logout";
            options.AccessDeniedPath = "/Auth/Login";
            options.Cookie.HttpOnly  = true;
            options.Cookie.SameSite  = SameSiteMode.Lax;
            // HTTPS-only у Production
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan    = TimeSpan.FromDays(7);
        });

    builder.Services.AddAuthorization(options =>
{
    // Дозволяємо гостям і авторизованим
    options.AddPolicy("GuestOrUser", policy =>
        policy.RequireAssertion(ctx =>
            ctx.User.Identity?.IsAuthenticated == true));

    // Тільки зареєстровані (не гості)
    options.AddPolicy("RegisteredOnly", policy =>
        policy.RequireRole("User"));

    options.AddPolicy("AdminOnly", policy =>
        policy.RequireRole(GameDB.Web.Services.AuthCookieService.AdminRole));
});

    // ── База даних ───────────────────────────────────────────────────────────
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    // ── Репозиторії ──────────────────────────────────────────────────────────
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IGameOfferRepository, GameOfferRepository>();
    builder.Services.AddScoped<IGameRepository, GameRepository>();
    builder.Services.AddScoped<ILookupRepository, LookupRepository>();
    builder.Services.AddScoped<ICatalogRepository, CatalogRepository>();
    builder.Services.AddScoped<IUserCollectionRepository, UserCollectionRepository>();
    builder.Services.AddScoped<IGameShopRepository, GameShopRepository>();
    builder.Services.AddScoped<IAlertRepository, AlertRepository>();

    // ── Сервіси Auth ─────────────────────────────────────────────────────────
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddHttpClient<SteamOpenIdService>();

    // ── Зовнішні HTTP-клієнти ────────────────────────────────────────────────
    builder.Services.AddHttpClient<ISteamClient, SteamClient>();
    builder.Services.AddHttpClient<ISteamSpyClient, SteamSpyClient>();

    // ── Бізнес-сервіси ───────────────────────────────────────────────────────
    builder.Services.AddScoped<PriceManagerService>();
    builder.Services.AddScoped<SteamSpyPriceSyncService>();
    builder.Services.AddSingleton<SteamGameFilter>();

    builder.Services.AddScoped<SteamSpyImportService>();
    builder.Services.AddSingleton<SteamSpyGameMapper>();
    builder.Services.AddScoped<GameEnrichmentService>();
    //
    builder.Services.AddScoped<ICatalogService, CatalogService>();
    builder.Services.AddScoped<IUserCollectionService, UserCollectionService>();
    builder.Services.AddScoped<IGameAlertRepository, GameAlertRepository>();
    builder.Services.AddScoped<IGameAlertService, GameAlertService>();
    builder.Services.AddScoped<IAdminRepository, AdminRepository>();
    builder.Services.AddScoped<IAdminService, AdminService>();
    builder.Services.AddSingleton<PriceSyncState>();
    builder.Services.AddSingleton<GameDB.Web.Services.AdminUserService>();
    builder.Services.AddScoped<IClaimsTransformation, GameDB.Web.Services.AdminClaimsTransformation>();
    builder.Services.AddSingleton<GameDB.Web.Services.GameDescriptionSanitizer>();
    builder.Services.AddScoped<GameDB.Web.Services.AuthCookieService>();
    builder.Services.AddScoped<GameDB.Web.Services.SteamPlayerService>();
    builder.Services.AddHttpClient();
    

    // ── Background Workers ───────────────────────────────────────────────────

    builder.Services.AddSingleton<GameEnrichmentImportState>();
    builder.Services.AddHostedService<GameDB.Infrastructure.Enrichment.GameEnrichmentWorker>();
    builder.Services.AddHostedService<GameDB.Infrastructure.Services.AlertCheckerHostedService>();

    // ── Налаштування ─────────────────────────────────────────────────────────
    builder.Services.Configure<SteamSpyImportOptions>(builder.Configuration.GetSection("SteamSpy"));
    builder.Services.Configure<GameDB.Application.Options.AdminOptions>(
        builder.Configuration.GetSection("Admin"));

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // ════════════════════════════════════════════════════════════════════════
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }

        app.UseSwagger();
        app.UseSwaggerUI(c =>
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "GameDB API V1"));
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();

    app.UseAuthentication(); // ← ПЕРЕД UseAuthorization
    app.UseAuthorization();

    app.UseSerilogRequestLogging();

    app.MapControllers();
    app.MapRazorPages();

    app.Run();
}
catch (HostAbortedException)
{
    // Нормально при dotnet ef database update / migrations — design-time host
    throw;
}
catch (Exception ex)
{
    Log.Fatal(ex, "Сервер впав через критичну помилку!");
}
finally
{
    Log.CloseAndFlush();
}
