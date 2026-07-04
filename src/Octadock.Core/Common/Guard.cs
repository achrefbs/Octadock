using System.Runtime.CompilerServices;

namespace Octadock.Core.Common;

/// <summary>Lightweight argument-validation helpers with caller-argument capture.</summary>
public static class Guard
{
    public static T NotNull<T>(T? value, [CallerArgumentExpression(nameof(value))] string? name = null)
        where T : class
        => value ?? throw new ArgumentNullException(name);

    public static string NotNullOrWhiteSpace(string? value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value must not be null or whitespace.", name);
        }

        return value;
    }

    public static int Positive(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value must be positive.");
        }

        return value;
    }

    public static int NotNegative(int value, [CallerArgumentExpression(nameof(value))] string? name = null)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, "Value must not be negative.");
        }

        return value;
    }
}
