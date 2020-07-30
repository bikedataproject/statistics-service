using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Features;
using NetTopologySuite.IO;

namespace BikeDataProject.Statistics.Tools.ImportAreas
{
    public class ImportTask
    {
        private readonly ILogger<ImportTask> _logger;
        private readonly StatisticsDbContext _dbContext;
        private readonly ImportTaskConfiguration _configuration;
        
        public ImportTask(StatisticsDbContext dbContext, ImportTaskConfiguration configuration, ILogger<ImportTask> logger)
        {
            _configuration = configuration;
            _dbContext = dbContext;
            _logger = logger;
        }
        
        public async Task Run()
        {
            // load boundaries.
            var boundaries = this.LoadBoundaries();
            
            // remove areas that exist.
            var existing = new HashSet<long>();
            foreach (var (key, _) in boundaries)
            {
                var area = this.GetAreaById(key);
                if (area != null) existing.Add(key);
            }
            foreach (var r in existing)
            {
                boundaries.Remove(r);
            }
            
            // add all to db.
            var postGisWriter = new PostGisWriter();
            while (boundaries.Count > 0)
            {
                // get boundaries with all parents done.
                var parents = new Dictionary<long, Area>();
                var queue = new Queue<long>();
                foreach (var (key, (_, parent)) in boundaries)
                {
                    if (parent == -1)
                    {
                        queue.Enqueue(key);
                        continue;
                    }

                    if (!parents.TryGetValue(parent, out var parentArea))
                    { 
                        parentArea = this.GetAreaById(parent);
                        if (parentArea == null) continue;
                        
                        parents[parent] = parentArea;
                    }

                    queue.Enqueue(key);
                }
                
                // no more items left.
                if (queue.Count == 0) break;
                
                // convert to areas.
                while (queue.Count > 0)
                {
                    var next = queue.Dequeue();
                    var nextValue = boundaries[next];
                    boundaries.Remove(next);

                    // get parent area.
                    if (nextValue.parent == -1 || 
                        !parents.TryGetValue(nextValue.parent, out var parentArea))
                    {
                        parentArea = null;
                    }
                    
                    // create area and attributes.
                    var geometry = postGisWriter.Write(nextValue.feature.Geometry);
                    var area = new Area()
                    {
                        Geometry = geometry,
                        ParentAreaId = parentArea?.AreaId
                    };
                    await _dbContext.Areas.AddAsync(area);
                    await _dbContext.SaveChangesAsync();
                    
                    foreach (var name in nextValue.feature.Attributes.GetNames())
                    {
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        if (name == "parents") continue;

                        var value = nextValue.feature.Attributes[name];
                        if (name == "name") _logger.LogInformation($"Importing {value}...");
                        if (!(value is string valueString))
                        {
                            if (value is long l)
                            {
                                valueString = l.ToString();
                            }
                            else
                            {
                                continue;
                            }
                        }
                        
                        var areaAttribute = new AreaAttribute()
                        {
                            Key = name,
                            AreaId = area.AreaId,
                            Value = valueString
                        };
                        await _dbContext.AreaAttributes.AddAsync(areaAttribute);
                    }
                    await _dbContext.SaveChangesAsync();
                }
            }
        }

        private Area GetAreaById(long id)
        {
            var area = _dbContext.AreaAttributes.Where(x => x.Key == "id" && x.Value == id.ToString())
                .Include(x => x.Area).FirstOrDefault();
            return area?.Area;
        }

        private Dictionary<long, (IFeature feature, long parent)> LoadBoundaries()
        {
            var boundaries = new Dictionary<long, (IFeature feature, long parent)>();
            
            // read all boundaries from geojson.
            var geoJsonReader = new GeoJsonReader();
            foreach (var file in Directory.EnumerateFiles(_configuration.DataPath, "*.geojson"))
            {
                var features = geoJsonReader.Read<FeatureCollection>(
                    File.ReadAllText(file));

                foreach (var feature in features)
                {
                    if (feature.Attributes == null) continue;
                    if (!feature.Attributes.Exists("id")) continue;
                    
                    var idValue = feature.Attributes["id"];
                    if (!(idValue is long)) continue;
                    var id = (long) idValue;
                    
                    var parentId = -1L;
                    if (feature.Attributes.Exists("parents"))
                    {
                        var parents = feature.Attributes["parents"];
                        if (parents is string parentString &&
                            !string.IsNullOrWhiteSpace(parentString))
                        {
                            var parentSplit = parentString.Split(',');
                            if (parentSplit.Length > 0)
                            {
                                foreach (var parentIdString in parentSplit)
                                {
                                    if (!long.TryParse(parentIdString, NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture, out parentId))
                                    {
                                        parentId = -1;
                                    }
                                    break;
                                }
                            }
                        }
                    }
                    
                    boundaries[id] = (feature, parentId);
                }
            }

            return boundaries;
        }
    }
}