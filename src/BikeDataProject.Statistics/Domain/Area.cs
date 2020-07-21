namespace BikeDataProject.Statistics.Domain
{
    public class Area
    {
        public int AreaId { get; set; }
        
        public byte[] Geometry { get; set; }
        
        public int? ParentAreaId { get; set; }
    }
}