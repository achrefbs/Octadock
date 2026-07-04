using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.Platform.Windows.Recording;

/// <summary>A single captured viewport frame (BGRA, top-down).</summary>
internal sealed record ScrollFrame(byte[] Pixels, int Width, int Height, int Stride);

/// <summary>
/// Holds the running state for a manual scrolling-capture session and performs
/// vertical stitching. Each appended frame is compared against the previous one
/// to estimate how far the content scrolled; only the newly revealed rows are
/// added to the stitched buffer. The logic is deterministic and unit-testable.
/// </summary>
internal sealed class ScrollingSession
{
    // Number of rows sampled per matching band to find the overlap.
    private const int MatchBandRows = 24;

    private readonly int _width;
    private readonly int _stride;
    private readonly int _maxStitchedEdge;

    // The stitched image as a list of BGRA rows (each _stride bytes).
    private readonly List<byte[]> _rows = new();

    private ScrollFrame? _previous;
    private bool _stopped;

    public ScrollingSession(PixelRect region, ScrollingCaptureOptions options, DisplayInfo monitor)
    {
        Region = region;
        Monitor = monitor;
        _width = region.Width;
        _stride = region.Width * 4;
        _maxStitchedEdge = options.MaxStitchedEdge > 0 ? options.MaxStitchedEdge : 32000;
    }

    /// <summary>The captured region on the virtual desktop.</summary>
    public PixelRect Region { get; }

    /// <summary>The monitor the region sits on (for DPI/id metadata).</summary>
    public DisplayInfo Monitor { get; }

    /// <summary>Current stitched height in pixels.</summary>
    public int StitchedHeight => _rows.Count;

    /// <summary>True when stitching stopped because the configured edge limit was reached.</summary>
    public bool Truncated { get; private set; }

    /// <summary>The configured maximum output edge for this session.</summary>
    public int MaxStitchedEdge => _maxStitchedEdge;

    /// <summary>
    /// Appends a frame, stitching only the newly revealed rows below the previously
    /// captured content. The first frame seeds the buffer in full.
    /// </summary>
    public void AppendFrame(ScrollFrame frame)
    {
        if (_stopped)
        {
            return;
        }

        if (_previous is null)
        {
            AppendRows(frame, 0, frame.Height);
            _previous = frame;
            return;
        }

        int shift = EstimateVerticalShift(_previous, frame);
        if (shift <= 0)
        {
            // No downward movement detected (or scrolled up/none): nothing new.
            _previous = frame;
            return;
        }

        // The bottom `shift` rows of the new frame are the newly revealed content.
        int startRow = Math.Max(0, frame.Height - shift);
        AppendRows(frame, startRow, frame.Height);
        _previous = frame;
    }

    /// <summary>Builds the final stitched frame (BGRA, top-down).</summary>
    public (byte[] Pixels, int Width, int Height, int Stride) BuildStitched()
    {
        int height = Math.Max(1, _rows.Count);
        byte[] pixels = new byte[_stride * height];

        for (int y = 0; y < _rows.Count; y++)
        {
            Array.Copy(_rows[y], 0, pixels, y * _stride, _stride);
        }

        // If nothing was captured, hand back a single transparent row so the frame
        // invariants (positive dimensions) hold.
        return (pixels, _width, height, _stride);
    }

    private void AppendRows(ScrollFrame frame, int startRow, int endRowExclusive)
    {
        for (int y = startRow; y < endRowExclusive; y++)
        {
            if (_rows.Count >= _maxStitchedEdge)
            {
                _stopped = true;
                Truncated = true;
                return;
            }

            byte[] row = new byte[_stride];
            int copyLen = Math.Min(_stride, frame.Stride);
            Array.Copy(frame.Pixels, y * frame.Stride, row, 0, copyLen);
            _rows.Add(row);
        }
    }

    /// <summary>
    /// Estimates how many rows the content moved up (i.e. how far the user scrolled
    /// down) between two frames by finding the offset at which a band of rows from
    /// the top of <paramref name="current"/> best matches rows in
    /// <paramref name="previous"/>. Returns 0 when no confident match is found.
    /// </summary>
    internal static int EstimateVerticalShift(ScrollFrame previous, ScrollFrame current)
    {
        if (previous.Width != current.Width || previous.Height != current.Height)
        {
            return 0;
        }

        int height = current.Height;
        int band = Math.Min(MatchBandRows, height / 2);
        if (band <= 0)
        {
            return 0;
        }

        int maxShift = height - band;
        double bestScore = double.MaxValue;
        int bestShift = 0;
        int[] bandStarts = BuildMatchBandStarts(height, band);

        // When content scrolls down, pixels move up: the top of the current frame
        // and bands away from sticky edges should match rows farther down in the
        // previous frame. The shift that minimizes that difference is the amount of
        // newly revealed bottom content.
        for (int shift = 1; shift <= maxShift; shift++)
        {
            double score = RowBandDifference(previous, current, shift, band, bandStarts);
            if (score < bestScore)
            {
                bestScore = score;
                bestShift = shift;
            }
        }

        // Require the match to be reasonably strong; otherwise treat as no scroll.
        const double threshold = 12.0; // average byte-difference tolerance
        return bestScore <= threshold ? bestShift : 0;
    }

    private static int[] BuildMatchBandStarts(int height, int band)
    {
        int maxStart = Math.Max(0, height - band);
        int firstContentBand = Math.Clamp(Math.Max(band * 2, height / 8), 0, maxStart);

        return new[]
            {
                0,
                firstContentBand,
                Math.Clamp(height / 3, 0, maxStart),
                Math.Clamp(height / 2, 0, maxStart),
                Math.Clamp((height * 2) / 3, 0, maxStart),
            }
            .Distinct()
            .ToArray();
    }

    private static double RowBandDifference(ScrollFrame previous, ScrollFrame current, int shift, int band, int[] bandStarts)
    {
        var bandScores = new List<double>(bandStarts.Length);
        int width4 = Math.Min(previous.Width, current.Width) * 4;

        foreach (int bandStart in bandStarts)
        {
            if (bandStart + band > current.Height || bandStart + shift + band > previous.Height)
            {
                continue;
            }

            long total = 0;
            long samples = 0;
            for (int r = 0; r < band; r++)
            {
                int prevRow = bandStart + r + shift;
                int curRow = bandStart + r;
                int prevBase = prevRow * previous.Stride;
                int curBase = curRow * current.Stride;

                // Sample every 4th pixel (16 bytes) to keep the comparison cheap.
                for (int x = 0; x < width4; x += 16)
                {
                    total += Math.Abs(previous.Pixels[prevBase + x] - current.Pixels[curBase + x]);
                    samples++;
                }
            }

            if (samples > 0)
            {
                bandScores.Add((double)total / samples);
            }
        }

        if (bandScores.Count == 0)
        {
            return double.MaxValue;
        }

        return bandScores
            .OrderBy(score => score)
            .Take(Math.Min(2, bandScores.Count))
            .Average();
    }
}
