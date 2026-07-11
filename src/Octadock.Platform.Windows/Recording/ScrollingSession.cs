using Octadock.Core.Abstractions;
using Octadock.Core.Geometry;

namespace Octadock.Platform.Windows.Recording;

/// <summary>A single captured viewport frame (BGRA, top-down).</summary>
internal sealed record ScrollFrame(byte[] Pixels, int Width, int Height, int Stride);

/// <summary>The result of comparing two scrolling-capture viewports.</summary>
internal enum VerticalScrollMatchOutcome
{
    NoMovement,
    DownwardMovement,
    Unmatched,
}

/// <summary>A confidence-qualified vertical-scroll match.</summary>
internal readonly record struct VerticalScrollMatch(
    VerticalScrollMatchOutcome Outcome,
    int Shift,
    double Score)
{
    public static VerticalScrollMatch NoMovement(double score = 0)
        => new(VerticalScrollMatchOutcome.NoMovement, 0, score);

    public static VerticalScrollMatch Downward(int shift, double score)
        => new(VerticalScrollMatchOutcome.DownwardMovement, shift, score);

    public static VerticalScrollMatch Unmatched(double score = double.PositiveInfinity)
        => new(VerticalScrollMatchOutcome.Unmatched, 0, score);
}

/// <summary>
/// Holds the running state for a manual scrolling-capture session and performs
/// vertical stitching. Each appended frame is compared against the previous one
/// to estimate how far the content scrolled; only confidently matched, newly
/// revealed rows are retained. The logic is deterministic and unit-testable.
/// </summary>
internal sealed class ScrollingSession
{
    private const int MatchBandRows = 24;
    private const int MatchSamplesPerRow = 16;
    private const double MaximumMatchScore = 12.0;
    private const double NoMovementScore = 0.75;
    private const double MinimumScoreImprovement = 2.0;
    private const double MinimumRunnerUpGap = 0.05;
    private const int MinimumBandDynamicRange = 8;
    private const int EstimatedRowOverheadBytes = 32;
    private const long DefaultRetainedByteBudget = 128L * 1024 * 1024;

    private readonly int _width;
    private readonly int _height;
    private readonly int _stride;
    private readonly int _maxStitchedRows;
    private readonly long _retainedByteBudget;

    // The stitched image as a list of BGRA rows (each _stride bytes).
    private readonly List<byte[]> _rows = new();

    private ScrollFrame? _previous;
    private bool _stopped;

    public ScrollingSession(
        PixelRect region,
        ScrollingCaptureOptions options,
        DisplayInfo monitor,
        long retainedByteBudget = DefaultRetainedByteBudget)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (region.Width <= 0 || region.Height <= 0)
        {
            throw new ArgumentException("Scrolling-capture region must have positive dimensions.", nameof(region));
        }

        long stride = (long)region.Width * 4;
        if (stride > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(region), "Scrolling-capture region is too wide.");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(retainedByteBudget);

        Region = region;
        Monitor = monitor;
        _width = region.Width;
        _height = region.Height;
        _stride = (int)stride;
        _retainedByteBudget = retainedByteBudget;

        int configuredMaxRows = options.MaxStitchedEdge > 0 ? options.MaxStitchedEdge : 32000;
        long bytesPerRetainedRow = _stride + EstimatedRowOverheadBytes;
        long budgetRows = retainedByteBudget / bytesPerRetainedRow;
        if (budgetRows < 1)
        {
            throw new ArgumentException(
                "The retained-byte budget cannot hold one row of the scrolling-capture region.",
                nameof(retainedByteBudget));
        }

