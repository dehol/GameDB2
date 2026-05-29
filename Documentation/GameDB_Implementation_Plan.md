# GameDB — Покроковий план реалізації

> Кожен етап — це робочий стан який можна запустити і перевірити.  
> Не переходь до наступного етапу поки поточний не працює.

---

## ЕТАП 0 — Підготовка середовища
**Час: 2–4 години**

### 0.1 Встанови інструменти

- [ ] [.NET 9 SDK](https://dotnet.microsoft.com/download)
- [ ] [Visual Studio 2022 Community](https://visualstudio.microsoft.com/) або [Rider](https://www.jetbrains.com/rider/)
- [ ] [PostgreSQL 16](https://www.postgresql.org/download/) + [pgAdmin](https://www.pgadmin.org/)
- [ ] [Git](https://git-scm.com/) + створи репозиторій на GitHub
- [ ] [Postman](https://www.postman.com/) — для тестування API

### 0.2 Перевір що все встановлено

```bash
dotnet --version        # має бути 9.x.x
git --version
psql --version
```

### 0.3 Отримай API ключі

- [ ] **Steam Web API Key** — https://steamcommunity.com/dev/apikey (безкоштовно, треба Steam акаунт)
- [ ] **ITAD API Key** — https://isthereanydeal.com/dev/app/ (безкоштовно, реєстрація)

### 0.4 Створи базу даних

Відкрий pgAdmin → створи базу `gamedb`.  
Запусти SQL з файлу schema (твій дамп) щоб створити всі таблиці.

**Результат етапу:** Все встановлено, база існує, є API ключі.

---

## ЕТАП 1 — Структура проєкту
**Час: 2–3 години**

### 1.1 Створи Solution

```bash
mkdir GameDB && cd GameDB
dotnet new sln -n GameDB

dotnet new webapp    -n GameDB.Web            --no-https false
dotnet new classlib  -n GameDB.Domain
dotnet new classlib  -n GameDB.Application
dotnet new classlib  -n GameDB.Infrastructure

dotnet sln add GameDB.Web/GameDB.Web.csproj
dotnet sln add GameDB.Domain/GameDB.Domain.csproj
dotnet sln add GameDB.Application/GameDB.Application.csproj
dotnet sln add GameDB.Infrastructure/GameDB.Infrastructure.csproj
```

### 1.2 Налаштуй залежності між проєктами

```bash
# Application знає про Domain
dotnet add GameDB.Application reference GameDB.Domain

# Infrastructure знає про Domain і Application
dotnet add GameDB.Infrastructure reference GameDB.Domain
dotnet add GameDB.Infrastructure reference GameDB.Application

# Web знає про всіх
dotnet add GameDB.Web reference GameDB.Application
dotnet add GameDB.Web reference GameDB.Infrastructure
```

### 1.3 Встанови NuGet пакети

```bash
# Infrastructure — EF Core + PostgreSQL
cd GameDB.Infrastructure
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add package Microsoft.EntityFrameworkCore.Design

# Web — потрібен для EF CLI команд
cd ../GameDB.Web
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL

# EF Core CLI глобально
dotnet tool install --global dotnet-ef
```

### 1.4 Створи базову структуру папок

```
GameDB.Domain/
  Entities/        ← порожня папка поки що
  Enums/

GameDB.Application/
  Services/
  DTOs/
  Interfaces/

GameDB.Infrastructure/
  Data/
  ExternalProviders/

GameDB.Web/
  Pages/
  ViewModels/
```

### 1.5 Перший запуск

```bash
cd GameDB.Web
dotnet run
```

Має відкритись стандартна сторінка ASP.NET — це ок.

### 1.6 Закомміть

```bash
git init
git add .
git commit -m "Initial project structure"
git remote add origin https://github.com/твій/репо.git
git push -u origin main
```

**Результат етапу:** Проєкт компілюється, запускається, є на GitHub.

---

## ЕТАП 2 — Domain моделі + DbContext
**Час: 3–4 години**

### 2.1 Створи Entity класи в GameDB.Domain/Entities/

Кожна сутність = окремий файл. Починай з основних:

**Game.cs**
```csharp
namespace GameDB.Domain.Entities;

public class Game
{
    public int GameId { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public DateOnly? ReleaseDate { get; set; }
    public int? DeveloperId { get; set; }
    public int? PublisherId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public double? Rating { get; set; }
    public int? RatingCount { get; set; }
    public string ContentType { get; set; } = "main_game";
    public string? HeaderImage { get; set; }
    public int? SteamAppId { get; set; }
    public string? Slug { get; set; }

    // Navigation properties
    public Developer? Developer { get; set; }
    public Publisher? Publisher { get; set; }
    public ICollection<GameGenre> GameGenres { get; set; } = [];
    public ICollection<GameOffer> GameOffers { get; set; } = [];
    public ICollection<Wishlist> Wishlists { get; set; } = [];
    public ICollection<UserLibrary> UserLibraries { get; set; } = [];
    public ICollection<Alert> Alerts { get; set; } = [];
}
```

Аналогічно створи: `Genre.cs`, `GameGenre.cs`, `GameOffer.cs`, `GameShop.cs`,
`Developer.cs`, `Publisher.cs`, `User.cs`, `Wishlist.cs`, `UserLibrary.cs`,
`Alert.cs`, `Notification.cs`, `PriceHistory.cs`, `UserShopProfile.cs`

> Орієнтуйся на колонки з твого SQL дампу — кожна колонка = property у класі.

### 2.2 Створи AppDbContext

```csharp
// GameDB.Infrastructure/Data/AppDbContext.cs
using GameDB.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Infrastructure.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Game> Games => Set<Game>();
    public DbSet<Genre> Genres => Set<Genre>();
    public DbSet<GameGenre> GameGenres => Set<GameGenre>();
    public DbSet<GameOffer> GameOffers => Set<GameOffer>();
    public DbSet<GameShop> GameShops => Set<GameShop>();
    public DbSet<Developer> Developers => Set<Developer>();
    public DbSet<Publisher> Publishers => Set<Publisher>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Wishlist> Wishlists => Set<Wishlist>();
    public DbSet<UserLibrary> UserLibraries => Set<UserLibrary>();
    public DbSet<Alert> Alerts => Set<Alert>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PriceHistory> PriceHistories => Set<PriceHistory>();
    public DbSet<UserShopProfile> UserShopProfiles => Set<UserShopProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Таблиці мають лапки у PostgreSQL
        modelBuilder.Entity<Game>().ToTable("Game");
        modelBuilder.Entity<Genre>().ToTable("Genre");
        // ... і так для кожної таблиці

        // Composite Primary Keys
        modelBuilder.Entity<GameGenre>().HasKey(x => new { x.GameId, x.GenreId });
        modelBuilder.Entity<Wishlist>().HasKey(x => new { x.UserId, x.GameId });
        modelBuilder.Entity<UserLibrary>().HasKey(x => new { x.UserId, x.GameId, x.ShopId });
    }
}
```

### 2.3 Підключи DbContext у Program.cs

```csharp
// GameDB.Web/Program.cs
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .LogTo(Console.WriteLine, LogLevel.Information));  // SQL у консоль

var app = builder.Build();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.Run();
```

### 2.4 Connection string

```json
// GameDB.Web/appsettings.Development.json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Database=gamedb;Username=postgres;Password=твій_пароль"
  }
}
```

### 2.5 Перевір підключення до БД

Запусти додаток — якщо немає помилки підключення, все ок.  
Якщо є помилка — перевір connection string і що PostgreSQL запущений.

**Результат етапу:** Проєкт підключений до БД, Entity класи готові.

---

## ЕТАП 3 — Перша сторінка каталогу (без фільтрів)
**Час: 3–4 години**

Мета: відкрити браузер і побачити список ігор з БД.

### 3.1 Додай seed дані

Виконай в pgAdmin SQL з файлу seed (що ми писали раніше).  
Переконайся що в таблиці `Game` є 5 ігор.

### 3.2 Створи DTO

```csharp
// GameDB.Application/DTOs/GameSummaryDto.cs
namespace GameDB.Application.DTOs;

public record GameSummaryDto(
    int GameId,
    string Name,
    string? Slug,
    string? HeaderImage,
    DateOnly? ReleaseDate,
    decimal CurrentPrice,
    int CurrentDiscount
);
```

### 3.3 Створи CatalogService

```csharp
// GameDB.Application/Services/CatalogService.cs
using GameDB.Application.DTOs;
using GameDB.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace GameDB.Application.Services;

public class CatalogService
{
    private readonly AppDbContext _db;

    public CatalogService(AppDbContext db) => _db = db;

    public async Task<(List<GameSummaryDto> Items, int TotalCount)> GetCatalogAsync(
        int page = 1, int pageSize = 50)
    {
        var query = _db.Games
            .Where(g => g.ContentType == "main_game")
            .AsQueryable();

        var totalCount = await query.CountAsync();

        var items = await query
            .Include(g => g.GameOffers)
            .OrderBy(g => g.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(g => new GameSummaryDto(
                g.GameId,
                g.Name,
                g.Slug,
                g.HeaderImage,
                g.ReleaseDate,
                g.GameOffers.FirstOrDefault(o => o.Currency == "USD") != null
                    ? g.GameOffers.First(o => o.Currency == "USD").CurrentPrice
                    : 0m,
                g.GameOffers.FirstOrDefault(o => o.Currency == "USD") != null
                    ? g.GameOffers.First(o => o.Currency == "USD").CurrentDiscount
                    : 0
            ))
            .ToListAsync();

        return (items, totalCount);
    }
}
```

### 3.4 Зареєструй сервіс у Program.cs

```csharp
builder.Services.AddScoped<CatalogService>();
```

### 3.5 Створи Razor Page

```csharp
// GameDB.Web/Pages/Index.cshtml.cs
using GameDB.Application.DTOs;
using GameDB.Application.Services;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace GameDB.Web.Pages;

public class IndexModel : PageModel
{
    private readonly CatalogService _catalog;

    public List<GameSummaryDto> Games { get; set; } = [];
    public int TotalCount { get; set; }

    public IndexModel(CatalogService catalog) => _catalog = catalog;

    public async Task OnGetAsync(int page = 1)
    {
        (Games, TotalCount) = await _catalog.GetCatalogAsync(page);
    }
}
```

```html
<!-- GameDB.Web/Pages/Index.cshtml -->
@page
@model IndexModel
@{
    ViewData["Title"] = "GameDB — Catalog";
}

<h1>Games (@Model.TotalCount total)</h1>

<div style="display:grid; grid-template-columns: repeat(auto-fill, minmax(200px,1fr)); gap:16px">
    @foreach (var game in Model.Games)
    {
        <div style="border:1px solid #ccc; padding:8px; border-radius:4px">
            @if (game.HeaderImage != null)
            {
                <img src="@game.HeaderImage" style="width:100%" alt="@game.Name" />
            }
            <h3 style="font-size:14px">@game.Name</h3>
            <p>
                @if (game.CurrentPrice == 0)
                {
                    <strong>FREE</strong>
                }
                else
                {
                    <span>$@game.CurrentPrice</span>
                    @if (game.CurrentDiscount > 0)
                    {
                        <span style="color:green"> -@game.CurrentDiscount%</span>
                    }
                }
            </p>
        </div>
    }
</div>
```

### 3.6 Запусти і перевір

```bash
dotnet run --project GameDB.Web
```

Відкрий `https://localhost:5001` — маєш побачити 5 ігор з картинками.

**Результат етапу:** Працюючий каталог з даними з БД.

---

## ЕТАП 4 — Авторизація (реєстрація + логін)
**Час: 4–6 годин**

### 4.1 Встанови пакети для Identity

```bash
cd GameDB.Web
dotnet add package Microsoft.AspNetCore.Identity.EntityFrameworkCore
```

### 4.2 Вибір підходу

У тебе вже є кастомна таблиця `User`. Є два варіанти:

**Варіант A (простіший для початківця)** — використати ASP.NET Identity повністю.  
Це означає що Identity сам створить таблиці `AspNetUsers`, `AspNetRoles` etc.  
Твою кастомну таблицю `User` використовуєш тільки для профілю (SteamId, тощо).

**Варіант B** — кастомна авторизація через cookies без Identity.  
Більше роботи, але повний контроль.

> Рекомендую **Варіант A** для першого проєкту.

### 4.3 Варіант A — ASP.NET Identity

Створи клас ApplicationUser:
```csharp
// GameDB.Infrastructure/Data/ApplicationUser.cs
using Microsoft.AspNetCore.Identity;

namespace GameDB.Infrastructure.Data;

public class ApplicationUser : IdentityUser<int>
{
    public string? SteamId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastLogin { get; set; }
}
```

Онови DbContext:
```csharp
// Змінити базовий клас
public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>
```

У Program.cs:
```csharp
builder.Services.AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
{
    options.Password.RequiredLength = 8;
    options.Password.RequireNonAlphanumeric = false;
    options.User.RequireUniqueEmail = true;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Auth/Login";
    options.LogoutPath = "/Auth/Logout";
});
```

### 4.4 Створи сторінки реєстрації та логіну

Мінімальний варіант — дві Razor Pages:

`Pages/Auth/Register.cshtml` — форма: Username, Email, Password  
`Pages/Auth/Login.cshtml` — форма: Email, Password

Логіка у `RegisterModel.OnPostAsync()`:
```csharp
var user = new ApplicationUser { UserName = Input.Username, Email = Input.Email };
var result = await _userManager.CreateAsync(user, Input.Password);
if (result.Succeeded)
{
    await _signInManager.SignInAsync(user, isPersistent: false);
    return RedirectToPage("/Index");
}
```

### 4.5 Перевір

- Реєстрація нового юзера
- Логін
- Logout
- Після логіну User.Identity.IsAuthenticated == true

**Результат етапу:** Працює реєстрація, логін, логаут.

---

## ЕТАП 5 — Steam OpenID логін
**Час: 3–4 години**

### 5.1 Встанови пакет

```bash
cd GameDB.Web
dotnet add package AspNet.Security.OpenId.Steam
```

### 5.2 Налаштуй у Program.cs

```csharp
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
})
.AddSteam(options =>
{
    options.ApplicationKey = builder.Configuration["Steam:ApiKey"]!;
    options.CallbackPath = "/auth/steam/callback";
});
```

### 5.3 Додай у appsettings.Development.json

```json
{
  "Steam": {
    "ApiKey": "твій_steam_api_key"
  }
}
```

### 5.4 Створи контролер для Steam callback

```csharp
// GameDB.Web/Controllers/AuthController.cs
[Route("auth")]
public class AuthController : Controller
{
    [HttpGet("steam")]
    public IActionResult SteamLogin()
    {
        return Challenge(new AuthenticationProperties
        {
            RedirectUri = "/auth/steam/callback"
        }, SteamAuthenticationDefaults.AuthenticationScheme);
    }

    [HttpGet("steam/callback")]
    public async Task<IActionResult> SteamCallback(
        [FromServices] UserManager<ApplicationUser> userManager,
        [FromServices] SignInManager<ApplicationUser> signInManager)
    {
        var info = await signInManager.GetExternalLoginInfoAsync();
        // info.Principal містить SteamId
        var steamId = info!.Principal.FindFirstValue(ClaimTypes.NameIdentifier)!
            .Split('/').Last(); // витягнути SteamID64

        // Якщо юзер вже залогінений — прив'язати Steam
        // Якщо ні — знайти/створити юзера по SteamId
        
        return RedirectToPage("/Index");
    }
}
```

### 5.5 Перевір

Клікнути "Login with Steam" → редирект на Steam → повернення назад → SteamId збережено у User.

**Результат етапу:** Можна логінитись через Steam.

---

## ЕТАП 6 — Steam імпорт (Phase 1 — App List)
**Час: 4–5 годин**

Мета: завантажити базовий список appid + назва через SteamSpy і зберегти в БД.

### 6.1 Створи SteamSpyClient (App List через SteamSpy)

```csharp
// GameDB.Infrastructure/ExternalProviders/SteamSpyClient.cs
using System.Text.Json;

namespace GameDB.Infrastructure.ExternalProviders;

public class SteamSpyClient
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;

    public SteamSpyClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _baseUrl = config["SteamSpy:BaseUrl"] ?? "https://steamspy.com/api.php";
    }

    // Отримати базовий список appid + назва
    public async Task<IReadOnlyCollection<SteamSpyAppListItemDto>> GetAppListAsync(CancellationToken ct = default)
    {
        var url = $"{_baseUrl.TrimEnd('/')}?request=all";
        var response = await _http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode) return [];

        var json = await response.Content.ReadAsStringAsync(ct);
        var data = JsonSerializer.Deserialize<Dictionary<string, SteamSpyAppListItemDto>>(json);
        return data?.Values
            .Where(item => item is not null && item.AppId > 0 && !string.IsNullOrWhiteSpace(item.Name))
            .ToList() ?? [];
    }
}
```

### 6.2 Зареєструй HttpClient

```csharp
// Program.cs
builder.Services.AddHttpClient<ISteamSpyClient, SteamSpyClient>();
```

### 6.3 Створи ImportService (Phase 1)

```csharp
// GameDB.Application/Services/ImportService.cs
public class ImportService
{
    private readonly AppDbContext _db;
    private readonly ISteamSpyClient _steamSpy;

    public ImportService(AppDbContext db, ISteamSpyClient steamSpy)
    {
        _db = db;
        _steamSpy = steamSpy;
    }

    // Phase 1: Швидкий seed — тільки назви і AppId
    public async Task SeedAppListAsync(CancellationToken ct = default)
    {
        var apps = await _steamSpy.GetAppListAsync(ct);

        // Отримай існуючі AppId щоб не дублювати
        var existingIds = await _db.Games
            .Select(g => g.SteamAppId)
            .ToHashSetAsync(ct);

        var newGames = apps
            .Where(a => !existingIds.Contains(a.AppId))
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new Game
            {
                Name = a.Name,
                SteamAppId = a.AppId,
                Slug = GenerateSlug(a.Name, a.AppId),
                ContentType = "main_game",  // уточниться при Phase 2
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            })
            .ToList();

        // Batch insert по 500 записів
        foreach (var batch in newGames.Chunk(500))
        {
            await _db.Games.AddRangeAsync(batch, ct);
            await _db.SaveChangesAsync(ct);
        }
    }

    private static string GenerateSlug(string name, int appId)
    {
        var slug = name.ToLower()
            .Replace(" ", "-")
            .Replace("'", "")
            .Replace(":", "")
            .Replace(".", "");
        // Додаємо appId щоб уникнути дублікатів
        return $"{slug}-{appId}";
    }
}
```

### 6.4 Перевір через тимчасовий endpoint

```csharp
// Тимчасово у Program.cs для тестування
app.MapGet("/test-import", async (ImportService import) =>
{
    await import.SeedAppListAsync();
    return "Done";
});
```

Відкрий `/test-import` у браузері — через ~30 сек у таблиці `Game` буде ~100k записів.

**Результат етапу:** БД містить весь каталог Steam (назви + AppId).

---

## ЕТАП 7 — Steam імпорт (Phase 2 — Metadata)
**Час: 4–5 годин**

Мета: заповнити деталі ігор (опис, жанри, тощо) через Steam Store API.

### 7.1 Додай моделі для відповіді Steam API

```csharp
// GameDB.Infrastructure/ExternalProviders/Steam/SteamAppDetails.cs
public class SteamAppDetails
{
    public string type { get; set; } = "";
    public string name { get; set; } = "";
    public string short_description { get; set; } = "";
    public string? header_image { get; set; }
    public bool is_free { get; set; }
    public List<SteamGenre>? genres { get; set; }
    public SteamReleaseDate? release_date { get; set; }
    public SteamMetacritic? metacritic { get; set; }
}

public record SteamGenre(string id, string description);
public record SteamReleaseDate(bool coming_soon, string date);
public record SteamMetacritic(int score, string url);
```

### 7.2 Додай метод RefreshGameMetaAsync у ImportService

```csharp
public async Task RefreshGameMetaAsync(int steamAppId, CancellationToken ct = default)
{
    var details = await _steam.GetAppDetailsAsync(steamAppId, ct);
    if (details == null) return;

    var game = await _db.Games
        .FirstOrDefaultAsync(g => g.SteamAppId == steamAppId, ct);
    if (game == null) return;

    // Оновлення
    game.Name = details.name;
    game.Description = details.short_description;
    game.HeaderImage = details.header_image;
    game.ContentType = details.type; // 'game', 'dlc', etc.
    game.UpdatedAt = DateTime.UtcNow;

    // Дата релізу
    if (details.release_date != null && !details.release_date.coming_soon)
    {
        if (DateOnly.TryParse(details.release_date.date, out var date))
            game.ReleaseDate = date;
    }

    // Рейтинг Metacritic
    if (details.metacritic != null)
        game.Rating = details.metacritic.score / 100.0;

    // Жанри
    if (details.genres != null)
    {
        foreach (var genreData in details.genres)
        {
            var genre = await _db.Genres
                .FirstOrDefaultAsync(g => g.Name == genreData.description, ct)
                ?? _db.Genres.Add(new Genre { Name = genreData.description }).Entity;

            var exists = await _db.GameGenres.AnyAsync(
                gg => gg.GameId == game.GameId && gg.GenreId == genre.GenreId, ct);
            if (!exists)
                _db.GameGenres.Add(new GameGenre { GameId = game.GameId, GenreId = genre.GenreId });
        }
    }

    await _db.SaveChangesAsync(ct);
}
```

### 7.3 Створи BackgroundService для поступового заповнення

```csharp
// GameDB.Infrastructure/BackgroundServices/MetadataFillBackgroundService.cs
public class MetadataFillBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<MetadataFillBackgroundService> _logger;

    public MetadataFillBackgroundService(IServiceProvider services,
        ILogger<MetadataFillBackgroundService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Чекаємо 10 сек після старту щоб додаток встиг запуститись
        await Task.Delay(10_000, ct);

        while (!ct.IsCancellationRequested)
        {
            using var scope = _services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var import = scope.ServiceProvider.GetRequiredService<ImportService>();

            // Беремо ігри без опису (пріоритет — ті де є Rating)
            var games = await db.Games
                .Where(g => g.Description == null)
                .Where(g => g.SteamAppId != null)
                .OrderByDescending(g => g.Rating)
                .Take(10) // 10 за раз
                .Select(g => g.SteamAppId!.Value)
                .ToListAsync(ct);

            if (!games.Any())
            {
                // Всі ігри оброблені — чекаємо 1 годину
                await Task.Delay(TimeSpan.FromHours(1), ct);
                continue;
            }

            foreach (var appId in games)
            {
                try
                {
                    await import.RefreshGameMetaAsync(appId, ct);
                    _logger.LogInformation("Refreshed AppId {AppId}", appId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to refresh AppId {AppId}", appId);
                }

                // Пауза між запитами — Steam rate limit
                await Task.Delay(1500, ct);
            }
        }
    }
}
```

### 7.4 Зареєструй у Program.cs

```csharp
builder.Services.AddScoped<ImportService>();
builder.Services.AddHostedService<MetadataFillBackgroundService>();
```

**Результат етапу:** Ігри поступово заповнюються деталями у фоні.

---

## ЕТАП 8 — Фільтри каталогу
**Час: 4–5 годин**

### 8.1 Додай FilterDto

```csharp
// GameDB.Application/DTOs/CatalogFilterDto.cs
public class CatalogFilterDto
{
    public string? Search { get; set; }
    public List<int>? GenreIds { get; set; }
    public decimal? MinPrice { get; set; }
    public decimal? MaxPrice { get; set; }
    public int? MinDiscount { get; set; }
    public bool? IsFree { get; set; }
    public string? SortBy { get; set; } = "name";
    public string? SortDir { get; set; } = "asc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
```

### 8.2 Оновлення CatalogService

```csharp
public async Task<(List<GameSummaryDto> Items, int TotalCount)> GetCatalogAsync(
    CatalogFilterDto filter)
{
    var query = _db.Games
        .Where(g => g.ContentType == "main_game")
        .AsQueryable();

    // Пошук по назві
    if (!string.IsNullOrWhiteSpace(filter.Search))
        query = query.Where(g => g.Name.ToLower().Contains(filter.Search.ToLower()));

    // Фільтр по жанру
    if (filter.GenreIds?.Any() == true)
        query = query.Where(g =>
            g.GameGenres.Any(gg => filter.GenreIds.Contains(gg.GenreId)));

    // Фільтр безкоштовних
    if (filter.IsFree == true)
        query = query.Where(g =>
            g.GameOffers.Any(o => o.CurrentPrice == 0));

    // Фільтр по ціні
    if (filter.MinPrice.HasValue)
        query = query.Where(g =>
            g.GameOffers.Any(o => o.CurrentPrice >= filter.MinPrice));
    if (filter.MaxPrice.HasValue)
        query = query.Where(g =>
            g.GameOffers.Any(o => o.CurrentPrice <= filter.MaxPrice));

    // Фільтр по знижці
    if (filter.MinDiscount.HasValue)
        query = query.Where(g =>
            g.GameOffers.Any(o => o.CurrentDiscount >= filter.MinDiscount));

    // Сортування
    query = filter.SortBy switch
    {
        "price"    => filter.SortDir == "desc"
                        ? query.OrderByDescending(g => g.GameOffers.Min(o => o.CurrentPrice))
                        : query.OrderBy(g => g.GameOffers.Min(o => o.CurrentPrice)),
        "discount" => query.OrderByDescending(g => g.GameOffers.Max(o => o.CurrentDiscount)),
        "date"     => query.OrderByDescending(g => g.ReleaseDate),
        _          => query.OrderBy(g => g.Name)
    };

    var totalCount = await query.CountAsync();

    var items = await query
        .Include(g => g.GameOffers)
        .Skip((filter.Page - 1) * filter.PageSize)
        .Take(filter.PageSize)
        .Select(g => new GameSummaryDto(
            g.GameId, g.Name, g.Slug, g.HeaderImage, g.ReleaseDate,
            g.GameOffers.FirstOrDefault(o => o.Currency == "USD") != null
                ? g.GameOffers.First(o => o.Currency == "USD").CurrentPrice : 0m,
            g.GameOffers.FirstOrDefault(o => o.Currency == "USD") != null
                ? g.GameOffers.First(o => o.Currency == "USD").CurrentDiscount : 0
        ))
        .ToListAsync();

    return (items, totalCount);
}
```

### 8.3 Форма фільтрів у Index.cshtml

Додай форму над списком ігор:
```html
<form method="get">
    <input type="text" name="Search" value="@Request.Query["Search"]" placeholder="Пошук..." />
    <select name="GenreIds" multiple>
        @foreach (var genre in Model.Genres)
        {
            <option value="@genre.GenreId">@genre.Name</option>
        }
    </select>
    <input type="number" name="MinDiscount" placeholder="Мін. знижка %" min="0" max="100" />
    <label>
        <input type="checkbox" name="IsFree" value="true" /> Тільки безкоштовні
    </label>
    <button type="submit">Знайти</button>
</form>
```

**Результат етапу:** Каталог фільтрується і сортується.

---

## ЕТАП 9 — Сторінка гри
**Час: 2–3 години**

### 9.1 Метод у CatalogService

```csharp
public async Task<GameDetailDto?> GetGameBySlugAsync(string slug)
{
    return await _db.Games
        .Include(g => g.GameGenres).ThenInclude(gg => gg.Genre)
        .Include(g => g.GameOffers).ThenInclude(o => o.Shop)
        .Include(g => g.Developer)
        .Include(g => g.Publisher)
        .Where(g => g.Slug == slug)
        .Select(g => new GameDetailDto { /* маппінг */ })
        .FirstOrDefaultAsync();
}
```

### 9.2 Razor Page

`Pages/Game/Detail.cshtml` — роут `/game/{slug}`

```csharp
@page "/game/{slug}"
@model DetailModel
```

Показуй: обкладинку, назву, опис, жанри, ціни по магазинах, дату релізу.

**Результат етапу:** Клік на гру → сторінка з деталями.

---

## ЕТАП 10 — Wishlist
**Час: 2–3 години**

### 10.1 WishlistService

```csharp
public class WishlistService
{
    public async Task AddAsync(int userId, int gameId) { ... }
    public async Task RemoveAsync(int userId, int gameId) { ... }
    public async Task<List<GameSummaryDto>> GetByUserAsync(int userId) { ... }
    public async Task<bool> IsInWishlistAsync(int userId, int gameId) { ... }
}
```

### 10.2 Кнопка на сторінці гри

```html
@if (User.Identity!.IsAuthenticated)
{
    @if (Model.IsInWishlist)
    {
        <form method="post" asp-page-handler="RemoveFromWishlist">
            <button type="submit">❤️ Видалити з вішліста</button>
        </form>
    }
    else
    {
        <form method="post" asp-page-handler="AddToWishlist">
            <button type="submit">🤍 Додати у вішліст</button>
        </form>
    }
}
```

### 10.3 Сторінка вішліста

`Pages/Wishlist/Index.cshtml` — список ігор з можливістю видалення.

**Результат етапу:** Юзер може додавати/видаляти ігри у вішліст.

---

## ЕТАП 11 — Імпорт бібліотеки Steam
**Час: 3–4 години**

### 11.1 Додай метод у SteamApiClient

```csharp
public async Task<List<SteamOwnedGame>> GetOwnedGamesAsync(string steamId64)
{
    var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v1/" +
              $"?key={_apiKey}&steamid={steamId64}&include_appinfo=1&include_played_free_games=1";
    var json = await _http.GetStringAsync(url);
    // парсинг...
}
```

### 11.2 LibraryService

```csharp
public async Task ImportSteamLibraryAsync(int userId, string steamId64)
{
    var ownedGames = await _steam.GetOwnedGamesAsync(steamId64);
    var steamShop = await _db.GameShops.FirstAsync(s => s.Name == "Steam");

    foreach (var owned in ownedGames)
    {
        // Знайти або створити Game по SteamAppId
        var game = await _db.Games
            .FirstOrDefaultAsync(g => g.SteamAppId == owned.appid);

        if (game == null) continue; // Гра не в каталозі — пропускаємо

        // Додати в UserLibrary якщо ще немає
        var exists = await _db.UserLibraries.AnyAsync(ul =>
            ul.UserId == userId && ul.GameId == game.GameId);

        if (!exists)
        {
            _db.UserLibraries.Add(new UserLibrary
            {
                UserId = userId,
                GameId = game.GameId,
                ShopId = steamShop.ShopId,
                AddedAt = DateTime.UtcNow
            });
        }
    }

    await _db.SaveChangesAsync();
}
```

**Результат етапу:** Кнопка "Імпортувати бібліотеку Steam" — і всі куплені ігри у профілі.

---

## ЕТАП 12 — Alerts (цінові алерти)
**Час: 3–4 години**

### 12.1 AlertService

```csharp
public class AlertService
{
    public async Task CreateAlertAsync(int userId, int gameId,
        decimal? targetPrice, int? targetDiscount) { ... }

    public async Task<List<Alert>> GetUserAlertsAsync(int userId) { ... }

    public async Task DeleteAlertAsync(int alertId, int userId) { ... }

    // Викликається BackgroundService
    public async Task CheckAndFireAlertsAsync()
    {
        var activeAlerts = await _db.Alerts
            .Where(a => a.TriggeredAt == null)
            .Include(a => a.Game)
                .ThenInclude(g => g.GameOffers)
            .ToListAsync();

        foreach (var alert in activeAlerts)
        {
            var offer = alert.Game.GameOffers.FirstOrDefault(o => o.Currency == "USD");
            if (offer == null) continue;

            var triggered =
                (alert.TargetPrice.HasValue && offer.CurrentPrice <= alert.TargetPrice) ||
                (alert.TargetDiscount.HasValue && offer.CurrentDiscount >= alert.TargetDiscount);

            if (triggered)
            {
                alert.TriggeredAt = DateTime.UtcNow;
                _db.Notifications.Add(new Notification
                {
                    UserId = alert.UserId,
                    Type = "PriceAlert",
                    Description = $"Ціна на {alert.Game.Name} знизилась до ${offer.CurrentPrice}!",
                    CreatedAt = DateTime.UtcNow
                });
            }
        }

        await _db.SaveChangesAsync();
    }
}
```

### 12.2 AlertCheckerBackgroundService

```csharp
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        await Task.Delay(TimeSpan.FromMinutes(15), ct);
        // ... викликати AlertService.CheckAndFireAlertsAsync()
    }
}
```

**Результат етапу:** Алерти створюються, спрацьовують і показуються у нотифікаціях.

---

## ЕТАП 13 — ITAD ціни
**Час: 3–4 години**

### 13.1 ItadApiClient

```csharp
// GameDB.Infrastructure/ExternalProviders/ITAD/ItadApiClient.cs
public class ItadApiClient
{
    // 1. Резолюція Steam AppId → ITAD UUID
    public async Task<string?> LookupGameIdAsync(int steamAppId) { ... }
    // GET https://api.isthereanydeal.com/games/lookup/v1?key=...&appid=app/steamAppId

    // 2. Отримати поточні ціни
    public async Task<ItadPriceData?> GetPricesAsync(string itadId) { ... }
    // POST https://api.isthereanydeal.com/games/prices/v3?key=...
}
```

### 13.2 PriceRefreshBackgroundService

```csharp
// Раз на ніч оновлює ціни для всіх ігор з GameOffer
protected override async Task ExecuteAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        // Чекаємо до наступної ночі 3:00
        var now = DateTime.Now;
        var next3am = now.Date.AddDays(now.Hour >= 3 ? 1 : 0).AddHours(3);
        await Task.Delay(next3am - now, ct);

        // Оновлюємо ціни батчами
        // ...
    }
}
```

**Результат етапу:** Ціни оновлюються щоночі з ITAD.

---

## ЕТАП 14 — Admin Panel
**Час: 4–5 годин**

### 14.1 Захисти роутами

```csharp
// Pages/Admin/Index.cshtml.cs
[Authorize(Roles = "Admin")]
public class AdminIndexModel : PageModel { ... }
```

Або через policy у Program.cs:
```csharp
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", p => p.RequireRole("Admin"));
});
```

### 14.2 Сторінки адмін-панелі

`/Admin/Index` — дашборд: кількість ігор, останній sync, кількість алертів

`/Admin/Games` — таблиця всіх ігор з пошуком і кнопками Refresh/Edit/Delete

`/Admin/Logs` — таблиця ImportLog з статусами

`/Admin/Incomplete` — ігри де `Description == null`

### 14.3 Тригер імпорту

Кнопка "Run Full Import" → POST → запускає ImportService у фоні:

```csharp
public async Task<IActionResult> OnPostRunImportAsync()
{
    // Запуск у фоні — не чекаємо результату
    _ = Task.Run(() => _import.SeedAppListAsync());
    TempData["Message"] = "Import started!";
    return RedirectToPage();
}
```

**Результат етапу:** Повноцінна адмін-панель.

---

## ФІНАЛЬНИЙ ЧЕКЛИСТ

```
✅ ЕТАП 0  — Середовище, API ключі, БД
✅ ЕТАП 1  — Solution структура, NuGet пакети
✅ ЕТАП 2  — Entity класи, AppDbContext, підключення до БД
✅ ЕТАП 3  — Каталог зі seed даними
✅ ЕТАП 4  — Реєстрація та логін
✅ ЕТАП 5  — Steam OpenID логін
✅ ЕТАП 6  — Steam import Phase 1 (app list)
✅ ЕТАП 7  — Steam import Phase 2 (metadata, background)
✅ ЕТАП 8  — Фільтри каталогу
✅ ЕТАП 9  — Сторінка гри
✅ ЕТАП 10 — Wishlist
✅ ЕТАП 11 — Імпорт бібліотеки Steam
✅ ЕТАП 12 — Alerts + нотифікації
✅ ЕТАП 13 — ITAD ціни
✅ ЕТАП 14 — Admin panel
```

---

## Якщо застряг

1. Прочитай **повний текст помилки** — відповідь зазвичай там
2. Шукай у Google `c# ef core [текст помилки]`
3. Запитай тут — скинь код + помилку

Головне правило: **один крок за раз**.
```
