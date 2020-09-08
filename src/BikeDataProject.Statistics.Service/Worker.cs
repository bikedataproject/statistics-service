using System;
using System.Collections;
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
            var areaQueue = new HashSet<int>();
            foreach (var a in this.GetAreasWithoutChildren())
            {
                _logger.LogInformation($"Processing area {a.AreaId}");
                if (a.ParentAreaId != null) areaQueue.Add(a.ParentAreaId.Value);
                
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
                
                var countStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyCount);
                if (countStats == null)
                {
                    countStats = new AreaStatistic {Key = Constants.StatisticKeyCount, AreaId = a.AreaId};
                    _db.AreaStatistics.Add(countStats);
                }
                else
                {
                    countStats.Value = count;
                    _db.AreaStatistics.Update(countStats);
                }
                
                var distanceStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyMeter);
                if (distanceStats == null)
                {
                    distanceStats = new AreaStatistic {Key = Constants.StatisticKeyMeter, AreaId = a.AreaId};
                    _db.AreaStatistics.Add(distanceStats);
                }
                else
                {
                    distanceStats.Value = distance;
                    _db.AreaStatistics.Update(distanceStats);
                }
                
                var durationStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyTime);
                if (durationStats == null)
                {
                    durationStats = new AreaStatistic {Key = Constants.StatisticKeyTime, AreaId = a.AreaId};
                    _db.AreaStatistics.Add(durationStats);
                }
                else
                {
                    durationStats.Value = duration;
                    _db.AreaStatistics.Update(durationStats);
                }

                _db.SaveChanges();
            }

            while (areaQueue.Count > 0)
            {
                var newQueue = new HashSet<int>();
                while (areaQueue.Count > 0)
                {
                    var areaId = areaQueue.First();
                    areaQueue.Remove(areaId);

                    var parentAreaId = UpdateAreaFromChildren(areaId);

                    if (parentAreaId != null) newQueue.Add(parentAreaId.Value);
                }

                areaQueue = newQueue;
            }
        }

        internal int? UpdateAreaFromChildren(int areaId)
        {
            var area = _db.Areas.Where(x => x.AreaId == areaId)
                .Include(x => x.ChildAreas)
                .ThenInclude(x => x.AreaStatistics).First();
            
            // accumulate stats.
            var count = 0L;
            var distance = 0L;
            var duration = 0L;
            foreach (var child in area.ChildAreas)
            {
                foreach (var stat in child.AreaStatistics)
                {
                    switch (stat.Key)
                    {
                        case Constants.StatisticKeyMeter:
                            distance += (long) stat.Value;
                            break;
                        case Constants.StatisticKeyCount:
                            count += (long) stat.Value;
                            break;
                        case Constants.StatisticKeyTime:
                            duration += (long) stat.Value;
                            break;
                    }
                }
            }
            
            // write them.
            var stats = _db.AreaStatistics.Where(x => x.AreaId == areaId).ToList();
                
            var countStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyCount);
            if (countStats == null)
            {
                countStats = new AreaStatistic {Key = Constants.StatisticKeyCount, AreaId = areaId};
                _db.AreaStatistics.Add(countStats);
            }
            else
            {
                countStats.Value = count;
                _db.AreaStatistics.Update(countStats);
            }
                
            var distanceStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyMeter);
            if (distanceStats == null)
            {
                distanceStats = new AreaStatistic {Key = Constants.StatisticKeyMeter, AreaId = areaId};
                _db.AreaStatistics.Add(distanceStats);
            }
            else
            {
                distanceStats.Value = distance;
                _db.AreaStatistics.Update(distanceStats);
            }
                
            var durationStats = stats.FirstOrDefault(x => x.Key == Constants.StatisticKeyTime);
            if (durationStats == null)
            {
                durationStats = new AreaStatistic {Key = Constants.StatisticKeyTime, AreaId = areaId};
                _db.AreaStatistics.Add(durationStats);
            }
            else
            {
                durationStats.Value = duration;
                _db.AreaStatistics.Update(durationStats);
            }

            _db.SaveChanges();

            return area.ParentAreaId;
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
            var c = _db.Areas.Count();
            var pageSize = 100;
            var returned = 0;
            while (returned < c)
            {
                var page = _db.Areas.Where(x => x.ChildAreas.Count == 0).OrderBy(i => i.AreaId).Skip(returned)
                    .Take(pageSize).ToList();
                foreach (var a in page)
                {
                    yield return a;
                }

                returned += pageSize;
            }
        }
    }
}