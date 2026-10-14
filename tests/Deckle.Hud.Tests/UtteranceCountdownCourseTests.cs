using Deckle.Composition;
using Xunit;

namespace Deckle.Hud.Tests;

// The course is the one place the pause's linear progress bends before it
// reaches the mask. These tests pin the curve's shape, not its numbers.
[Trait("Category", "unit")]
public sealed class UtteranceCountdownCourseTests
{
    private const float Tolerance = 0.002f;

    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    [InlineData(0.5f)]
    [InlineData(0.75f)]
    [InlineData(1f)]
    public void TheDiagonalIsAPlainLerp(float progress)
    {
        var course = new UtteranceCountdownCourse(0, 0, 1, 1);
        Assert.InRange(MathF.Abs(course.Solve(progress) - progress), 0f, Tolerance);
    }

    [Fact]
    public void TheEndpointsStayClosedAndFull()
    {
        var course = UtteranceCountdownCourse.Of(new UtteranceCountdownConfig());
        Assert.Equal(0f, course.Solve(0f));
        Assert.Equal(1f, course.Solve(1f));
        Assert.Equal(0f, course.Solve(-0.5f));
        Assert.Equal(1f, course.Solve(1.5f));
    }

    // Louis, 2026-10-15: start at zero, accelerate gently to a strong speed,
    // brake only at the very end.
    [Fact]
    [Trait("Category", "regression")]
    public void TheDefaultCourseStartsSlowAndBrakesLate()
    {
        var course = UtteranceCountdownCourse.Of(new UtteranceCountdownConfig());

        Assert.True(course.Solve(0.2f) < 0.1f, "The first fifth of the pause must stay near the centre.");
        Assert.True(course.Solve(0.9f) > 0.9f, "By nine tenths the light must be nearly full.");
        Assert.True(course.Solve(0.5f) - course.Solve(0.4f) > course.Solve(0.2f) - course.Solve(0.1f),
            "The middle must run faster than the start.");
    }

    [Fact]
    public void TheCourseNeverRunsBackwards()
    {
        var course = UtteranceCountdownCourse.Of(new UtteranceCountdownConfig());
        float previous = 0f;
        for (int i = 1; i <= 100; i++)
        {
            float value = course.Solve(i / 100f);
            Assert.True(value >= previous - Tolerance, $"The course fell at {i}/100.");
            previous = value;
        }
    }
}
