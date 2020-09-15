using System;
using System.Collections.Generic;
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
            CheckAreaStatistics();
            var allAreas = _statisticsDb.Areas
                .Include(x => x.AreaStatistics) // Add all the statistics. Otherwise, they'll be 'null'
                .ToList(); // Group them as a list. We might need them all anyway, there's not too much of them and we can't have multiple open queries at the same time

            if (!allAreas.Any())
            {
                throw new Exception(
                    "No areas loaded. Did you forget to import the areas? (Don't worry, I forget about it too - hence why there is an error here)");
            }

            while (HandleChunk(allAreas)) ;
        }

        /**
         * Handles 1000 contributions and commits to the database. Returns true if more work is to be done
         */
        private bool HandleChunk(List<Area> allAreas, int chunkCount = 1000)
        {
            var lastUpdateIds = _statisticsDb.UpdateCounts.Where(update => update.UpdateCountId == 1);

            UpdateCount lastUpdateId;
            if (!lastUpdateIds.Any())
            {
                lastUpdateId = new UpdateCount {LastProcessedEntry = 0, UpdateCountId = 1};
                _statisticsDb.Add(lastUpdateId);
            }
            else
            {
                lastUpdateId = lastUpdateIds.First();
            }

            var lowestId = lastUpdateId.LastProcessedEntry;
            _logger.Log(LogLevel.Information, $"Last processed entry is {lowestId}");
            var contributions = _bikeDataDb.Contributions
                .Where(contribution =>
                    contribution.ContributionId > lowestId)
                .Take(chunkCount);

            if (!contributions.Any())
            {
                return false;
            }

            var lastContribution = lowestId;

            var topLevelAreas = TopLevelAreas(allAreas);
            foreach (var contribution in contributions)
            {
                foreach (var area in topLevelAreas)
                {
                    try
                    {
                        lastContribution = contribution.ContributionId;
                        HandleTrack(contribution, area);
                    }
                    catch (Exception e)
                    {
                        _logger.Log(LogLevel.Error,
                            $"Could not handle track {contribution.ContributionId}: crashed: {e.Message}");
                    }
                }
            }

            lastUpdateId.LastProcessedEntry = lastContribution;
            _logger.Log(LogLevel.Information,
                $"Updated the statistics for {contributions.Count()} tracks between #{lowestId} to {lastContribution}");
            _statisticsDb.UpdateCounts.Update(lastUpdateId);
            _statisticsDb.SaveChanges();
            return true;
        }

        private List<Area> TopLevelAreas(IEnumerable<Area> areas)
        {
            var topLevelAreas = new List<Area>();
            foreach (var area in areas)
            {
                if (area.ParentAreaId == null)
                {
                    topLevelAreas.Add(area);
                }
            }

            return topLevelAreas;
        }

        private void HandleTrack(Contribution contribution, Area topLevelArea)
        {
            if (contribution.PointsGeom == null)
            {
                // Hmm
                _logger.Log(LogLevel.Information,
                    $"Not adding track {contribution.ContributionId}, geometry is empty");
                return;
            }

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

        /// <summary>
        /// Makes sure that all the areas have initialized statistics
        /// </summary>
        private void CheckAreaStatistics()
        {
            var statsMissing = _statisticsDb.Areas.Include(a => a.AreaStatistics)
                .Where(a => a.AreaStatistics == null || a.AreaStatistics.Count() == 0);
            if (!statsMissing.Any())
            {
                return;
            }

            _logger.Log(LogLevel.Information, "Initializing all statistics");
            foreach (var area in statsMissing)
            {
                area.AreaStatistics = new List<AreaStatistic>
                {
                    new AreaStatistic
                    {
                        AreaId = area.AreaId,
                        Key = Constants.StatisticKeyCount,
                        Value = 0
                    },
                    new AreaStatistic
                    {
                        AreaId = area.AreaId,
                        Key = Constants.StatisticKeyTime,
                        Value = 0
                    },
                    new AreaStatistic
                    {
                        AreaId = area.AreaId,
                        Key = Constants.StatisticKeyMeter,
                        Value = 0
                    }
                };
                _statisticsDb.AreaStatistics.AddRange(area.AreaStatistics);
            }

            _statisticsDb.SaveChanges();
        }

        private void UpdateStatistics(Area area, Contribution c)
        {
            AreaStatistic distanceStat = null;
            AreaStatistic countStat = null;
            AreaStatistic timeStat = null;

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

            distanceStat.Value += c.Distance;
            _statisticsDb.AreaStatistics.Update(distanceStat);
            countStat.Value++;
            _statisticsDb.AreaStatistics.Update(countStat);
            var time = (decimal) (c.TimeStampStop - c.TimeStampStart).TotalSeconds;
            time = Math.Abs(time);
            timeStat.Value += time;
            _statisticsDb.AreaStatistics.Update(timeStat);
        }
    }
}