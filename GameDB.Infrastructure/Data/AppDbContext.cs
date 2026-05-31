using Microsoft.EntityFrameworkCore;
using GameDB.Domain.Entities;
using GameDB.Domain.Enums;

namespace GameDB.Infrastructure.Data;

public partial class AppDbContext : DbContext
{
    public AppDbContext() { }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public virtual DbSet<Alert> Alerts { get; set; }
    public virtual DbSet<Developer> Developers { get; set; }
    public virtual DbSet<Game> Games { get; set; }
    public virtual DbSet<GameExternalId> GameExternalIds { get; set; }
    public virtual DbSet<GameOffer> GameOffers { get; set; }
    public virtual DbSet<GameShop> GameShops { get; set; }
    public virtual DbSet<Genre> Genres { get; set; }
    public virtual DbSet<Tag> Tags { get; set; }
    public virtual DbSet<Notification> Notifications { get; set; }
    public virtual DbSet<PriceHistory> PriceHistories { get; set; }
    public virtual DbSet<Publisher> Publishers { get; set; }
    public virtual DbSet<User> Users { get; set; }
    public virtual DbSet<UserLibrary> UserLibraries { get; set; }
    public virtual DbSet<UserShopProfile> UserShopProfiles { get; set; }
    public virtual DbSet<Wishlist> Wishlists { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        => optionsBuilder.UseNpgsql("Host=localhost;Port=5432;Database=mygamedb;Username=postgres;Password=postgres");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Alert>(entity =>
        {
            entity.ToTable("Alert");
            entity.HasIndex(e => e.GameId, "IX_Alert_GameId");
            entity.HasIndex(e => e.UserId, "IX_Alert_UserId");
            entity.HasIndex(e => e.ShopId, "IX_Alert_ShopId");
            entity.Property(e => e.AutoUpdateMode).HasMaxLength(20);
            entity.HasOne(d => d.Game).WithMany(p => p.Alerts).HasForeignKey(d => d.GameId);
            entity.HasOne(d => d.User).WithMany(p => p.Alerts).HasForeignKey(d => d.UserId);
            entity.HasOne(d => d.Shop).WithMany().HasForeignKey(d => d.ShopId);
        });

        modelBuilder.Entity<Developer>(entity =>
        {
            entity.ToTable("Developer");
            entity.HasIndex(e => e.Name, "IX_Developer_Name").IsUnique();
        });

        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("Game");

            entity.HasIndex(e => e.DeveloperId, "IX_Game_DeveloperId");
            entity.HasIndex(e => e.PublisherId,  "IX_Game_PublisherId");
            entity.HasIndex(e => e.ImportStatus,  "IX_Game_ImportStatus");

            entity.Property(e => e.HeaderImage).HasMaxLength(512);
            entity.Property(e => e.IconImage).HasMaxLength(512);
            entity.Property(e => e.ImportStatus)
            .HasConversion<string>()
            .HasMaxLength(10)
            .HasDefaultValue(GameImportStatus.Basic);

            entity.HasOne(d => d.Developer).WithMany(p => p.Games).HasForeignKey(d => d.DeveloperId);
            entity.HasOne(d => d.Publisher).WithMany(p => p.Games).HasForeignKey(d => d.PublisherId);

            entity.HasMany(d => d.Genres).WithMany(p => p.Games)
                .UsingEntity<Dictionary<string, object>>(
                    "GameGenre",
                    r => r.HasOne<Genre>().WithMany().HasForeignKey("GenreId"),
                    l => l.HasOne<Game>().WithMany().HasForeignKey("GameId"),
                    j =>
                    {
                        j.HasKey("GameId", "GenreId");
                        j.ToTable("GameGenre");
                        j.HasIndex(new[] { "GenreId" }, "IX_GameGenre_GenreId");
                    });

            entity.HasMany(d => d.Tags).WithMany(p => p.Games)
                .UsingEntity<Dictionary<string, object>>(
                    "GameTag",
                    r => r.HasOne<Tag>().WithMany().HasForeignKey("TagId"),
                    l => l.HasOne<Game>().WithMany().HasForeignKey("GameId"),
                    j =>
                    {
                        j.HasKey("GameId", "TagId");
                        j.ToTable("GameTag");
                        j.HasIndex(new[] { "TagId" }, "IX_GameTag_TagId");
                    });
        });

        modelBuilder.Entity<GameExternalId>(entity =>
        {
            entity.ToTable("GameExternalId");

            // Одна гра може мати лише один ExternalId у межах одного магазину
            entity.HasIndex(e => new { e.GameId, e.ShopId }, "UQ_GameExternalId_GameId_ShopId").IsUnique();
            // Пошук по ExternalId у межах магазину (напр. знайти гру за Steam AppId)
            entity.HasIndex(e => new { e.ShopId, e.ExternalId }, "UQ_GameExternalId_ShopId_ExternalId").IsUnique();

            entity.Property(e => e.ExternalId).HasMaxLength(64);
            entity.Property(e => e.ExternalUrl).HasMaxLength(512);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");

            entity.HasOne(d => d.Game)
                  .WithMany(p => p.ExternalIds)
                  .HasForeignKey(d => d.GameId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(d => d.Shop)
                  .WithMany()
                  .HasForeignKey(d => d.ShopId);
        });

        modelBuilder.Entity<GameOffer>(entity =>
        {
            entity.ToTable("GameOffer");
            entity.HasIndex(e => e.GameId, "IX_GameOffer_GameId");
            entity.HasIndex(e => e.ShopId, "IX_GameOffer_ShopId");
            entity.Property(e => e.FinalPrice)
                  .HasComputedColumnSql("(\"CurrentPrice\" * ((1)::numeric - ((\"CurrentDiscount\")::numeric / 100.0)))", true);
            entity.HasOne(d => d.Game).WithMany(p => p.GameOffers).HasForeignKey(d => d.GameId);
            entity.HasOne(d => d.Shop).WithMany(p => p.GameOffers).HasForeignKey(d => d.ShopId);
        });

        modelBuilder.Entity<GameShop>(entity =>
        {
            entity.HasKey(e => e.ShopId);
            entity.ToTable("GameShop");
        });

        modelBuilder.Entity<Genre>(entity =>
        {
            entity.ToTable("Genre");
            entity.HasIndex(e => e.Name, "IX_Genre_Name").IsUnique();
        });

        modelBuilder.Entity<Tag>(entity =>
        {
            entity.ToTable("Tag");
            entity.HasIndex(e => e.Name, "IX_Tag_Name").IsUnique();
        });

        modelBuilder.Entity<Notification>(entity =>
        {
            entity.ToTable("Notification");
            entity.HasIndex(e => e.UserId, "IX_Notification_UserId");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("now()");
            entity.HasOne(d => d.User).WithMany(p => p.Notifications).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<PriceHistory>(entity =>
        {
            entity.HasKey(e => e.PriceHistory1);
            entity.ToTable("PriceHistory");
            entity.HasIndex(e => e.GameOfferId, "IX_PriceHistory_GameOfferId");
            entity.Property(e => e.PriceHistory1).HasColumnName("PriceHistory");
            entity.HasOne(d => d.GameOffer).WithMany(p => p.PriceHistories)
                  .HasForeignKey(d => d.GameOfferId)
                  .HasConstraintName("FK_PriceHistory_GameOffer_PriceChangeRecordedId");
        });

        modelBuilder.Entity<Publisher>(entity =>
        {
            entity.ToTable("Publisher");
            entity.HasIndex(e => e.Name, "IX_Publisher_Name").IsUnique();
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User");
            entity.HasIndex(e => e.SteamId, "UQ_User_SteamId").IsUnique().HasFilter("\"SteamId\" IS NOT NULL");
            entity.HasIndex(e => e.Email,   "UQ_User_Email").IsUnique().HasFilter("\"Email\" IS NOT NULL");
            entity.Property(e => e.SteamId).HasMaxLength(20);
        });

        modelBuilder.Entity<UserLibrary>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.GameId, e.ShopId });
            entity.ToTable("UserLibrary");
            entity.HasIndex(e => e.GameId, "IX_UserLibrary_GameId");
            entity.HasIndex(e => e.ShopId, "IX_UserLibrary_ShopId");
            entity.HasOne(d => d.Game).WithMany(p => p.UserLibraries).HasForeignKey(d => d.GameId);
            entity.HasOne(d => d.Shop).WithMany(p => p.UserLibraries).HasForeignKey(d => d.ShopId);
            entity.HasOne(d => d.User).WithMany(p => p.UserLibraries).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<UserShopProfile>(entity =>
        {
            entity.HasKey(e => e.ProfileId);
            entity.ToTable("UserShopProfile");
            entity.HasIndex(e => e.ShopId, "IX_UserShopProfile_ShopId");
            entity.HasIndex(e => e.UserId, "IX_UserShopProfile_UserId");
            entity.HasOne(d => d.Shop).WithMany(p => p.UserShopProfiles).HasForeignKey(d => d.ShopId);
            entity.HasOne(d => d.User).WithMany(p => p.UserShopProfiles).HasForeignKey(d => d.UserId);
        });

        modelBuilder.Entity<Wishlist>(entity =>
        {
            entity.HasKey(e => new { e.UserId, e.GameId });
            entity.ToTable("Wishlist");
            entity.HasIndex(e => e.GameId, "IX_Wishlist_GameId");
            entity.HasOne(d => d.Game).WithMany(p => p.Wishlists).HasForeignKey(d => d.GameId);
            entity.HasOne(d => d.User).WithMany(p => p.Wishlists).HasForeignKey(d => d.UserId);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}