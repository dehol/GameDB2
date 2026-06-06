using GameDB.Application.Interfaces;
using GameDB.Application.Options;
using GameDB.Application.Services;
using GameDB.Application.Services.Import;
using GameDB.Infrastructure.Catalog;
using GameDB.Infrastructure.Data;
using GameDB.Infrastructure.Data.Repositories;
using GameDB.Infrastructure.ExternalProviders;
using GameDB.Infrastructure.Http;
using GameDB.Infrastructure.Steam;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // ЦЕЙ ФІЛЬТР ПРИБЕРЕ SQL-ЗАПИТИ З КОНСОЛІ
    .Filter.ByExcluding(logEvent => 
        logEvent.Properties.ContainsKey("SourceContext") && 
        logEvent.Properties["SourceContext"].ToString().Contains("Microsoft.EntityFrameworkCore.Database.Command"))
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

try
{
    Log.Information("Запуск сервера GameDB...");

    var builder = WebApplication.CreateBuilder(args);

    // ── Razor Pages + Controllers ────────────────────────────────────────────
    builder.Services.AddRazorPages();
    builder.Services.AddMemoryCache();
    builder.Services.AddControllers()
        .AddJsonOptions(o =>
            o.JsonSerializerOptions.Converters.Add(
                new System.Text.Json.Serialization.JsonStringEnumConverter()));
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
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.SlidingExpiration = true;
            options.ExpireTimeSpan    = TimeSpan.FromDays(7);
        });

    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("GuestOrUser", policy =>
            policy.RequireAssertion(ctx =>
                ctx.User.Identity?.IsAuthenticated == true));

        options.AddPolicy("RegisteredOnly", policy =>
            policy.RequireRole("User"));

        options.AddPolicy("AdminOnly", policy =>
            policy.RequireRole(GameDB.Web.Services.AuthCookieService.AdminRole));
    });

    // ── База даних ───────────────────────────────────────────────────────────
    builder.Services.AddDbContext<AppDbContext>(options =>
        options.UseNpgsql("Host=localhost;Port=5432;Database=mygamedb;Username=postgres;Password=postgres"));

    // ── Hangfire ─────────────────────────────────────────────────────────────
    builder.Services.AddHangfire(configuration => configuration
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(c => c.UseNpgsqlConnection("Host=localhost;Port=5432;Database=mygamedb;Username=postgres;Password=postgres"),
        new PostgreSqlStorageOptions
        {
            // Збільшуємо таймаут для тривалих задач
            InvisibilityTimeout = TimeSpan.FromHours(5) 
        }));
    builder.Services.AddHangfireServer();
    builder.Services.AddSingleton<BasicImportOperationState>();
    builder.Services.AddSingleton<EnrichmentOperationState>();
    builder.Services.AddSingleton<PriceSyncOperationState>();

    // ── Репозиторії ──────────────────────────────────────────────────────────
    builder.Services.AddScoped<IGameRepository, GameRepository>();
    builder.Services.AddScoped<IGameOfferRepository, GameOfferRepository>();
    builder.Services.AddScoped<IAlertRepository, AlertRepository>();
    builder.Services.AddScoped<GameDB.Application.Interfaces.IUserRepository, UserRepository>();
    builder.Services.AddScoped<GameDB.Application.Interfaces.ICatalogRepository, CatalogRepository>();
    builder.Services.AddScoped<GameDB.Application.Interfaces.IUserCollectionRepository, UserCollectionRepository>();
    builder.Services.AddScoped<GameDB.Application.Interfaces.IGameShopRepository, GameShopRepository>();

    // ── Сервіси Auth ─────────────────────────────────────────────────────────
    builder.Services.AddScoped<AuthService>();
    builder.Services.AddHttpClient<SteamOpenIdService>();

    // ── Зовнішні HTTP-клієнти ────────────────────────────────────────────────
    builder.Services.AddHttpClient<ISteamClient, SteamClient>();

    builder.Services
        .AddHttpClient<GameDB.Application.Interfaces.ISteamSpyClient, GameDB.Infrastructure.ExternalProviders.SteamSpyClient>()
        .AddStoreProviderResiliency("steamspy");

    builder.Services
        .AddHttpClient<GameDB.Application.Interfaces.IGogClient, GameDB.Infrastructure.ExternalProviders.GogClient>()
        .AddStoreProviderResiliency("gog");

    builder.Services
        .AddHttpClient<GameDB.Application.Interfaces.IEGDataClient, GameDB.Infrastructure.ExternalProviders.EGDataClient>()
        .AddStoreProviderResiliency("egdata");

    // ── Store providers & import ─────────────────────────────────────────────
    builder.Services.Configure<GameDB.Application.Options.StoreImportOptions>(builder.Configuration.GetSection("StoreImport"));
    builder.Services.Configure<GameDB.Application.Options.GogImportOptions>(builder.Configuration.GetSection("Gog"));
    builder.Services.Configure<GameDB.Application.Options.EGDataImportOptions>(builder.Configuration.GetSection("EGData"));

    builder.Services.AddScoped<GameDB.Application.Interfaces.IStoreProvider, GameDB.Infrastructure.Providers.SteamStoreProvider>();
    builder.Services.AddScoped<GameDB.Application.Interfaces.IStoreProvider, GameDB.Infrastructure.Providers.GogStoreProvider>();
    builder.Services.AddScoped<GameDB.Application.Interfaces.IStoreProvider, GameDB.Infrastructure.Providers.EGDataStoreProvider>();

    // ── Бізнес-сервіси ───────────────────────────────────────────────────────
    builder.Services.AddScoped<IPriceManagerService, PriceManagerService>();
    builder.Services.AddSingleton<SteamGameFilter>();
    builder.Services.AddScoped<IBasicImportService, BasicImportService>();
    builder.Services.AddScoped<IGameEnrichmentService, GameEnrichmentService>();
    builder.Services.AddScoped<GameDB.Application.Services.Import.BasicImportService>();
    builder.Services.AddScoped<IPriceSyncService, PriceSyncService>();
    builder.Services.AddScoped<StoreGameMapper>();
    
    builder.Services.AddScoped<ICatalogService, CatalogService>();
    builder.Services.AddScoped<IUserCollectionService, UserCollectionService>();
    builder.Services.AddScoped<INotificationRepository, NotificationRepository>();
    builder.Services.AddScoped<IAdminRepository, AdminRepository>();
    builder.Services.AddScoped<IAdminService, AdminService>();
    builder.Services.AddSingleton<GameDB.Web.Services.AdminUserService>();
    builder.Services.AddScoped<IClaimsTransformation, GameDB.Web.Services.AdminClaimsTransformation>();
    builder.Services.AddSingleton<GameDB.Web.Services.GameDescriptionSanitizer>();
    builder.Services.AddScoped<GameDB.Web.Services.AuthCookieService>();
    builder.Services.AddScoped<GameDB.Web.Services.SteamPlayerService>();
    builder.Services.AddHttpClient();

    builder.Services.Configure<SteamSpyImportOptions>(builder.Configuration.GetSection("SteamSpy"));
    builder.Services.Configure<GameDB.Application.Options.AdminOptions>(builder.Configuration.GetSection("Admin"));

    builder.Services.AddSerilog();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.MigrateAsync();
        }
        app.UseSwagger();
        app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "GameDB API V1"));
    }

    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseSerilogRequestLogging();

    app.MapControllers();
    app.UseHangfireDashboard();
    app.MapRazorPages();

    app.Run();
}
catch (HostAbortedException) { throw; }
catch (Exception ex) { Log.Fatal(ex, "Сервер впав!"); }
finally { Log.CloseAndFlush(); }