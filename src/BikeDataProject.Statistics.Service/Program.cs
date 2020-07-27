using System;
using BikeDataProject.DB.Domain;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BikeDataProject.Statistics.Service
{
    class Program
    {
        internal const string EnvVarPrefix = "BIKEDATA_";
        
        static void Main(string[] args)
        {            
            // read configuration.
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .AddUserSecrets<Program>()
                .AddEnvironmentVariables(prefix: EnvVarPrefix)
                .Build();
            
            // setup serilog logging (from configuration).
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            
            // get database connection.
            var connectionString = configuration[$"{Program.EnvVarPrefix}STATS_DB"];
            var bikeDataConnectionString = configuration[$"{Program.EnvVarPrefix}DB"];
            var data = configuration["data"];
            
            // setup DI.
            var serviceProvider = new ServiceCollection()
                .AddLogging(b =>
                {
                    b.AddSerilog();
                })
                .AddSingleton<Worker>()
                .AddSingleton(new WorkerConfiguration()
                {
                    DataPath = data
                })
                .AddDbContext<StatisticsDbContext>(
                    options => options.UseNpgsql(connectionString))
                .AddDbContext<BikeDataDbContext>(
                    options => options.UseNpgsql(bikeDataConnectionString))
                .BuildServiceProvider();
            
            //do the actual work here
            var task = serviceProvider.GetService<Worker>();
            task.Run();
        }
    }
}
