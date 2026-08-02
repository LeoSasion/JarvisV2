using System.Windows;
using System.Windows.Media;

namespace Jarvis.Win10.TaskbarEdgeOverlay;

public static class TaskbarEdgeVectorModel
{
    public const double RailWidth = 236.0;
    public const double RailHeight = 8.0;
    public const int GaussianBlurRadius = 3;
    public const double TriangleAx = 112.0;
    public const double TriangleAy = 1.0;
    public const double TriangleBx = 118.0;
    public const double TriangleBy = 7.0;
    public const double TriangleCx = 106.0;
    public const double TriangleCy = 7.0;
    public const double PointCenterX = 112.0;
    public const double PointCenterY = 2.75;
    public const double PointRadius = 1.25;

    public static Geometry SignalLineGeometry { get; } =
        ParseFrozen("M 0,1 L 94,1 M 142,1 L 236,1");

    public static Geometry PulseGeometry { get; } =
        ParseFrozen("M 112,1 L 118,7 L 106,7 Z");

    public static double SampleSegmentCoverage(
        int pixelX,
        int pixelY,
        double ax,
        double ay,
        double bx,
        double by,
        double strokeWidth)
    {
        const int sampleAxis = 4;
        int inside = 0;
        double radius = strokeWidth / 2.0;
        for (int sy = 0; sy < sampleAxis; sy++)
        {
            for (int sx = 0; sx < sampleAxis; sx++)
            {
                double x = pixelX + ((sx + 0.5) / sampleAxis);
                double y = pixelY + ((sy + 0.5) / sampleAxis);
                if (DistanceToSegment(x, y, ax, ay, bx, by) <= radius)
                {
                    inside++;
                }
            }
        }

        return inside / (double)(sampleAxis * sampleAxis);
    }

    public static double SampleStrokeCoverage(
        int pixelX,
        int pixelY,
        double strokeWidth)
    {
        double ab = SampleSegmentCoverage(
            pixelX,
            pixelY,
            TriangleAx,
            TriangleAy,
            TriangleBx,
            TriangleBy,
            strokeWidth);
        double bc = SampleSegmentCoverage(
            pixelX,
            pixelY,
            TriangleBx,
            TriangleBy,
            TriangleCx,
            TriangleCy,
            strokeWidth);
        double ca = SampleSegmentCoverage(
            pixelX,
            pixelY,
            TriangleCx,
            TriangleCy,
            TriangleAx,
            TriangleAy,
            strokeWidth);
        return Math.Max(ab, Math.Max(bc, ca));
    }

    public static double SamplePointCoverage(int pixelX, int pixelY)
    {
        const int sampleAxis = 4;
        int inside = 0;
        double radiusSquared = PointRadius * PointRadius;
        for (int sy = 0; sy < sampleAxis; sy++)
        {
            for (int sx = 0; sx < sampleAxis; sx++)
            {
                double x = pixelX + ((sx + 0.5) / sampleAxis);
                double y = pixelY + ((sy + 0.5) / sampleAxis);
                double dx = x - PointCenterX;
                double dy = y - PointCenterY;
                if ((dx * dx) + (dy * dy) <= radiusSquared)
                {
                    inside++;
                }
            }
        }

        return inside / (double)(sampleAxis * sampleAxis);
    }

    public static double[] GaussianKernel()
    {
        const double sigma = 1.35;
        double[] kernel = new double[(GaussianBlurRadius * 2) + 1];
        double total = 0.0;
        for (int offset = -GaussianBlurRadius;
             offset <= GaussianBlurRadius;
             offset++)
        {
            double value = Math.Exp(
                -(offset * offset) / (2.0 * sigma * sigma));
            kernel[offset + GaussianBlurRadius] = value;
            total += value;
        }

        for (int index = 0; index < kernel.Length; index++)
        {
            kernel[index] /= total;
        }

        return kernel;
    }

    private static Geometry ParseFrozen(string data)
    {
        Geometry geometry = Geometry.Parse(data);
        geometry.Freeze();
        return geometry;
    }

    private static double DistanceToSegment(
        double x,
        double y,
        double ax,
        double ay,
        double bx,
        double by)
    {
        double dx = bx - ax;
        double dy = by - ay;
        double lengthSquared = (dx * dx) + (dy * dy);
        double projection = lengthSquared <= double.Epsilon
            ? 0.0
            : Math.Clamp(
                (((x - ax) * dx) + ((y - ay) * dy)) / lengthSquared,
                0.0,
                1.0);
        double nearestX = ax + (projection * dx);
        double nearestY = ay + (projection * dy);
        double distanceX = x - nearestX;
        double distanceY = y - nearestY;
        return Math.Sqrt(
            (distanceX * distanceX) + (distanceY * distanceY));
    }
}
