using Microsoft.EntityFrameworkCore;
using BtcAdvisor.Repository.Models;
using BtcAdvisor.Services.Models;

namespace BtcAdvisor.Repository;

public partial class ApplicationDbContext : DbContext
{
    public ApplicationDbContext () { }

    public ApplicationDbContext (DbContextOptions<ApplicationDbContext> options) : base (options) {}

    public virtual DbSet<ShortSellerState> ShortSellerStates { get; set; }
    public DbSet<ShortSellerPhaseNotification> ShortSellerPhaseNotifications => Set<ShortSellerPhaseNotification>();
    public virtual DbSet<IndicatorDatum> IndicatorData { get; set; }
    public virtual DbSet<IndicatorError> IndicatorErrors { get; set; }
    public virtual DbSet<IndicatorResult> IndicatorResults { get; set; }
    public virtual DbSet<BuyActionLog> BuyActionLogs { get; set; }
    public virtual DbSet<SellActionLog> SellActionLogs { get; set; }
    public virtual DbSet<ActionLog> ActionLogs { get; set; }
    public virtual DbSet<StockCandle> StockCandles { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql ("Name=ConnectionStrings:DefaultConnection");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<IndicatorError>(entity =>
        {
            entity.ToTable("IndicatorError");

            entity.HasIndex(e => e.IndicatorDataId, "IX_IndicatorError_IndicatorDataId");

            entity.HasOne(d => d.IndicatorData).WithMany(p => p.IndicatorErrors).HasForeignKey(d => d.IndicatorDataId);
        });

        modelBuilder.Entity<ActionLog>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Type).HasMaxLength(10);
            entity.Property(e => e.Price).HasColumnType("decimal(18, 8)");
            entity.Property(e => e.TradeAdvice).HasMaxLength(50);
        });

        modelBuilder.Entity<IndicatorResult>(entity =>
        {
            entity.Property(e => e.IndicatorId).HasMaxLength(100);
            entity.Property(e => e.Indicator).HasMaxLength(20);
            entity.Property(e => e.ValueMacd).HasColumnName("ValueMACD");
            entity.Property(e => e.ValueMacdhist).HasColumnName("ValueMACDHist");
            entity.Property(e => e.ValueMacdsignal).HasColumnName("ValueMACDSignal");

            entity.HasOne(d => d.IndicatorData).WithMany(p => p.IndicatorResults).HasForeignKey(d => d.IndicatorDataId);
        });

        modelBuilder.Entity<IndicatorDatum>(entity =>
        {
            entity.HasIndex(e => e.DateCreated).HasDatabaseName("IX_IndicatorDatum_DateCreated");
        });

        modelBuilder.Entity<SellActionLog>(entity =>
        {
            entity.HasOne(d => d.BuyActionLog) // Definiert die Beziehung zu BuyActionLog
                .WithMany(b => b.SellActionLogs) // BuyActionLog kann mehrere SellActionLogs haben
                .HasForeignKey(d => d.BuyActionLogId) // Fremdschlüssel in SellActionLog
                .OnDelete(DeleteBehavior.SetNull); // Setzt FK auf NULL beim Löschen des BuyActionLog
        });

        modelBuilder.Entity<StockCandle>(entity =>
        {
            entity.HasKey(e => e.CandleID);
            entity.Property(e => e.Symbol).IsRequired().HasMaxLength(10);
            entity.Property(e => e.OpenPrice).HasColumnType("decimal(10, 5)");
            entity.Property(e => e.HighPrice).HasColumnType("decimal(10, 5)");
            entity.Property(e => e.LowPrice).HasColumnType("decimal(10, 5)");
            entity.Property(e => e.ClosePrice).HasColumnType("decimal(10, 5)");
            entity.Property(e => e.Volume).IsRequired();
            entity.Property(e => e.Timestamp);
        });

        modelBuilder.Entity<ShortSellerState>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Phase).IsRequired();
            entity.Property(e => e.CreatedAtUtc).IsRequired();
        });

        modelBuilder.Entity<ShortSellerPhaseNotification>()
                .HasOne(n => n.ShortSellerState)
                .WithMany() // falls du in ShortSellerState keine Collection willst
                .HasForeignKey(n => n.ShortSellerStateId)
                .OnDelete(DeleteBehavior.Cascade);

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial (ModelBuilder modelBuilder);
}
