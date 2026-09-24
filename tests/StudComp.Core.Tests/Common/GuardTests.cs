using StudComp.Core.Common;

namespace StudComp.Core.Tests.Common;

public class GuardTests
{
    [Fact]
    public void NotNull_returns_the_value_when_present()
    {
        var value = new object();

        Assert.Same(value, Guard.NotNull(value));
    }

    [Fact]
    public void NotNull_throws_with_the_caller_expression_as_parameter_name()
    {
        object? candidate = null;

        var exception = Assert.Throws<ArgumentNullException>(() => Guard.NotNull(candidate));

        Assert.Equal(nameof(candidate), exception.ParamName);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void NotNullOrWhiteSpace_rejects_blank_input(string? candidate)
    {
        // ThrowsAny: на null прилетает ArgumentNullException, на пробелы — ArgumentException; и то и другое нарушение контракта.
        var exception = Assert.ThrowsAny<ArgumentException>(() => Guard.NotNullOrWhiteSpace(candidate));

        Assert.Equal(nameof(candidate), exception.ParamName);
    }

    [Fact]
    public void NotNullOrWhiteSpace_returns_the_value_when_meaningful()
    {
        Assert.Equal("Матан", Guard.NotNullOrWhiteSpace("Матан"));
    }

    [Fact]
    public void NotNegative_allows_zero_and_positive()
    {
        Assert.Equal(0, Guard.NotNegative(0));
        Assert.Equal(1.5m, Guard.NotNegative(1.5m));
    }

    [Fact]
    public void NotNegative_rejects_negative()
    {
        var candidate = -1;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Guard.NotNegative(candidate));

        Assert.Equal(nameof(candidate), exception.ParamName);
    }

    [Fact]
    public void Positive_rejects_zero()
    {
        var candidate = 0m;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Guard.Positive(candidate));

        Assert.Equal(nameof(candidate), exception.ParamName);
    }

    [Fact]
    public void Positive_returns_the_value_when_above_zero()
    {
        Assert.Equal(3, Guard.Positive(3));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void InRange_accepts_inclusive_bounds(int candidate)
    {
        Assert.Equal(candidate, Guard.InRange(candidate, 1, 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void InRange_rejects_values_outside_the_bounds(int candidate)
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => Guard.InRange(candidate, 1, 5));

        Assert.Equal(nameof(candidate), exception.ParamName);
    }

    [Fact]
    public void NotEmpty_rejects_the_empty_guid()
    {
        var candidate = Guid.Empty;

        var exception = Assert.Throws<ArgumentException>(() => Guard.NotEmpty(candidate));

        Assert.Equal(nameof(candidate), exception.ParamName);
    }

    [Fact]
    public void NotEmpty_returns_the_value_when_set()
    {
        var candidate = Guid.NewGuid();

        Assert.Equal(candidate, Guard.NotEmpty(candidate));
    }
}
