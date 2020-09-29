using System.IO;
using System.Threading.Tasks;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace BikeDataProject.Statistics.Tools.ImportAreas
{
    class Program
    {
        internal const string EnvVarPrefix = "BIKEDATA_";
        
        static async Task Main(string[] args)
        {
            // read configuration.
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets<ImportTask>()
                .AddEnvironmentVariables((c) =>
                {
                    c.Prefix = EnvVarPrefix;
                })
                .Build();
            
            // setup serilog logging (from configuration).
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            
            // get database connection.
            var connectionString = await File.ReadAllTextAsync(configuration[$"STATS_DB"]);
            
            // setup DI.
            var serviceProvider = new ServiceCollection()
                .AddLogging()
                .AddSingleton<ImportTask>()
                .AddSingleton(new ImportTaskConfiguration()
                {
                    DataPath = configuration["boundaryDataDirectory"]
                })
                .AddDbContext<StatisticsDbContext>(
                        options => options.UseNpgsql(connectionString))
                .BuildServiceProvider();

            // add serilog logger to DI provider.
            serviceProvider.GetRequiredService<ILoggerFactory>()
                .AddSerilog();
            
            //do the actual work here
            var bar = serviceProvider.GetService<ImportTask>();
            await bar.Run();
        }
    }
}
