using System.Threading.Tasks;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BikeDataProject.Statistics.Tools.ExportVectorTiles
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // read configuration.
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();
            
            // setup serilog logging (from configuration).
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            
            // get database connection.
            var connectionString = configuration["ConnectionString"];
            
            // setup DI.
            var serviceProvider = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ExportTask>()
                .AddSingleton(new ExportTaskConfiguration()
                {
                    OutputPath = configuration["data"]
                })
                .AddDbContext<StatisticsDbContext>(
                    options => options.UseNpgsql(connectionString))
                .BuildServiceProvider();

            // add serilog logger to DI provider.
            serviceProvider.GetRequiredService<ILoggerFactory>()
                .AddSerilog();
            
            //do the actual work here
            var bar = serviceProvider.GetService<ExportTask>();
            bar.Run();
        }
    }
}