        _maxStitchedRows = (int)Math.Min(configuredMaxRows, Math.Min(budgetRows, int.MaxValue));
    }

    /// <summary>The captured region on the virtual desktop.</summary>
    public PixelRect Region { get; }

    /// <summary>The monitor the region sits on (for DPI/id metadata).</summary>
    public DisplayInfo Monitor { get; }

    /// <summary>Current stitched height in pixels.</summary>
    public int StitchedHeight => _rows.Count;

    /// <summary>True when stitching stopped because a dimension or byte limit was reached.</summary>
    public bool Truncated { get; private set; }

    /// <summary>The effective maximum output height after dimension and byte budgets.</summary>
    public int MaxStitchedEdge => _maxStitchedRows;

    /// <summary>Pixel bytes currently retained, excluding conservative row-object overhead.</summary>
    internal long RetainedPixelBytes => (long)_rows.Count * _stride;

    /// <summary>
    /// Appends a frame, stitching only confidently matched rows newly revealed below
    /// the previous viewport. The first frame seeds the buffer in full.
    /// </summary>
    public void AppendFrame(ScrollFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ValidateFrame(frame);

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

        VerticalScrollMatch match = MatchVerticalShift(_previous, frame);
        switch (match.Outcome)
        {
            case VerticalScrollMatchOutcome.NoMovement:
                // Refresh the anchor for harmless repaint noise without growing the output.
                _previous = frame;
                return;

            case VerticalScrollMatchOutcome.Unmatched:
                // Keep the last accepted anchor. Advancing it to an unrelated viewport
                // could later append content whose intervening rows were never stitched.
                return;

            case VerticalScrollMatchOutcome.DownwardMovement:
                int startRow = Math.Max(0, frame.Height - match.Shift);
                AppendRows(frame, startRow, frame.Height);
                _previous = frame;
                return;
        }
    }

    /// <summary>Builds the final stitched frame (BGRA, top-down).</summary>
    public (byte[] Pixels, int Width, int Height, int Stride) BuildStitched()
    {
        int height = Math.Max(1, _rows.Count);
        long outputLength = (long)_stride * height;
        if (outputLength > _retainedByteBudget || outputLength > int.MaxValue)
        {
            throw new InvalidOperationException(
                "The stitched image exceeds the safe output allocation budget.");
        }

        byte[] pixels = new byte[(int)outputLength];

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
            if (_rows.Count >= _maxStitchedRows)
            {
                _stopped = true;
                Truncated = true;
                return;
            }

            byte[] row = new byte[_stride];
            Array.Copy(frame.Pixels, y * frame.Stride, row, 0, _stride);
            _rows.Add(row);
        }
    }

    private void ValidateFrame(ScrollFrame frame)
    {
        long requiredBytes = (long)frame.Stride * frame.Height;
        if (frame.Width != _width
            || frame.Height != _height
            || frame.Stride < _stride
            || frame.Stride <= 0
            || requiredBytes > frame.Pixels.LongLength)
        {
            throw new ArgumentException("Frame dimensions or pixel buffer do not match the session.", nameof(frame));
        }
    }

    /// <summary>
    /// Compatibility helper returning a positive shift only for a confident downward
    /// match. No movement and unmatched frames both return zero.
    /// </summary>
    internal static int EstimateVerticalShift(ScrollFrame previous, ScrollFrame current)
    {
        VerticalScrollMatch match = MatchVerticalShift(previous, current);
        return match.Outcome == VerticalScrollMatchOutcome.DownwardMovement ? match.Shift : 0;
    }

    /// <summary>
    /// Classifies the relationship between two viewports. Exact/near-static frames are
    /// reported as no movement; ambiguous, low-texture, or weak matches are unmatched;
    /// only a strong, uniquely better positive shift is accepted as downward movement.
    /// </summary>
    internal static VerticalScrollMatch MatchVerticalShift(ScrollFrame previous, ScrollFrame current)
    {
        if (!FramesAreComparable(previous, current))
        {
            return VerticalScrollMatch.Unmatched();
        }

        if (VisiblePixelsEqual(previous, current))
        {
            return VerticalScrollMatch.NoMovement();
        }

        int height = current.Height;
        int band = Math.Min(MatchBandRows, height / 2);
        if (band <= 0)
        {
            return VerticalScrollMatch.Unmatched();
        }

        int[] bandStarts = BuildMatchBandStarts(height, band);
        FrameSamples previousSamples = BuildFrameSamples(previous);
        FrameSamples currentSamples = BuildFrameSamples(current);
        int[] informativeBandStarts = bandStarts
            .Where(start => BandDynamicRange(currentSamples, start, band) >= MinimumBandDynamicRange)
            .ToArray();
        if (informativeBandStarts.Length == 0)
        {
            bool previousHasTexture = bandStarts.Any(
                start => BandDynamicRange(previousSamples, start, band) >= MinimumBandDynamicRange);
            return previousHasTexture
                ? VerticalScrollMatch.Unmatched()
                : VerticalScrollMatch.NoMovement();
        }

        MatchCandidate zero = ScoreShift(
            previousSamples,
            currentSamples,
            previous.Height,
            current.Height,
            shift: 0,
            band,
            informativeBandStarts);
        if (zero.HasEvidence && zero.Score <= NoMovementScore)
        {
            return VerticalScrollMatch.NoMovement(zero.Score);
        }

        int maxShift = height - band;
        var candidates = new List<MatchCandidate>(maxShift);
        for (int shift = 1; shift <= maxShift; shift++)
        {
            MatchCandidate candidate = ScoreShift(
                previousSamples,
                currentSamples,
                previous.Height,
                current.Height,
                shift,
                band,
                informativeBandStarts);
            if (candidate.HasEvidence)
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            return VerticalScrollMatch.Unmatched();
        }

        candidates.Sort(static (left, right) => left.Score.CompareTo(right.Score));
        MatchCandidate best = candidates[0];
        if (best.Score > MaximumMatchScore)
        {
            return VerticalScrollMatch.Unmatched(best.Score);
        }

        if (zero.HasEvidence)
        {
            double requiredImprovement = Math.Max(MinimumScoreImprovement, zero.Score * 0.25);
            if ((zero.Score - best.Score) < requiredImprovement)
            {
                return VerticalScrollMatch.Unmatched(best.Score);
            }
        }

        if (candidates.Count > 1 && (candidates[1].Score - best.Score) < MinimumRunnerUpGap)
        {
            return VerticalScrollMatch.Unmatched(best.Score);
        }

        return VerticalScrollMatch.Downward(best.Shift, best.Score);
    }

    private static bool FramesAreComparable(ScrollFrame previous, ScrollFrame current)
    {
        if (previous.Width <= 0
            || previous.Height <= 0
            || previous.Width != current.Width
            || previous.Height != current.Height)
        {
            return false;
        }

        long visibleRowBytes = (long)previous.Width * 4;
        long previousRequired = (long)previous.Stride * previous.Height;
        long currentRequired = (long)current.Stride * current.Height;
        return previous.Stride >= visibleRowBytes
            && current.Stride >= visibleRowBytes
            && previousRequired <= previous.Pixels.LongLength
            && currentRequired <= current.Pixels.LongLength;
    }

    private static bool VisiblePixelsEqual(ScrollFrame previous, ScrollFrame current)
    {
        int visibleRowBytes = previous.Width * 4;
        for (int y = 0; y < previous.Height; y++)
        {
            ReadOnlySpan<byte> previousRow = previous.Pixels.AsSpan(y * previous.Stride, visibleRowBytes);
            ReadOnlySpan<byte> currentRow = current.Pixels.AsSpan(y * current.Stride, visibleRowBytes);
            if (!previousRow.SequenceEqual(currentRow))
            {
                return false;
            }
        }

        return true;
    }

    private static int[] BuildMatchBandStarts(int height, int band)
    {
        int maxStart = Math.Max(0, height - band);
        var starts = new HashSet<int> { 0 };

        // Regular interior bands make the vote resilient to sticky headers/footers;
        // the extra early band retains evidence when a large shift leaves little overlap.
        for (int i = 1; i <= 8; i++)
        {
            starts.Add((maxStart * i) / 8);
        }

        starts.Add(Math.Clamp(Math.Max(band * 2, height / 8), 0, maxStart));
        return starts.OrderBy(value => value).ToArray();
    }

    private static MatchCandidate ScoreShift(
        FrameSamples previous,
        FrameSamples current,
        int previousHeight,
        int currentHeight,
        int shift,
        int band,
        int[] bandStarts)
    {
        var bandScores = new List<double>(bandStarts.Length);

        foreach (int bandStart in bandStarts)
        {
            if (bandStart + band > currentHeight || bandStart + shift + band > previousHeight)
            {
                continue;
            }

            bandScores.Add(BandDifference(previous, current, bandStart, shift, band));
        }

        if (bandScores.Count == 0)
        {
            return MatchCandidate.NoEvidence(shift);
        }

        // Use the best half as a robust consensus. Sticky headers can occupy several
        // adjacent bands when overlap is short; they must not veto the interior bands.
        bandScores.Sort();
        int scoresToUse = Math.Max(1, (bandScores.Count + 1) / 2);
        double score = bandScores.Take(scoresToUse).Average();
        return new MatchCandidate(shift, score, bandScores.Count);
    }

    private static FrameSamples BuildFrameSamples(ScrollFrame frame)
    {
        int samplesPerRow = Math.Min(MatchSamplesPerRow, frame.Width);
        byte[] values = new byte[frame.Height * samplesPerRow * 3];

        for (int y = 0; y < frame.Height; y++)
        {
            int rowBase = y * frame.Stride;
            int sampleRowBase = y * samplesPerRow * 3;
            for (int sample = 0; sample < samplesPerRow; sample++)
            {
                int pixel = samplesPerRow == 1
                    ? 0
                    : (sample * (frame.Width - 1)) / (samplesPerRow - 1);
                int sourceOffset = rowBase + (pixel * 4);
                int sampleOffset = sampleRowBase + (sample * 3);
                values[sampleOffset] = frame.Pixels[sourceOffset];
                values[sampleOffset + 1] = frame.Pixels[sourceOffset + 1];
                values[sampleOffset + 2] = frame.Pixels[sourceOffset + 2];
            }
        }

        return new FrameSamples(values, samplesPerRow);
    }

    private static int BandDynamicRange(FrameSamples frame, int startRow, int band)
    {
        int min = byte.MaxValue;
        int max = byte.MinValue;
        int endRow = startRow + band;

        for (int y = startRow; y < endRow; y += 2)
        {
            int rowBase = y * frame.ValuesPerRow;
            for (int offset = 0; offset < frame.ValuesPerRow; offset++)
            {
                int value = frame.Values[rowBase + offset];
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }
        }

        return max - min;
    }

    private static double BandDifference(
        FrameSamples previous,
        FrameSamples current,
        int bandStart,
        int shift,
        int band)
    {
        long total = 0;
        long samples = 0;

        for (int row = 0; row < band; row++)
        {
            int previousBase = (bandStart + row + shift) * previous.ValuesPerRow;
            int currentBase = (bandStart + row) * current.ValuesPerRow;
            for (int offset = 0; offset < current.ValuesPerRow; offset++)
            {
                total += Math.Abs(
                    previous.Values[previousBase + offset]
                    - current.Values[currentBase + offset]);
                samples++;
            }
        }

        return samples == 0 ? double.PositiveInfinity : (double)total / samples;
    }

    private readonly record struct MatchCandidate(int Shift, double Score, int EvidenceBands)
    {
        public bool HasEvidence => EvidenceBands > 0 && double.IsFinite(Score);

        public static MatchCandidate NoEvidence(int shift)
            => new(shift, double.PositiveInfinity, 0);
    }

    private sealed record FrameSamples(byte[] Values, int SamplesPerRow)
    {
        public int ValuesPerRow => SamplesPerRow * 3;
    }
}
