using System;
using System.Collections.Generic;

namespace VanillaExpanded.RadialMenu;

/// <summary>Builds readable center-label lines without splitting individual words.</summary>
internal static class RadialMenuLabelLayout
{
    /// <summary>Chooses the whole-word line layout that maximizes rendered size inside a circle.</summary>
    internal static string FitToCircle(string text, double diameter, double lineHeight,
        Func<string, double> measure, double padding)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(measure);
        if (diameter <= 0) throw new ArgumentOutOfRangeException(nameof(diameter));
        if (lineHeight <= 0) throw new ArgumentOutOfRangeException(nameof(lineHeight));
        if (padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));

        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return string.Empty;

        string best = string.Join(' ', words);
        double bestScale = 0;
        int bestLineCount = int.MaxValue;
        for (int lineCount = 1; lineCount <= words.Length; lineCount++)
        {
            string[] lines = FindBestLayout(words, lineCount, lineHeight, measure, padding);
            double scale = GetScaleForCircle(lines, diameter, lineHeight, measure, padding);
            if (scale < bestScale || scale == bestScale && lineCount >= bestLineCount) continue;
            best = string.Join('\n', lines);
            bestScale = scale;
            bestLineCount = lineCount;
        }
        return best;
    }

    /// <summary>Gets the largest scale whose individual line rectangles remain inside the circle.</summary>
    internal static double GetScaleForCircle(IReadOnlyList<string> lines, double diameter, double lineHeight,
        Func<string, double> measure, double padding)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(measure);
        if (diameter <= 0) throw new ArgumentOutOfRangeException(nameof(diameter));
        if (lineHeight <= 0) throw new ArgumentOutOfRangeException(nameof(lineHeight));
        if (padding < 0) throw new ArgumentOutOfRangeException(nameof(padding));
        if (lines.Count == 0) return 1;

        double blockHeight = lines.Count * lineHeight;
        double requiredRadius = 0;
        for (int index = 0; index < lines.Count; index++)
        {
            double halfWidth = measure(lines[index]) / 2d + padding;
            double lineCenter = (index + 0.5d) * lineHeight - blockHeight / 2d;
            double verticalExtent = Math.Abs(lineCenter) + lineHeight / 2d + padding;
            requiredRadius = Math.Max(requiredRadius,
                Math.Sqrt(halfWidth * halfWidth + verticalExtent * verticalExtent));
        }
        return requiredRadius > 0 ? Math.Min(1d, diameter / 2d / requiredRadius) : 1d;
    }

    private static string[] FindBestLayout(string[] words, int lineCount, double lineHeight,
        Func<string, double> measure, double padding)
    {
        int count = words.Length;
        var costs = new double[lineCount + 1, count + 1];
        var starts = new int[lineCount + 1, count + 1];
        for (int lines = 0; lines <= lineCount; lines++)
            for (int end = 0; end <= count; end++)
                costs[lines, end] = double.PositiveInfinity;
        costs[0, 0] = 0;

        for (int lines = 1; lines <= lineCount; lines++)
            for (int end = lines; end <= count; end++)
                for (int start = lines - 1; start < end; start++)
                {
                    double width = measure(string.Join(' ', words[start..end]));
                    double blockHeight = lineCount * lineHeight;
                    double lineCenter = (lines - 0.5d) * lineHeight - blockHeight / 2d;
                    double verticalExtent = Math.Abs(lineCenter) + lineHeight / 2d + padding;
                    double halfWidth = width / 2d + padding;
                    double lineRadius = Math.Sqrt(halfWidth * halfWidth + verticalExtent * verticalExtent);
                    double cost = Math.Max(costs[lines - 1, start], lineRadius);
                    if (cost >= costs[lines, end]) continue;
                    costs[lines, end] = cost;
                    starts[lines, end] = start;
                }

        var linesResult = new string[lineCount];
        int position = count;
        for (int line = lineCount; line > 0; line--)
        {
            int start = starts[line, position];
            linesResult[line - 1] = string.Join(' ', words[start..position]);
            position = start;
        }
        return linesResult;
    }
}
