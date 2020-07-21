using Microsoft.EntityFrameworkCore;

namespace BikeDataProject.Statistics.Domain
{
    public class StatisticsDbContext : DbContext
    {
        public StatisticsDbContext(DbContextOptions<StatisticsDbContext> options) : base(options)
        {
            
        }

        public DbSet<Area> Areas { get; set; }
        public DbSet<AreaAttribute> AreaAttributes { get; set; }
        public DbSet<AreaStatistic> AreaStatistics { get; set; }
    }
}