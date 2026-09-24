namespace AutoVerify.Api.Analytics;

public sealed record DataPoint(double X, double Y, int OriginalIndex);

/// <summary>
/// Largest Triangle Three Buckets (LTTB) downsampling algorithm.
/// Reduces high-volume time-series / run charts while preserving visual peaks,
/// valleys, and statistical outliers.
/// </summary>
public static class LttbDownsampler
{
    public static List<DataPoint> Downsample(IReadOnlyList<DataPoint> data, int threshold)
    {
        if (data == null || data.Count <= threshold || threshold < 3)
        {
            return data?.ToList() ?? new List<DataPoint>();
        }

        var sampled = new List<DataPoint>(threshold);
        var dataLength = data.Count;

        // Bucket size. Leave room for start and end data points
        var every = (double)(dataLength - 2) / (threshold - 2);

        var a = 0; // Initially first point
        sampled.Add(data[a]);

        for (var i = 0; i < threshold - 2; i++)
        {
            // Calculate point average for next bucket (containing c)
            var avgX = 0.0;
            var avgY = 0.0;
            var avgRangeStart = (int)Math.Floor((i + 1) * every) + 1;
            var avgRangeEnd = (int)Math.Floor((i + 2) * every) + 1;
            if (avgRangeEnd > dataLength) avgRangeEnd = dataLength;

            var avgRangeLength = avgRangeEnd - avgRangeStart;
            if (avgRangeLength > 0)
            {
                for (var j = avgRangeStart; j < avgRangeEnd; j++)
                {
                    avgX += data[j].X;
                    avgY += data[j].Y;
                }
                avgX /= avgRangeLength;
                avgY /= avgRangeLength;
            }
            else
            {
                avgX = data[^1].X;
                avgY = data[^1].Y;
            }

            // Get the range for this bucket
            var rangeOffs = (int)Math.Floor(i * every) + 1;
            var rangeTo = (int)Math.Floor((i + 1) * every) + 1;
            if (rangeTo > dataLength) rangeTo = dataLength;

            // Point a
            var pointAX = data[a].X;
            var pointAY = data[a].Y;

            var maxArea = -1.0;
            var nextA = rangeOffs;

            for (var j = rangeOffs; j < rangeTo; j++)
            {
                // Calculate triangle area over three points: a, current point, and avg point
                var area = Math.Abs(
                    (pointAX - avgX) * (data[j].Y - pointAY) -
                    (pointAX - data[j].X) * (avgY - pointAY)
                ) * 0.5;

                if (area > maxArea)
                {
                    maxArea = area;
                    nextA = j;
                }
            }

            sampled.Add(data[nextA]);
            a = nextA;
        }

        // Always add last point
        sampled.Add(data[^1]);

        return sampled;
    }
}
