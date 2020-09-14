using System;
using System.Linq;
using BikeDataProject.DB.Domain;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.IO;

namespace BikeDataProject.Statistics.Service
{
    /// <summary>
    ///  The track-based-worker loads all the latest (yet unseen) tracks and updates the statistics-database based on them.
    /// </summary>
    public class TrackBasedWorker
    {
        private static readonly PostGisReader _postGisReader = new PostGisReader();

        private readonly BikeDataDbContext _bikeDataDb;
        private readonly StatisticsDbContext _statisticsDb;
        private readonly ILogger<Worker> _logger;

        public TrackBasedWorker(BikeDataDbContext bikeDataDb, StatisticsDbContext statisticsDb, ILogger<Worker> logger)
        {
            _bikeDataDb = bikeDataDb;
            _statisticsDb = statisticsDb;
            _logger = logger;
        }


        public void Run()
        {
            /*
             * The time when the last update of the database was performed
             * TODO actually save this somewhere
             */
            var lastUpdateTime = new DateTime(2020, 08, 01);

            /*
             * The end time.
             * Every contribution between 'lastUpdateTime' and 'now' will be taken into account and updated.
             * NB: we only take contribution.startDate into account for this interval
             */
            var now = DateTime.Now;


            var contributions = _bikeDataDb.Contributions
                .Where(contribution =>
                    lastUpdateTime <= contribution.TimeStampStart && contribution.TimeStampStart < now);


            var topLevelAreas = _statisticsDb.Areas
                .Include(x => x.AreaStatistics) // Add all the statistics. Otherwise, they'll be 'null'
                .ToList(); // Group them as a list. We might need them all anyway, there's not too much of them and we can't have multiple open queries at the same time

            foreach (var area in topLevelAreas)
            {
                if (area.ParentAreaId != null)
                {
                    continue;
                }

                _logger.Log(LogLevel.Information, "Found a top level area");

                foreach (var contribution in contributions)
                {
                    HandleTrack(contribution, area);
                }
            }

            _logger.Log(LogLevel.Information,
                $"Updated the statistics for {contributions.Count()} tracks between {lastUpdateTime} to {now}");
            _statisticsDb.SaveChanges();
        }

        private void HandleTrack(Contribution contribution, Area topLevelArea)
        {
            var contributionGeometry = _postGisReader.Read(contribution.PointsGeom);
            var contributionBBox = contributionGeometry.Envelope;
            // Update the toplevel area
            UpdateStatistics(topLevelArea, contribution);

            if (topLevelArea.ChildAreas == null)
            {
                // We have reached the bottom
                return;
            }

            foreach (var childArea in topLevelArea.ChildAreas)
            {
                var childGeometry = _postGisReader.Read(childArea.Geometry);
                var childBBox = childGeometry.Envelope;
                if (!childBBox.Covers(contributionBBox))
                {
                    // The child area bbox doesn't cover the contribution bbox -> we don't care
                    continue;
                }

                if (!childGeometry.Covers(contributionGeometry))
                {
                    continue;
                }

                // Recursively handle this child area to update the stats of the childarea and it's children
                HandleTrack(contribution, childArea);
            }
        }

        private void UpdateStatistics(Area area, Contribution c)
        {
            AreaStatistic distanceStat = null;
            AreaStatistic countStat = null;
            AreaStatistic timeStat = null;
            if (area.AreaStatistics == null)
            {
            }

            foreach (var stat in area.AreaStatistics)
            {
                switch (stat.Key)

                {
                    case Constants.StatisticKeyMeter:
                        distanceStat = stat;
                        break;
                    case Constants.StatisticKeyCount:
                        countStat = stat;
                        break;
                    case Constants.StatisticKeyTime:
                        timeStat = stat;
                        break;
                }
            }

            if (distanceStat == null)
            {
                distanceStat = new AreaStatistic
                {
                    Key = Constants.StatisticKeyMeter,
                    Value = 0
                };
                _statisticsDb.AreaStatistics.Add(distanceStat);
            }

            if (countStat == null)
            {
                countStat = new AreaStatistic
                {
                    Key = Constants.StatisticKeyCount,
                    Value = 0
                };
                _statisticsDb.AreaStatistics.Add(countStat);
            }


            if (timeStat == null)
            {
                timeStat = new AreaStatistic
                {
                    Key = Constants.StatisticKeyTime,
                    Value = 0
                };
                _statisticsDb.AreaStatistics.Add(timeStat);
            }

            distanceStat.Value += c.Distance;
            _statisticsDb.AreaStatistics.Update(distanceStat);

            countStat.Value++;
            _statisticsDb.AreaStatistics.Update(countStat);

            timeStat.Value += (decimal) (c.TimeStampStop - c.TimeStampStart).TotalSeconds;
            _statisticsDb.AreaStatistics.Update(timeStat);
        }
    }
}