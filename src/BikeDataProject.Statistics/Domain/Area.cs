using System.Collections.Generic;

namespace BikeDataProject.Statistics.Domain
{
    public class Area
    {
        public int AreaId { get; set; }
        
        public byte[] Geometry { get; set; }
        
        public int? ParentAreaId { get; set; }
        
        public Area ParentArea { get; set; }
        
        public List<Area> ChildAreas { get; set; }
        
        public List<AreaAttribute> AreaAttributes { get; set; }
        
        public List<AreaStatistic> AreaStatistics { get; set; }
    }
}