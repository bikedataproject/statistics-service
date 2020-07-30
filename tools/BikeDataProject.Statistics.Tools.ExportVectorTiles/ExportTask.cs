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
using Serilog;

namespace BikeDataProject.Statistics.Tools.ExportVectorTiles
{
    public class ExportTask
    {        
        private readonly ILogger<ExportTask> _logger;
        private readonly StatisticsDbContext _dbContext;
        private readonly ExportTaskConfiguration _configuration;
        
        public ExportTask(StatisticsDbContext dbContext, ExportTaskConfiguration configuration, ILogger<ExportTask> logger)
        {
            _configuration = configuration;
            _dbContext = dbContext;
            _logger = logger;
        }

        public void Run()
        {
            // do for zoom levels 0 -> 5.
            var areas = GetForZoom(5);
            var vectorTileTree = new VectorTileTree();
            var areaCount = 0;
            foreach (var area in areas)
            {
                var areaFeature = ToFeature(area);
                var features = new FeatureCollection {areaFeature};
                vectorTileTree.Add(features, Get(0, 5));

                areaCount++;
                Log.Information($"Area count: {areaCount}: {Name(area)} ({vectorTileTree.Count})");
            }
            
            vectorTileTree.Write(_configuration.OutputPath);
        }

        private string Name(Area area)
        {
            var nameAttribute = area.AreaAttributes.FirstOrDefault(x => x.Key == "name");
            if (nameAttribute == null) return string.Empty;
            return nameAttribute.Value;
        }

        private VectorTileTreeExtensions.ToFeatureZoomAndLayerFunc Get(int minZoom, int maxZoom)
        {
            System.Collections.Generic.IEnumerable<(IFeature feature, int zoom, string layerName)> ConfigureFeature(IFeature feature)
            {
                if (feature.Attributes == null) yield break;
                if (!feature.Attributes.Exists("admin_level")) yield break;

                var adminLevelValue = feature.Attributes["admin_level"];
                if (!(adminLevelValue is string adminLevelString)) yield break;
                if (!long.TryParse(adminLevelString, NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var adminLevel)) yield break;

                if (adminLevel == 2)
                {
                    // country level.
                    for (var z = 0; z <= 7; z++)
                    {
                        if (z < minZoom || z > maxZoom) continue;
                        yield return (feature, z, "areas");
                    }
                    yield break;
                }
                if (adminLevel > 2 && adminLevel <= 4)
                {
                    // regional level.
                    for (var z = 7; z <= 10; z++)
                    {
                        if (z < minZoom || z > maxZoom) continue;
                        yield return (feature, z, "areas");
                    }
                    yield break;
                }
                for (var z = 10; z <= 14; z++)
                {
                    if (z < minZoom || z > maxZoom) continue;
                    yield return (feature, z, "areas");
                }
            }

            return ConfigureFeature;
        }

        private readonly Random Random = new Random();
        
        private IEnumerable<Area> GetForZoom(int zoom, (double left, double bottom, double right, double top)? box = null)
        {
            return _dbContext.Areas.Where(x => true).Include(x => x.AreaAttributes).Include(x => x.AreaStatistics);
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
                    
                    attributes.Add(at.Key, at.Value);
                }
            }

            if (!attributes.Exists(Constants.StatisticKeyCount))
                attributes.Add(Constants.StatisticKeyCount,
                    Random.Next(10000));
            if (!attributes.Exists(Constants.StatisticKeyMeter))
                attributes.Add(Constants.StatisticKeyMeter,
                    Random.Next(10000));
            if (!attributes.Exists(Constants.StatisticKeyTime))
                attributes.Add(Constants.StatisticKeyTime,
                    Random.Next(10000));
            
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