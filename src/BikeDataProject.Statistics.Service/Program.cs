using System;
using System.IO;
using BikeDataProject.DB.Domain;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
                .AddEnvironmentVariables((c) =>
                {
                    c.Prefix = EnvVarPrefix;
                })
                .Build();
            
            // setup serilog logging (from configuration).
            Log.Logger = new LoggerConfiguration()
                .ReadFrom.Configuration(configuration)
                .CreateLogger();
            
            // get database connection.
            var connectionString = File.ReadAllText(configuration[$"STATS_DB"]);
            var bikeDataConnectionString = File.ReadAllText(configuration[$"DB"]);
            var data = configuration["data"];
            
            // setup Dependency injection
            var serviceProvider = new ServiceCollection()
                .AddLogging(b =>
                {
                    b.AddSerilog();
                })
                .AddSingleton<Worker>()
                .AddSingleton<TrackBasedWorker>()
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
            var task = serviceProvider.GetService<TrackBasedWorker>();
            task.Run();
        }
    }
}
