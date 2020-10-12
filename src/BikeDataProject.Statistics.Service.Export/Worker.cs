using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace BikeDataProject.Statistics.Service.Export
{
    public class Worker
    {
        private readonly ILogger<Worker> _logger;
        private readonly StatisticsDbContext _dbContext;
        private readonly WorkerConfiguration _configuration;

        public Worker(StatisticsDbContext dbContext, WorkerConfiguration configuration, ILogger<Worker> logger)
        {
            _configuration = configuration;
            _dbContext = dbContext;
            _logger = logger;
        }

        public void Run()
        {
            _logger.LogInformation($"Output directory is {_configuration.OutputPath}");
            if (!Directory.Exists(_configuration.OutputPath))
            {
                _logger.LogInformation($"Creating the output directory {_configuration.OutputPath}");
                Directory.CreateDirectory(_configuration.OutputPath);
            }

            var areas = _dbContext.Areas
                .Include(x => x.AreaStatistics);

            var total = areas.Count();
            var current = 0;
            var all = new List<string>();
            foreach (var area in areas)
            {
                current++;


                if (area.AreaStatistics.All(stat => stat.Value == 0))
                {
                    // It is empty anyway...
                    continue;
                }

                var outputFile = Path.Combine(_configuration.OutputPath, area.AreaId + ".json");


                var stats = string.Join(",",
                    area.AreaStatistics.Select(statistic => $"\"{statistic.Key}\": {statistic.Value}"));

                all.Add($"\"{area.AreaId}\": {{{stats}}}");

                // File.WriteAllText(outputFile, "{" + stats + "}\n");
                if (current % 100 == 0)
                {
                    Console.WriteLine(current + "/" + total);
                }
            }

            File.WriteAllText(_configuration.OutputPath + "/all.json",
                "{\n" + string.Join(",\n", all) + "}");
        }
    }
}