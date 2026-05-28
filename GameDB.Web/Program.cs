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
});

    // ── База даних ───────────────────────────────────────────────────────────
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    // ── Репозиторії ──────────────────────────────────────────────────────────
    builder.Services.AddScoped<IUserRepository, UserRepository>();
    builder.Services.AddScoped<IGameOfferRepository, GameOfferRepository>();
    builder.Services.AddScoped<IGameRepository, GameRepository>();
    builder.Services.AddScoped<ILookupRepository, LookupRepository>();

    // ── Сервіси Auth ─────────────────────────────────────────────────────────
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddHttpClient<SteamOpenIdService>();

    // ── Зовнішні HTTP-клієнти ────────────────────────────────────────────────
    builder.Services.AddHttpClient<IItadClient, ItadClient>();
    builder.Services.AddHttpClient<ISteamClient, SteamClient>();
    builder.Services.AddHttpClient<IIgdbClient, IgdbClient>();

    // ── Бізнес-сервіси ───────────────────────────────────────────────────────
    builder.Services.AddScoped<PriceManagerService>();
    builder.Services.AddScoped<ItadPriceSyncService>();
    builder.Services.AddSingleton<SteamGameFilter>();
    builder.Services.AddSingleton<GameMapper>();
    builder.Services.AddScoped<SteamImportService>();
    builder.Services.AddSingleton<IgdbGameMapper>();
    builder.Services.AddScoped<FastIgdbImportService>();

    // ── Background Workers ───────────────────────────────────────────────────
    builder.Services.AddSingleton<SteamImportState>();
    builder.Services.AddHostedService<SteamDetailsWorker>();
    builder.Services.AddSingleton<IgdbImportState>();
    builder.Services.AddSingleton<GameDB.Infrastructure.Igdb.IgdbRateLimiter>();
    builder.Services.AddHostedService<GameDB.Infrastructure.Igdb.IgdbDetailsWorker>();

    // ── Налаштування ─────────────────────────────────────────────────────────
    builder.Services.Configure<SteamImportOptions>(builder.Configuration.GetSection("SteamImport"));

    // ── Serilog ──────────────────────────────────────────────────────────────
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // ════════════════════════════════════════════════════════════════════════
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
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

    // Steam import API endpoints
    app.MapPost("/api/steam/details/start", (SteamImportState state) =>
    {
        state.IsImportingDetails = true;
        return Results.Ok(new { Message = "Фоновий процес запущено." });
    }).RequireAuthorization();

    app.MapPost("/api/steam/details/stop", (SteamImportState state) =>
    {
        state.IsImportingDetails = false;
        return Results.Ok(new { Message = "Фоновий процес зупинено." });
    }).RequireAuthorization();

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Сервер впав через критичну помилку!");
}
finally
{
    Log.CloseAndFlush();
}
