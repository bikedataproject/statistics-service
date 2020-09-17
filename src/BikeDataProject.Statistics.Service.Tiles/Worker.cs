using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.IO;
using NetTopologySuite.IO.VectorTiles;
using NetTopologySuite.IO.VectorTiles.Mapbox;

namespace BikeDataProject.Statistics.Service.Tiles
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

            var areas = _dbContext.Areas.Where(x => x.ParentAreaId == null)
                .Include(x => x.AreaAttributes)
                .Include(x => x.AreaStatistics);

            var total = areas.Count();
            var count = 0;

            var vectorTileTree = new VectorTileTree();
            var queue = new Queue<int>();
            var start = DateTime.Now;
            foreach (var area in areas)
            {
                var areaFeature = ToFeature(area);
                var features = new FeatureCollection {areaFeature};
                vectorTileTree.Add(features, ConfigureFeature);

                queue.Enqueue(area.AreaId);
                count++;
                var end = DateTime.Now;
                Console.WriteLine($"Exported area ${count}/${total} in {(end - start).TotalMilliseconds}ms");
            }

            vectorTileTree.Write(_configuration.OutputPath);
        }

        private IEnumerable<(IFeature feature, int zoom, string layerName)> ConfigureFeature(IFeature feature)
        {
            if (feature.Attributes == null) yield break;
            if (!feature.Attributes.Exists("admin_level")) yield break;
            if (feature.Attributes.Exists("name"))
            {
                Console.WriteLine($"Exporting area {feature.Attributes["name"]}");
                _logger.LogDebug($"Exporting area {feature.Attributes["name"]}");
            }

            var adminLevelValue = feature.Attributes["admin_level"];
            if (!(adminLevelValue is string adminLevelString)) yield break;
            if (!long.TryParse(adminLevelString, NumberStyles.Any, CultureInfo.InvariantCulture,
                out var adminLevel)) yield break;

            if (adminLevel == 2)
            {
                // country level.
                for (var z = 0; z <= 7; z++)
                {
                    yield return (feature, z, "areas");
                }

                yield break;
            }

            if (adminLevel > 2 && adminLevel <= 4)
            {
                // regional level.
                for (var z = 7; z <= 10; z++)
                {
                    yield return (feature, z, "areas");
                }

                yield break;
            }

            for (var z = 10; z <= 14; z++)
            {
                yield return (feature, z, "areas");
            }
        }

        private Feature ToFeature(Area area)
        {
            var postGisReader = new PostGisReader();
            var attributes = new AttributesTable();

            attributes.Add("id", area.AreaId);
            attributes.Add("parent_id", area.ParentAreaId);

            if (area.AreaStatistics != null)
            {
                foreach (var at in area.AreaStatistics)
                {
                    if (attributes.Exists(at.Key)) continue;

                    attributes.Add(at.Key, (long) at.Value);
                }
            }

            if (!attributes.Exists(Constants.StatisticKeyCount))
                attributes.Add(Constants.StatisticKeyCount, 1);
            if (!attributes.Exists(Constants.StatisticKeyMeter))
                attributes.Add(Constants.StatisticKeyMeter, 1);
            if (!attributes.Exists(Constants.StatisticKeyTime))
                attributes.Add(Constants.StatisticKeyTime, 1);

            if (area.AreaAttributes != null)
            {
                foreach (var at in area.AreaAttributes)
                {
                    if (attributes.Exists(at.Key)) continue;

                    attributes.Add(at.Key, at.Value);
                }
            }

            var geometry = postGisReader.Read(area.Geometry);
            return new Feature(geometry, attributes);
        }
    }
}