using System.Collections.Generic;

namespace BikeDataProject.Statistics.Domain
{
    public class Area
    {
        public int AreaId { get; set; }
        
        public virtual byte[] Geometry { get; set; }
        
        public int? ParentAreaId { get; set; }
        
        public Area ParentArea { get; set; }
        
        // The childareas are 'virtual' to have them loaded lazily when needed
        public List<Area> ChildAreas { get; set; }
        
        public List<AreaAttribute> AreaAttributes { get; set; }
        
        public List<AreaStatistic> AreaStatistics { get; set; }
    }
}