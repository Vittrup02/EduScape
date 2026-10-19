using Eduscape.Domain.Common;

namespace Eduscape.Domain.Rooms;

/// <summary>Position of an object as a percentage (0–100) of the scene image's width and height.</summary>
public sealed record Position
{
    public Position(double x, double y)
    {
        if (!IsPercentage(x) || !IsPercentage(y))
        {
            throw new DomainException(DomainErrors.PositionOutOfRange);
        }

        X = x;
        Y = y;
    }

    public double X { get; }

    public double Y { get; }

    private static bool IsPercentage(double value) => value is >= 0 and <= 100;
}
