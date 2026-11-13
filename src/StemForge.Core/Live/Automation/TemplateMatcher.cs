using System.Drawing;
using System.Drawing.Imaging;

namespace StemForge.Core.Live.Automation;

public sealed record TemplateMatch(bool Found, int X, int Y, double Score)
{
    public static TemplateMatch None => new(false, 0, 0, 0);
}

/// <summary>
/// Small normalised-cross-correlation template matcher used as a fallback when Live's
/// controls are not reachable through UI Automation. Correlation is invariant to
/// overall brightness, so Live's light and dark themes both work with one reference image.
/// </summary>
public static class TemplateMatcher
{
    /// <summary>
    /// Finds <paramref name="template"/> inside <paramref name="source"/>.
    /// Returns window-relative coordinates of the template's top-left corner.
    /// </summary>
    public static TemplateMatch Find(Bitmap source, Bitmap template, double threshold = 0.85, int coarseStep = 4)
    {
        var src = ToGray(source, out var srcWidth, out var srcHeight);
        var tpl = ToGray(template, out var tplWidth, out var tplHeight);

        if (tplWidth < 2 || tplHeight < 2 || tplWidth > srcWidth || tplHeight > srcHeight)
            return TemplateMatch.None;

        var templateStats = Mean(tpl, tplWidth * tplHeight);

        // A template with almost no luminance variation cannot be located reliably:
        // normalised correlation would divide by near-zero and report noise.
        if (Variance(tpl, templateStats) < MinimumTemplateVariance)
            return TemplateMatch.None;

        var best = TemplateMatch.None;
        double bestScore = double.NegativeInfinity;

        var positions = (long)(srcWidth - tplWidth + 1) * (srcHeight - tplHeight + 1);

        // Small search spaces (dialog screenshots, cropped regions) are scanned at full
        // resolution so an exact match can never be missed.
        if (positions <= ExhaustivePositionBudget)
        {
            for (var y = 0; y + tplHeight <= srcHeight; y++)
            {
                for (var x = 0; x + tplWidth <= srcWidth; x++)
                {
                    var score = Score(src, srcWidth, tpl, tplWidth, tplHeight, x, y, templateStats);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new TemplateMatch(false, x, y, score);
                    }
                }
            }

            return new TemplateMatch(bestScore >= threshold, best.X, best.Y, bestScore);
        }

        // Larger images: a coarse pass narrows the search to a handful of promising seeds.
        var seeds = new List<(int X, int Y, double Score)>();
        for (var y = 0; y + tplHeight <= srcHeight; y += coarseStep)
        {
            for (var x = 0; x + tplWidth <= srcWidth; x += coarseStep)
            {
                seeds.Add((x, y, Score(src, srcWidth, tpl, tplWidth, tplHeight, x, y, templateStats)));
            }
        }

        if (seeds.Count == 0) return TemplateMatch.None;

        seeds.Sort((a, b) => b.Score.CompareTo(a.Score));
        var seedCount = Math.Min(MaxRefinementSeeds, seeds.Count);

        // Refine every seed at full resolution. Refining only the single best coarse
        // result would miss the true location whenever it sits between coarse samples.
        for (var s = 0; s < seedCount; s++)
        {
            var (sx, sy, _) = seeds[s];
            var fromY = Math.Max(0, sy - coarseStep);
            var toY = Math.Min(srcHeight - tplHeight, sy + coarseStep);
            var fromX = Math.Max(0, sx - coarseStep);
            var toX = Math.Min(srcWidth - tplWidth, sx + coarseStep);

            for (var y = fromY; y <= toY; y++)
            {
                for (var x = fromX; x <= toX; x++)
                {
                    var score = Score(src, srcWidth, tpl, tplWidth, tplHeight, x, y, templateStats);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = new TemplateMatch(false, x, y, score);
                    }
                }
            }
        }

        if (bestScore == double.NegativeInfinity) return TemplateMatch.None;

        return new TemplateMatch(bestScore >= threshold, best.X, best.Y, bestScore);
    }

    /// <summary>How many coarse seeds get a full-resolution refinement pass.</summary>
    private const int MaxRefinementSeeds = 12;

    /// <summary>Above this many candidate positions the search switches to the seeded strategy.</summary>
    private const long ExhaustivePositionBudget = 400_000;

    private static double Score(byte[] src, int srcWidth, byte[] tpl, int tplWidth, int tplHeight, int originX, int originY, double templateMean)
    {
        double sumProduct = 0, sumSrc = 0, sumSrcSq = 0, sumTplDev = 0, sumTplDevSq = 0;
        var count = tplWidth * tplHeight;

        for (var y = 0; y < tplHeight; y++)
        {
            var srcRow = (originY + y) * srcWidth + originX;
            var tplRow = y * tplWidth;
            for (var x = 0; x < tplWidth; x++)
            {
                double s = src[srcRow + x];
                double t = tpl[tplRow + x] - templateMean;
                sumProduct += s * t;
                sumSrc += s;
                sumSrcSq += s * s;
                sumTplDev += t;
                sumTplDevSq += t * t;
            }
        }

        var srcMean = sumSrc / count;
        var numerator = sumProduct - srcMean * sumTplDev;
        var denominator = Math.Sqrt(Math.Max(1e-9, sumSrcSq - count * srcMean * srcMean) * Math.Max(1e-9, sumTplDevSq));
        return numerator / denominator;
    }

    private static double Mean(byte[] data, int count)
    {
        double sum = 0;
        for (var i = 0; i < data.Length; i++) sum += data[i];
        return sum / Math.Max(1, count);
    }

    /// <summary>Minimum luminance variance a reference image must have to be matchable.</summary>
    private const double MinimumTemplateVariance = 9.0;   // ~3 grey levels of standard deviation

    private static double Variance(byte[] data, double mean)
    {
        double sum = 0;
        for (var i = 0; i < data.Length; i++)
        {
            var d = data[i] - mean;
            sum += d * d;
        }
        return sum / Math.Max(1, data.Length);
    }

    private static byte[] ToGray(Bitmap bitmap, out int width, out int height)
    {
        width = bitmap.Width;
        height = bitmap.Height;
        var result = new byte[width * height];

        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var stride = data.Stride;
                var scan0 = (byte*)data.Scan0;
                for (var y = 0; y < height; y++)
                {
                    var row = scan0 + y * stride;
                    for (var x = 0; x < width; x++)
                    {
                        var b = row[x * 4];
                        var g = row[x * 4 + 1];
                        var r = row[x * 4 + 2];
                        result[y * width + x] = (byte)((r * 299 + g * 587 + b * 114) / 1000);
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return result;
    }
}
