using System;
using System.Buffers;

namespace VanillaExpanded.HandbookSearch;

/// <summary>
/// High-performance word boundary matching for handbook search prioritization.
/// Uses .NET 8 SearchValues for efficient character scanning.
/// </summary>
internal static class WordBoundaryMatcher
{
    /// <summary>
    /// Characters that are considered word boundaries (non-word characters).
    /// </summary>
    private static readonly SearchValues<char> WordBoundaryChars = SearchValues.Create(" \t\n\r-_.,;:!?()[]{}\"'`/\\|@#$%^&*+=<>~");

    /// <summary>
    /// Checks if <paramref name="searchText"/> appears as a complete word in <paramref name="text"/>.
    /// A word is bounded by start/end of string or non-alphanumeric characters.
    /// </summary>
    /// <param name="text">The text to search within.</param>
    /// <param name="searchText">The word to find.</param>
    /// <returns>True if searchText appears as a complete word; otherwise false.</returns>
    public static bool ContainsFullWord(ReadOnlySpan<char> text, ReadOnlySpan<char> searchText)
    {
        if (text.IsEmpty || searchText.IsEmpty)
            return false;

        int index = 0;
        while (index <= text.Length - searchText.Length)
        {
            int foundIndex = text[index..].IndexOf(searchText, StringComparison.OrdinalIgnoreCase);
            if (foundIndex < 0)
                return false;

            int absoluteIndex = index + foundIndex;
            int endIndex = absoluteIndex + searchText.Length;

            bool startBoundary = absoluteIndex == 0 || IsWordBoundary(text[absoluteIndex - 1]);
            bool endBoundary = endIndex == text.Length || IsWordBoundary(text[endIndex]);

            if (startBoundary && endBoundary)
                return true;

            // Continue searching after this occurrence
            index = absoluteIndex + 1;
        }

        return false;
    }

    /// <summary>
    /// Determines if a character is a word boundary.
    /// </summary>
    private static bool IsWordBoundary(char c)
    {
        // Fast path: check against known boundary characters
        if (WordBoundaryChars.Contains(c))
            return true;

        // Fallback: any non-letter-or-digit is a boundary
        return !char.IsLetterOrDigit(c);
    }
}
