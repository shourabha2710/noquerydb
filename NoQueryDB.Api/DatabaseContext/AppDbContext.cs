using Microsoft.EntityFrameworkCore;
using NoQueryDatabase.Model.Login;
using NoQueryDatabase.Model.Login.NoQueryDatabase.Model.Audit;
using NoQueryDB.Api.Models.Explorer;

namespace NoQueryDB.Api.DatabaseContext
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }

        public DbSet<LoginRequest> LoginRequests { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<Datasource> Datasources { get; set; }
        public DbSet<AuditLog> AuditLogs { get; set; }
        public DbSet<QueryHistory> QueryHistories { get; set; }
        public DbSet<SavedQuery> SavedQueries { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<QueryHistory>().ToTable("QueryHistory");
            modelBuilder.Entity<SavedQuery>().ToTable("SavedQueries");
            modelBuilder.Entity<LoginRequest>(entity =>
            {
                entity.ToTable("LoginRequests");

                entity.HasKey(x => x.Id);

                entity.Property(x => x.Email)
                      .IsRequired()
                      .HasMaxLength(255);

                entity.Property(x => x.Token)
      .IsRequired()
      .HasMaxLength(128);

                entity.Property(x => x.IsVerified)
                      .HasDefaultValue(false);

                entity.Property(x => x.CreatedAt)
                      .HasDefaultValueSql("GETUTCDATE()");

                entity.HasIndex(x => x.Token).IsUnique();
                entity.HasIndex(x => x.Email);
            });
        }
    }
}
