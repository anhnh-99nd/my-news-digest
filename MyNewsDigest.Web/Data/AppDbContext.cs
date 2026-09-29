using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using MyNewsDigest.Web.Models;

namespace MyNewsDigest.Web.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Cluster> Clusters => Set<Cluster>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();
    public DbSet<Digest> Digests => Set<Digest>();
    public DbSet<DigestItem> DigestItems => Set<DigestItem>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Source>().HasIndex(s => s.Key).IsUnique();

        b.Entity<Article>().HasIndex(a => a.Url).IsUnique();
        b.Entity<Article>().HasIndex(a => a.PublishedAt);
        b.Entity<Article>().HasOne(a => a.Source).WithMany().HasForeignKey(a => a.SourceId);
        b.Entity<Article>().HasOne(a => a.Cluster).WithMany(c => c.Articles)
            .HasForeignKey(a => a.ClusterId).OnDelete(DeleteBehavior.SetNull);

        b.Entity<Cluster>().HasIndex(c => c.LastUpdatedAt);
        b.Entity<Cluster>().HasIndex(c => c.IsSaved);
        b.Entity<Cluster>().Ignore(c => c.IsRead).Ignore(c => c.HasUpdate);

        b.Entity<AppSetting>().HasKey(s => s.Key);

        b.Entity<Digest>().HasIndex(d => new { d.LocalDate, d.Slot }).IsUnique();
        b.Entity<Digest>().HasIndex(d => d.CreatedAt);
        b.Entity<DigestItem>().HasOne(i => i.Digest).WithMany(d => d.Items)
            .HasForeignKey(i => i.DigestId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<DigestItem>().HasOne(i => i.Cluster).WithMany()
            .HasForeignKey(i => i.ClusterId).OnDelete(DeleteBehavior.SetNull);

        // SQLite không lưu DateTimeKind → đánh dấu lại UTC khi đọc để JSON trả về có hậu tố Z
        var utc = new ValueConverter<DateTime, DateTime>(v => v, v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
        var utcNullable = new ValueConverter<DateTime?, DateTime?>(
            v => v, v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
        foreach (var entity in b.Model.GetEntityTypes())
        foreach (var prop in entity.GetProperties())
        {
            if (prop.ClrType == typeof(DateTime)) prop.SetValueConverter(utc);
            else if (prop.ClrType == typeof(DateTime?)) prop.SetValueConverter(utcNullable);
        }
    }
}
