using Microsoft.EntityFrameworkCore;

namespace BikeDataProject.Statistics.Domain
{
    public class StatisticsDbContext : DbContext
    {
        public StatisticsDbContext(DbContextOptions<StatisticsDbContext> options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Area>()
                .HasMany(e => e.ChildAreas)
                .WithOne(e => e.ParentArea)
                .HasForeignKey(e => e.ParentAreaId);
        }

        public DbSet<Area> Areas { get; set; }
        public DbSet<AreaAttribute> AreaAttributes { get; set; }
        public DbSet<AreaStatistic> AreaStatistics { get; set; }

        public DbSet<UpdateCount> UpdateCounts { get; set; }
    }
}