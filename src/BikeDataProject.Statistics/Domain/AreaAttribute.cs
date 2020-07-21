namespace BikeDataProject.Statistics.Domain
{
    public class AreaAttribute
    {
        public int AreaAttributeId { get; set; }
        
        public int AreaId { get; set; }
        
        public Area Area { get; set; }
        
        public string Key { get; set; }
        
        public string Value { get; set; }
    }
}