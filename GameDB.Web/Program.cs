using Serilog;
using GameDB.Application.Services;
using GameDB.Application.Options;
using GameDB.Application.Interfaces;
using GameDB.Infrastructure.Data;
using GameDB.Infrastructure.Steam;
using Microsoft.EntityFrameworkCore;
using GameDB.Infrastructure.Data.Repositories;
using GameDB.Infrastructure.ExternalProviders;
// 1. Ініціалізуємо "Bootstrap" логер (щоб зловити помилки, якщо програма навіть не зможе запуститись)
Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    Log.Information("Запуск сервера GameDB...");
    
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddRazorPages();
    builder.Services.AddControllers();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));
    builder.Services.AddHttpClient<IItadClient, ItadClient>();
    builder.Services.AddScoped<IGameOfferRepository, GameOfferRepository>();

    builder.Services.AddScoped<PriceManagerService>();
    builder.Services.AddScoped<ItadPriceSyncService>();
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
    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(c =>
        {
            // Робимо так, щоб Swagger відкривався за адресою /swagger
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "GameDB API V1");
        });
    }
    app.UseHttpsRedirection();
    app.UseStaticFiles();
    app.UseRouting();
    app.UseAuthorization();

    app.UseSerilogRequestLogging(); 
    app.MapControllers();
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