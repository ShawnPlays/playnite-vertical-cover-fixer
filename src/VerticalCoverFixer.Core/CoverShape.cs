namespace VerticalCoverFixer.Core
{
    /// <summary>What a game's current cover art looks like.</summary>
    public enum CoverShape
    {
        Vertical,
        Square,
        Horizontal,

        /// <summary>No cover set, or the cover file is gone from disk.</summary>
        Missing,

        /// <summary>A cover exists but its size couldn't be read (unusual format, or a web address not downloaded yet).</summary>
        Unknown,
    }

    public static class CoverShapes
    {
        /// <summary>
        /// Covers within 10% of 1:1 count as square; anything taller is vertical, anything wider horizontal.
        /// Same thresholds as the PC Game Cover Art Screensaver, so a fixed cover also passes its "vertical only" filter.
        /// </summary>
        public const double SquareMin = 0.9;

        public const double SquareMax = 1 / 0.9;

        public static CoverShape Classify(int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return CoverShape.Unknown;
            }

            double aspect = (double)width / height;
            return aspect < SquareMin ? CoverShape.Vertical
                : aspect > SquareMax ? CoverShape.Horizontal
                : CoverShape.Square;
        }
    }

    /// <summary>Which cover shapes get replaced.</summary>
    public sealed class ReplaceRules
    {
        public bool ReplaceSquare { get; set; } = true;

        public bool ReplaceHorizontal { get; set; } = true;

        public bool FillMissing { get; set; } = true;

        public bool Applies(CoverShape shape) => shape switch
        {
            CoverShape.Square => ReplaceSquare,
            CoverShape.Horizontal => ReplaceHorizontal,
            CoverShape.Missing => FillMissing,
            _ => false,
        };
    }
}
