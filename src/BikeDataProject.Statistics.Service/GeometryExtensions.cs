using NetTopologySuite.Geometries;

namespace BikeDataProject.Statistics.Service
{
    public static class GeometryExtensions
    {

        /*
         *
         * Given an envelope, returns ST_MakeEnvelope({left}, {bottom}, {right}, {top}, 4326)
         */
        public static string EnvelopeToSQLQuery(this Geometry geometry)
        {
            var envelope = geometry.EnvelopeInternal;
            var left = envelope.MinX;
            var right = envelope.MaxX;
            var top = envelope.MaxY;
            var bottom = envelope.MinY;
            return $"ST_MakeEnvelope({left}, {bottom}, {right}, {top}, 4326)";
        }
        
    }
}