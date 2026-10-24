using Microsoft.EntityFrameworkCore;
using PortfolioApi.Data.Entities;

namespace PortfolioApi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }
        public DbSet<Portfolio> Portfolios { get; set; } = null!;
        public DbSet<Transaction> Transactions { get; set; } = null!;
        public DbSet<Holding> Holdings { get; set; } = null!;
        public DbSet<Asset> Assets { get; set; } = null!;

        protected override void ConfigureConventions(ModelConfigurationBuilder builder)
        {
            builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Asset>(e =>
            {
                e.HasIndex(a => a.Symbol).IsUnique();
                e.Property(a => a.Symbol).HasMaxLength(10).IsRequired();
                e.Property(a => a.Name).HasMaxLength(200).IsRequired();
                e.Property(a => a.AssetType).HasConversion<string>().HasMaxLength(10);
                e.Property(a => a.CurrentPrice).HasPrecision(18, 4);
            });

            modelBuilder.Entity<Portfolio>(e =>
            {
                e.HasIndex(p => new { p.Name, p.OwnerEmail }).IsUnique();
                e.Property(p => p.Name).HasMaxLength(200).IsRequired();
                e.Property(p => p.OwnerEmail).HasMaxLength(100).IsRequired();
                e.Property(p => p.BaseCurrency).HasMaxLength(3).IsRequired();
                e.Property(p => p.BaseCurrency).HasMaxLength(3).IsRequired().HasDefaultValue("USD");
                e.HasIndex(p => new { p.OwnerEmail, p.Name }).IsUnique();

            });

            modelBuilder.Entity<Transaction>(e =>
            {
                e.Property(t => t.Quantity).HasPrecision(18, 6);
                e.Property(t => t.UnitPrice).HasPrecision(18, 4);
                e.Property(t => t.Fee).HasPrecision(18, 2);
                e.Property(t => t.Type).HasConversion<string>().HasMaxLength(10);
                e.HasOne(t => t.Portfolio).WithMany(p => p.Transactions)
                .HasForeignKey(t => t.PortfolioId)
                .OnDelete(DeleteBehavior.Restrict);

                e.HasOne(t => t.Asset).WithMany()
                .HasForeignKey(t => t.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
                e.HasIndex(t => new { t.PortfolioId, t.ExecutedAtUtc });
                e.Property(t => t.Fee).HasPrecision(18, 2).HasDefaultValue(0m);
            });

            modelBuilder.Entity<Holding>(e =>
            {
                e.HasIndex(h => new { h.PortfolioId, h.AssetId }).IsUnique();
                e.Property(h => h.Quantity).HasPrecision(18, 6);
                e.Property(h => h.AverageCost).HasPrecision(18, 4);
                e.Property(h => h.RealizedPnl).HasPrecision(18, 2);
                e.Property(h => h.RowVersion).IsRowVersion();
                e.HasOne(h => h.Portfolio).WithMany(p => p.Holdings)
                .HasForeignKey(h => h.PortfolioId)
                .OnDelete(DeleteBehavior.Restrict);
                e.HasOne(h => h.Asset).WithMany()
                .HasForeignKey(h => h.AssetId)
                .OnDelete(DeleteBehavior.Restrict);
                e.ToTable(t => t.HasCheckConstraint("CK_Holdings_Quantity_NonNegative", "[Quantity] >= 0"));
            });

        }
    }
}
