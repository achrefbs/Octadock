using FluentAssertions;
using Octadock.App.Editing;
using Octadock.Core.Annotations;
using Octadock.Core.Primitives;
using Xunit;

namespace Octadock.App.Tests.Editing;

public sealed class AnnotationEndpointsTests
{
    [Theory]
    [InlineData(90, 10)]
    [InlineData(10, 90)]
    [InlineData(90, 90)]
    [InlineData(10, 10)]
    [InlineData(50, 10)]
    [InlineData(90, 50)]
    public void Moving_arrow_tip_keeps_tail_fixed_in_every_direction(double x, double y)
    {
        var tail = new PointD(50, 50);
        AnnotationObject arrow = Create(AnnotationObjectType.Arrow, tail, new PointD(80, 20));

        AnnotationObject moved = AnnotationEndpoints.Move(arrow, false, new PointD(x, y));

        AnnotationEndpoints.Get(moved).Should().Be((tail, new PointD(x, y)));
        moved.Frame.X.Should().Be(Math.Min(50, x));
        moved.Frame.Y.Should().Be(Math.Min(50, y));
        moved.Frame.Width.Should().Be(Math.Abs(x - 50));
        moved.Frame.Height.Should().Be(Math.Abs(y - 50));
        // The drag snapshot must remain usable by undo and later mouse moves.
        AnnotationEndpoints.Get(arrow).Should().Be((tail, new PointD(80, 20)));
    }

    [Theory]
    [InlineData(AnnotationObjectType.Arrow)]
    [InlineData(AnnotationObjectType.Line)]
    public void Moving_start_across_end_preserves_end_identity(AnnotationObjectType type)
    {
        AnnotationObject obj = Create(type, new PointD(10, 80), new PointD(60, 20));
        AnnotationObject moved = AnnotationEndpoints.Move(obj, true, new PointD(90, 5));

        AnnotationEndpoints.Get(moved).Should().Be((new PointD(90, 5), new PointD(60, 20)));
        moved.Frame.Should().Be(new AnnotationFrame(60, 5, 30, 15));
    }

    [Fact]
    public void Legacy_frame_only_line_can_be_resized_without_losing_the_other_endpoint()
    {
        AnnotationObject line = AnnotationObject.Create(AnnotationObjectType.Line,
            new AnnotationFrame(10, 20, 80, 60), 0);

        AnnotationObject moved = AnnotationEndpoints.Move(line, false, new PointD(5, 15));

        AnnotationEndpoints.Get(moved).Should().Be((new PointD(10, 20), new PointD(5, 15)));
    }

    private static AnnotationObject Create(AnnotationObjectType type, PointD start, PointD end)
        => AnnotationObject.Create(type, AnnotationFrame.Empty, 0) with
        {
            Payload = new AnnotationPayload { Points = [start, end] },
        };
}
