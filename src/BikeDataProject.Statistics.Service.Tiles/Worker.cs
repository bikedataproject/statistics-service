using System;
using System.Collections.Generic;
using System.Globalization;
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
            var areas = this.GetAreas();

            IEnumerable<(IFeature feature, int zoom, string layerName)> ConfigureFeature(IFeature feature)
            {
                if (feature.Attributes == null) yield break;
                if (!feature.Attributes.Exists("admin_level")) yield break;

                var adminLevelValue = feature.Attributes["admin_level"];
                if (!(adminLevelValue is string adminLevelString)) yield break;
                if (!long.TryParse(adminLevelString, NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var adminLevel)) yield break;

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

            var vectorTileTree = new VectorTileTree();
            foreach (var area in areas)
            {
                var areaFeature = ToFeature(area);
                var features = new FeatureCollection {areaFeature};

                try
                {
                    vectorTileTree.Add(features, ConfigureFeature);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Unhandled exception while processing area {area.AreaId}");
                }
            }

            vectorTileTree.Write(_configuration.OutputPath);
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
                    
                    attributes.Add(at.Key, (long)at.Value);
                }
            }

            if (!attributes.Exists(Constants.StatisticKeyCount))
                attributes.Add(Constants.StatisticKeyCount,1);
            if (!attributes.Exists(Constants.StatisticKeyMeter))
                attributes.Add(Constants.StatisticKeyMeter,1);
            if (!attributes.Exists(Constants.StatisticKeyTime))
                attributes.Add(Constants.StatisticKeyTime,1);
            
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

        private IEnumerable<Area> GetAreas()
        {
            var c = _dbContext.Areas.Count();
            var pageSize = 100;
            var returned = 0;
            while (returned < c)
            {
                var page = _dbContext.Areas.Include(x => x.AreaAttributes)
                    .Include(x => x.AreaStatistics).OrderBy(i => i.AreaId).Skip(returned)
                    .Take(pageSize).ToList();
                var i = 0;
                foreach (var a in page)
                {
                    var name = a.AreaAttributes.Where(x => x.Key == "name").Select(x => x.Value).FirstOrDefault();
                    _logger.LogInformation($"Processing {i+returned+1}/{c} {name}...");
                    i++;
                    yield return a;
                }

                returned += pageSize;
            }
        }
    }
}