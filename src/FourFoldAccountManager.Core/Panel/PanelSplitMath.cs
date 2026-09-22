namespace FourFoldAccountManager.Core.Panel;

public static class PanelSplitMath
{
    public static IReadOnlyList<double> Normalize(IReadOnlyList<double> weights)
    {
        ValidateWeights(weights);

        var scale = weights.Max();
        var scaledTotal = weights.Sum(weight => weight / scale);
        if (!double.IsFinite(scaledTotal) || scaledTotal <= 0)
        {
            throw new ArgumentException("Weights must have a finite positive total.", nameof(weights));
        }

        var normalized = new double[weights.Count];
        for (var index = 0; index < weights.Count; index++)
        {
            normalized[index] = weights[index] / scale / scaledTotal;
        }

        return Array.AsReadOnly(normalized);
    }

    public static IReadOnlyList<double> ClampToMinimum(
        IReadOnlyList<double> weights,
        double minimum = 0.30)
    {
        var normalized = Normalize(weights);
        ValidateMinimum(minimum, normalized.Count);

        return ClampNormalizedToMinimum(normalized, minimum);
    }

    private static IReadOnlyList<double> ClampNormalizedToMinimum(
        IReadOnlyList<double> normalized,
        double minimum)
    {
        var result = normalized.ToArray();
        var isFlexible = Enumerable.Repeat(true, result.Length).ToArray();
        var remainingWeight = 1d;
        var flexibleWeightTotal = normalized.Sum();

        while (true)
        {
            var fixedAnyTrack = false;
            for (var index = 0; index < result.Length; index++)
            {
                if (!isFlexible[index])
                {
                    continue;
                }

                var candidate = normalized[index] / flexibleWeightTotal * remainingWeight;
                if (candidate < minimum)
                {
                    isFlexible[index] = false;
                    result[index] = minimum;
                    remainingWeight -= minimum;
                    flexibleWeightTotal -= normalized[index];
                    fixedAnyTrack = true;
                }
            }

            if (!fixedAnyTrack)
            {
                break;
            }
        }

        var lastFlexibleIndex = -1;
        for (var index = 0; index < result.Length; index++)
        {
            if (isFlexible[index])
            {
                result[index] = Math.Round(
                    normalized[index] / flexibleWeightTotal * remainingWeight,
                    12);
                lastFlexibleIndex = index;
            }
        }

        if (lastFlexibleIndex >= 0)
        {
            result[lastFlexibleIndex] += 1d - result.Sum();
        }

        return Array.AsReadOnly(result);
    }

    public static IReadOnlyList<double> AdjustBoundary(
        IReadOnlyList<double> weights,
        int boundaryIndex,
        double deltaFraction,
        double minimum = 0.30)
    {
        var normalized = Normalize(weights);
        ValidateMinimum(minimum, normalized.Count);
        if (boundaryIndex < 0 || boundaryIndex >= normalized.Count - 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(boundaryIndex),
                boundaryIndex,
                "Boundary index must identify two adjacent tracks.");
        }

        if (!double.IsFinite(deltaFraction))
        {
            throw new ArgumentException("The boundary delta must be finite.", nameof(deltaFraction));
        }

        var adjusted = normalized.ToArray();
        var leftIndex = boundaryIndex;
        var rightIndex = boundaryIndex + 1;
        var boundedDelta = Math.Clamp(
            deltaFraction,
            -adjusted[leftIndex],
            adjusted[rightIndex]);
        adjusted[leftIndex] += boundedDelta;
        adjusted[rightIndex] -= boundedDelta;

        return ClampNormalizedToMinimum(adjusted, minimum);
    }

    private static void ValidateWeights(IReadOnlyList<double> weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        if (weights.Count < 2)
        {
            throw new ArgumentException("At least two tracks are required.", nameof(weights));
        }

        for (var index = 0; index < weights.Count; index++)
        {
            if (!double.IsFinite(weights[index]) || weights[index] <= 0)
            {
                throw new ArgumentException("Weights must be finite and greater than zero.", nameof(weights));
            }
        }
    }

    private static void ValidateMinimum(double minimum, int trackCount)
    {
        if (!double.IsFinite(minimum) || minimum <= 0 || minimum > 1d / trackCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimum),
                minimum,
                "Minimum must be greater than zero and no greater than the equal track share.");
        }
    }
}
