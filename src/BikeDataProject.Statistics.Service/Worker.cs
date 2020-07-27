using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BikeDataProject.DB.Domain;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NetTopologySuite.IO;

namespace BikeDataProject.Statistics.Service
{
    public class Worker
    {
        private readonly BikeDataDbContext _bikeDataDb;
        private readonly StatisticsDbContext _db;
        private readonly ILogger<Worker> _logger;

        public Worker(BikeDataDbContext bikeDataDb, StatisticsDbContext dbContext, ILogger<Worker> logger)
        {
            _bikeDataDb = bikeDataDb;
            _db = dbContext;
            _logger = logger;
        }

        public void Run()
        {
            foreach (var a in this.GetAreasWithoutChildren())
            {
                _logger.LogInformation($"Processing area {a.AreaId}");
                
                // get contributions, if any.
                var contributions = this.GetContributionsFor(a);
                
                // collect stats.
                var count = 0L;
                var distance = 0L;
                var duration = 0L;
                foreach (var c in contributions)
                {
                    count++;
                    distance += c.Distance;
                    duration += c.Duration;
                }
                
                // write them.
                var stats = _db.AreaStatistics.Where(x => x.AreaId == a.AreaId).ToList();
                var countStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyCount) ??
                                 new AreaStatistic() {Key = Constants.StatisticKeyCount, AreaId = a.AreaId};
                countStats.Value = count;
                var distanceStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyMeter) ??
                                 new AreaStatistic() {Key = Constants.StatisticKeyMeter, AreaId = a.AreaId};
                distanceStats.Value = distance;
                var durationStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyTime) ??
                                 new AreaStatistic() {Key = Constants.StatisticKeyTime, AreaId = a.AreaId};
                durationStats.Value = duration;

                _db.AreaStatistics.Add(countStats);
                _db.AreaStatistics.Add(distanceStats);
                _db.AreaStatistics.Add(durationStats);
                _db.SaveChanges();
            }
        }

        internal IEnumerable<Contribution> GetContributionsFor(Area a)
        {
            var postGisReader = new PostGisReader();
            // query bike data, get all data in the envelope around the geometry..
            var geometry = postGisReader.Read(a.Geometry);
            var envelope = geometry.EnvelopeInternal;
            var left = envelope.MinX;
            var right = envelope.MaxX;
            var top = envelope.MaxY;
            var bottom = envelope.MinY;
            var sql = $"select * from \"Contributions\" where \"PointsGeom\" && " +
                      $"ST_MakeEnvelope({left}, {bottom}, {right}, {top}, 4326)"; 
            var contributions = _bikeDataDb.Contributions
                .FromSqlRaw(sql)
                .ToList();
                
            // check if there is an overlap, if so, add to the area statistics.
            foreach (var c in contributions)
            {
                var cGeometry = postGisReader.Read(c.PointsGeom);
                if (geometry.Crosses(cGeometry)) yield return c;
            }
        }

        internal IEnumerable<Area> GetAreasWithoutChildren()
        {
            return _db.Areas.Include(x => x.ChildAreas)
                .Where(x => x.ChildAreas.Count == 0).ToList();
        }
    }
}