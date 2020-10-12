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

            var parentAreas = _dbContext.Areas.Where(x => x.ParentAreaId == null)
                .Include(x => x.AreaAttributes)
                .ToList();


            var vectorTileTree = new VectorTileTree();
            ExportRecursive(parentAreas, vectorTileTree);
            
            IEnumerable<VectorTile> GetTiles()
            {
                foreach (ulong tileId in vectorTileTree)
                {
                    var tile = new NetTopologySuite.IO.VectorTiles.Tiles.Tile(tileId);
                    _logger.LogInformation($"Exporting {tile.Zoom}/{tile.X}{tile.Y}.");
                    yield return vectorTileTree[tileId];
                }
            }
            GetTiles().Write(_configuration.OutputPath);
        }

        private void ExportRecursive(IReadOnlyList<Area> parentAreas, VectorTileTree vectorTileTree)
        {
            for (var a=  0; a < parentAreas.Count; a++)
            {
                var area = parentAreas[a];
                _dbContext.Entry(area).Collection(a => a.AreaAttributes).Load();

                var (areaFeature, name) = ToFeature(area);
                _logger.Log(LogLevel.Information, $"Exporting {a+1}/{parentAreas.Count}: {name}");
                var features = new FeatureCollection {areaFeature};
                try
                {
                    vectorTileTree.Add(features, ConfigureFeature);
                }
                catch (Exception e)
                {
                    _logger.Log(LogLevel.Error, e.Message);
                    Console.WriteLine(e);
                    continue;
                }

                // // Load the child areas from the database
                // _dbContext.Entry(area).Collection(a => a.ChildAreas).Load();
                //
                // if (area.ChildAreas.Count != 0)
                // {
                //     _logger.Log(LogLevel.Information, $"Exporting {area.ChildAreas.Count} children");
                //     ExportRecursive(area.ChildAreas.ToList(), vectorTileTree);
                // }

                // Make sure these can be garbage collected!
                area.ChildAreas = null;
            }
        }


        private IEnumerable<(IFeature feature, int zoom, string layerName)> ConfigureFeature(IFeature feature)
        {
            if (feature.Attributes == null)
            {
                yield break;
            }

            if (!feature.Attributes.Exists("admin_level"))
            {
                yield break;
            }

            if (feature.Attributes.Exists("name"))
            {
                _logger.LogDebug($"Exporting area {feature.Attributes["name"]}");
            }

            var adminLevelValue = feature.Attributes["admin_level"];
            if (!(adminLevelValue is string adminLevelString))
            {
                yield break;
            }

            if (!long.TryParse(adminLevelString, NumberStyles.Any, CultureInfo.InvariantCulture,
                out var adminLevel))
            {
                yield break;
            }

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

        private (Feature, string name) ToFeature(Area area)
        {
            var postGisReader = new PostGisReader();
            var attributes = new AttributesTable
            {
                {"id", area.AreaId},
                {"parent_id", area.ParentAreaId},
            };

            var name = "";

            if (area.AreaAttributes != null)
            {
                foreach (var at in area.AreaAttributes)
                {
                    if (attributes.Exists(at.Key)) continue;

                    if (at.Key == "name")
                    {
                        name = at.Value;
                    }

                    attributes.Add(at.Key, at.Value);
                }
            }

            var geometry = postGisReader.Read(area.Geometry);
            return (new Feature(geometry, attributes), name);
        }
    }
}