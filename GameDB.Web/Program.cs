using Serilog;
using GameDB.Application.Services;
using GameDB.Application.Options;
using GameDB.Application.Interfaces;
using GameDB.Infrastructure.Data;
using GameDB.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;
using GameDB.Infrastructure.Data.Repositories;

// 1. Ініціалізуємо "Bootstrap" логер (щоб зловити помилки, якщо програма навіть не зможе запуститись)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Запуск сервера GameDB...");
    
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

    builder.Services.AddHttpClient<ISteamClient, SteamClient>();
    builder.Services.AddScoped<IGameRepository, GameRepository>();
    
    // 2. Підключаємо повноцінний Serilog і кажемо йому читати appsettings.json
    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services));

    // --- ТВОЇ РЕЄСТРАЦІЇ СЕРВІСІВ ---
    
    // Інфраструктура
    builder.Services.AddHttpClient<ISteamClient, SteamClient>();
    builder.Services.AddScoped<IGameRepository, GameRepository>();
    // Стан та Воркер
    builder.Services.AddSingleton<SteamImportState>();
    builder.Services.AddHostedService<SteamDetailsWorker>();

    // Бізнес-логіка (Чиста Архітектура)
    builder.Services.AddSingleton<SteamGameFilter>();
    builder.Services.AddSingleton<GameMapper>();
    builder.Services.AddScoped<SteamImportService>();

    builder.Services.Configure<SteamImportOptions>(builder.Configuration.GetSection("SteamImport"));
    // ---------------------------------
    builder.Services.AddRazorPages();
    var app = builder.Build();
    app.UseStaticFiles();
    app.UseRouting();
    // 3. Додаємо Middleware для запису HTTP-запитів користувачів (опціонально, але корисно)
    app.UseSerilogRequestLogging(); 

    // --- ТВОЇ ЕНДПОІНТИ ---
    app.MapPost("/api/steam/details/start", (SteamImportState state) =>
    {
        state.IsImportingDetails = true;
        return Results.Ok(new { Message = "Фоновий процес запущено." });
    });

    app.MapPost("/api/steam/details/stop", (SteamImportState state) =>
    {
        state.IsImportingDetails = false;
        return Results.Ok(new { Message = "Фоновий процес МИTTЄВО зупинено." });
    });
    // ----------------------
    app.MapRazorPages();
    app.Run();
}
catch (Exception ex)
{
    // Якщо при старті впаде база даних або щось інше — ми це побачимо
    Log.Fatal(ex, "Сервер впав через критичну помилку!");
}
finally
{
    // Обов'язково скидаємо останні логи у файл перед закриттям програми
    Log.CloseAndFlush();
}