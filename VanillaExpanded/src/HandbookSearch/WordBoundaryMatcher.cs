using System;

namespace VanillaExpanded.HandbookSearch;

/// <summary>
/// Word boundary matching for handbook search prioritization.
/// </summary>
internal static class WordBoundaryMatcher
{

    /// <summary>
    /// Checks if <paramref name="searchText"/> appears as a complete word in <paramref name="text"/>.
    /// A word is bounded by start/end of string or non-alphanumeric characters.
    /// </summary>
    /// <param name="text">The text to search within.</param>
    /// <param name="searchText">The word to find.</param>
    /// <returns>True if searchText appears as a complete word; otherwise false.</returns>
    public static bool ContainsFullWord(ReadOnlySpan<char> text, ReadOnlySpan<char> searchText)
    {
        return TryGetFullWordPosition(text, searchText, out _);
    }

    /// <summary>
    /// Finds the first full-word match of <paramref name="searchText"/> in <paramref name="text"/>
    /// and returns its word position (0-based index of which word it is).
    /// </summary>
    /// <param name="text">The text to search within.</param>
    /// <param name="searchText">The word to find.</param>
    /// <param name="wordPosition">The 0-based word position where the match was found, or -1 if not found.</param>
    /// <returns>True if searchText appears as a complete word; otherwise false.</returns>
    public static bool TryGetFullWordPosition(ReadOnlySpan<char> text, ReadOnlySpan<char> searchText, out int wordPosition)
    {
        wordPosition = -1;

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
            {
                // Count words before this position
                wordPosition = CountWordsBefore(text, absoluteIndex);
                return true;
            }

            // Continue searching after this occurrence
            index = absoluteIndex + 1;
        }

        return false;
    }

    /// <summary>
    /// Counts the number of complete words before the given character index.
    /// </summary>
    private static int CountWordsBefore(ReadOnlySpan<char> text, int charIndex)
    {
        int wordCount = 0;
        bool inWord = false;

        for (int i = 0; i < charIndex; i++)
        {
            bool isBoundary = IsWordBoundary(text[i]);
            if (inWord && isBoundary)
            {
                wordCount++;
                inWord = false;
            }
            else if (!inWord && !isBoundary)
            {
                inWord = true;
            }
        }

        return wordCount;
    }

    /// <summary>
    /// Determines if a character is a word boundary (space).
    /// </summary>
    private static bool IsWordBoundary(char c) => c == ' ';
}
