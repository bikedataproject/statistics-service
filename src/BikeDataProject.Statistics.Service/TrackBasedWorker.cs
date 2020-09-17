using System;
using System.Collections.Generic;
using System.Linq;
using BikeDataProject.DB.Domain;
using BikeDataProject.Statistics.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace BikeDataProject.Statistics.Service
{
    /// <summary>
    ///  The track-based-worker loads all the latest (yet unseen) tracks and updates the statistics-database based on them.
    /// </summary>
    public class TrackBasedWorker
    {
        private string GeometryToGeojson(Geometry geometry)
        {
            var geojsonParts = new List<string>();
            foreach (var c in geometry.Coordinates)
            {
                geojsonParts.Add($"[{c.X},{c.Y}]");
            }

            return string.Join(",", geojsonParts);
        }


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
            var topLevelAreas = _statisticsDb.Areas
                .Where(a => a.ParentAreaId == null)
                .ToList(); // Group them as a list. We might need them all anyway, there's not too much of them and we can't have multiple open queries at the same time

            if (!topLevelAreas.Any())
            {
                throw new Exception(
                    "No areas loaded. Did you forget to import the areas? (Don't worry, I forget about it too - hence why there is an error here)");
            }

            _logger.LogInformation($"Loaded {topLevelAreas.Count} top level areas");

            while (HandleChunk(topLevelAreas)) ;
        }

        /**
         * Handles 1000 contributions and commits to the database. Returns true if more work is to be done
         */
        private bool HandleChunk(List<Area> topLevelAreas, int chunkCount = 1000)
        {
            var lastUpdateIds = _statisticsDb.UpdateCounts.Where(update => update.UpdateCountId == 1);

            UpdateCount lastUpdateId;
            if (!lastUpdateIds.Any())
            {
                lastUpdateId = new UpdateCount {LastProcessedEntry = 0, UpdateCountId = 1};
                _statisticsDb.Add(lastUpdateId);
                _statisticsDb.SaveChanges();
                // We retry
                return true;
                
            }

            lastUpdateId = lastUpdateIds.First();

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

            foreach (var contribution in contributions)
            {
                lastContribution = contribution.ContributionId;
                var contributionGeometry = _postGisReader.Read(contribution.PointsGeom);
                var contributionBBox = contributionGeometry.Envelope;
                var addedToNAreas = 0;
                var start = DateTime.Now;
                foreach (var area in topLevelAreas)
                {
                    try
                    {
                        addedToNAreas += HandleTrack(contribution, area,
                            contributionGeometry, contributionBBox);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        _logger.Log(LogLevel.Error,
                            $"Could not handle track {contribution.ContributionId}: crashed: {e.Message}");
                    }
                }

                Console.WriteLine(
                    $"Track {lastContribution} was added to {addedToNAreas} areas in {(DateTime.Now - start).TotalMilliseconds}ms");
            }

            lastUpdateId.LastProcessedEntry = lastContribution;
            _statisticsDb.UpdateCounts.Update(lastUpdateId);

            _logger.Log(LogLevel.Information,
                $"Updated the statistics for {contributions.Count()} tracks between #{lowestId} to {lastContribution}");
            _statisticsDb.SaveChanges();
            return true;
        }

        private Dictionary<int, Geometry> areaBBoxes = new Dictionary<int, Geometry>();
        private Dictionary<int, Geometry> areaGeometries = new Dictionary<int, Geometry>();

        private int HandleTrack(Contribution contribution,
            Area topLevelArea,
            Geometry contributionGeometry,
            Geometry contributionBBox)
        {
            var addedToNAreas = 0;
            if (contribution.PointsGeom == null)
            {
                // Hmm
                _logger.Log(LogLevel.Information,
                    $"Not adding track {contribution.ContributionId}, geometry is empty");
                return addedToNAreas;
            }

            _statisticsDb.Entry(topLevelArea).Collection(a => a.AreaStatistics).Load();


            if (!areaGeometries.TryGetValue(topLevelArea.AreaId, out var areaGeometry))
            {
                areaGeometry = areaGeometries[topLevelArea.AreaId] = _postGisReader.Read(topLevelArea.Geometry);
            }

            if (!areaBBoxes.TryGetValue(topLevelArea.AreaId, out var areaBBox))
            {
                areaBBox = areaBBoxes[topLevelArea.AreaId] = areaGeometry.Envelope;
            }

            if (!areaBBox.Covers(contributionBBox))
            {
                // The child area bbox doesn't cover the contribution bbox -> we don't care
                return addedToNAreas;
            }

            if (!areaGeometry.Covers(contributionGeometry))
            {
                return addedToNAreas;
            }


            // Update the toplevel area
            UpdateStatistics(topLevelArea, contribution);
            addedToNAreas++;
            // Load the child areas from the database
            _statisticsDb.Entry(topLevelArea).Collection(a => a.ChildAreas).Load();

            if (topLevelArea.ChildAreas == null)
            {
                // We have reached the bottom
                return addedToNAreas;
            }


            foreach (var childArea in topLevelArea.ChildAreas)
            {
                // Recursively handle this child area to update the stats of the childarea and it's children
                addedToNAreas += HandleTrack(contribution, childArea, contributionGeometry, contributionBBox);
            }

            return addedToNAreas;
        }


        private void CheckAreaStatistics()
        {
            while (CheckAreaStatisticsChunked()) ;
        }

        /// <summary>
        /// Makes sure that all the areas have initialized statistics
        /// </summary>
        private bool CheckAreaStatisticsChunked()
        {
            var statsMissing = _statisticsDb.Areas
                .Include(a => a.AreaStatistics)
                .Where(a => a.AreaStatistics == null || !a.AreaStatistics.Any())
                .Take(1000);
            if (!statsMissing.Any())
            {
                return false;
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
            return true;
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