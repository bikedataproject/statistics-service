namespace BikeDataProject.Statistics.Domain
{
    public class AreaStatistic
    {
        public int AreaStatisticId { get; set; }
        
        public int AreaId { get; set; }
        
        public string Key { get; set; }
        
        public decimal Value { get; set; }
    }
}