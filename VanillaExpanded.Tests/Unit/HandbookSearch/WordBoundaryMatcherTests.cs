using VanillaExpanded.HandbookSearch;

namespace VanillaExpanded.Tests.Unit.HandbookSearch;

/// <summary>
/// Tests for the WordBoundaryMatcher utility class.
/// </summary>
[Trait("Category", "Unit")]
public class WordBoundaryMatcherTests
{
    #region Basic Word Boundary Detection

    [Theory]
    [InlineData("Iron Ingot", "iron", true)]      // Start of string
    [InlineData("Copper Iron Ingot", "iron", true)] // Middle of string
    [InlineData("Copper Ingot Iron", "iron", true)] // End of string
    [InlineData("iron", "iron", true)]             // Exact match
    [InlineData("IRON INGOT", "iron", true)]       // Case insensitive
    [InlineData("iron ingot", "IRON", true)]       // Case insensitive reverse
    public void ContainsFullWord_FullWordMatch_ReturnsTrue(string text, string searchText, bool expected)
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord(text.AsSpan(), searchText.AsSpan());

        // Assert
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Ironwood Log", "iron", false)]    // Partial match at start
    [InlineData("Cast Iron", "castiron", false)]   // Words separated by space
    [InlineData("Firewood", "fire", false)]        // Partial match at start
    [InlineData("Bonfire", "fire", false)]         // Partial match at end
    [InlineData("Campfire Pit", "fire", false)]    // Partial match in middle
    public void ContainsFullWord_PartialMatch_ReturnsFalse(string text, string searchText, bool expected)
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord(text.AsSpan(), searchText.AsSpan());

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Space Boundary Tests

    [Theory]
    [InlineData("Fire starter", "fire", true)]     // Space boundary
    [InlineData("The Fire starter", "fire", true)] // Space boundaries on both sides
    [InlineData("Fire-starter", "fire", false)]    // Hyphen is not a boundary
    [InlineData("Fire_starter", "fire", false)]    // Underscore is not a boundary
    [InlineData("(Fire)", "fire", false)]          // Parentheses are not boundaries
    public void ContainsFullWord_OnlySpacesAreBoundaries(string text, string searchText, bool expected)
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord(text.AsSpan(), searchText.AsSpan());

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Adjacent Characters

    [Theory]
    [InlineData("Fire2", "fire", false)]           // Number adjacent is not a boundary
    [InlineData("2Fire", "fire", false)]           // Number prefix is not a boundary
    [InlineData("Fire 2", "fire", true)]           // Space is a boundary
    [InlineData("Type2 Fire", "fire", true)]       // Space before Fire
    public void ContainsFullWord_AdjacentCharacters_HandledCorrectly(string text, string searchText, bool expected)
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord(text.AsSpan(), searchText.AsSpan());

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ContainsFullWord_EmptyText_ReturnsFalse()
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord("".AsSpan(), "iron".AsSpan());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsFullWord_EmptySearchText_ReturnsFalse()
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord("Iron Ingot".AsSpan(), "".AsSpan());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsFullWord_BothEmpty_ReturnsFalse()
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord("".AsSpan(), "".AsSpan());

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void ContainsFullWord_SearchLongerThanText_ReturnsFalse()
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord("Iron".AsSpan(), "Iron Ingot".AsSpan());

        // Assert
        Assert.False(result);
    }

    [Theory]
    [InlineData("Ax", "ax", true)]                 // Short word exact
    [InlineData("Flax Seeds", "ax", false)]        // Short word partial
    [InlineData("Axe", "ax", false)]               // Short word prefix
    [InlineData("Battle Ax", "ax", true)]          // Short word at end
    public void ContainsFullWord_ShortSearchTerms_HandledCorrectly(string text, string searchText, bool expected)
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord(text.AsSpan(), searchText.AsSpan());

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Multiple Occurrences

    [Theory]
    [InlineData("Ironwood Iron Ingot", "iron", true)]  // Partial then full word
    [InlineData("Firewood Fire Pit", "fire", true)]    // Partial then full word
    [InlineData("Ironwood Ironstone", "iron", false)]  // Multiple partials, no full word
    public void ContainsFullWord_MultipleOccurrences_FindsFullWord(string text, string searchText, bool expected)
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord(text.AsSpan(), searchText.AsSpan());

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Word Position Tests

    [Theory]
    [InlineData("Iron Ingot", "iron", 0)]              // First word
    [InlineData("Copper Iron Ingot", "iron", 1)]       // Second word
    [InlineData("Raw Copper Iron Ingot", "iron", 2)]   // Third word
    [InlineData("A B C Iron D", "iron", 3)]            // Fourth word
    public void TryGetFullWordPosition_ReturnsCorrectWordPosition(string text, string searchText, int expectedPosition)
    {
        // Act
        bool found = WordBoundaryMatcher.TryGetFullWordPosition(text.AsSpan(), searchText.AsSpan(), out int wordPosition);

        // Assert
        Assert.True(found);
        Assert.Equal(expectedPosition, wordPosition);
    }

    [Theory]
    [InlineData("Ironwood Log", "iron")]               // Partial match, no full word
    [InlineData("No match here", "iron")]              // Not found at all
    public void TryGetFullWordPosition_NoFullWord_ReturnsFalse(string text, string searchText)
    {
        // Act
        bool found = WordBoundaryMatcher.TryGetFullWordPosition(text.AsSpan(), searchText.AsSpan(), out int wordPosition);

        // Assert
        Assert.False(found);
        Assert.Equal(-1, wordPosition);
    }

    [Fact]
    public void TryGetFullWordPosition_MultipleOccurrences_ReturnsFirstFullWordPosition()
    {
        // "Ironwood" is partial, "Iron" at position 1 is full word
        bool found = WordBoundaryMatcher.TryGetFullWordPosition(
            "Ironwood Iron Ingot".AsSpan(),
            "iron".AsSpan(),
            out int wordPosition);

        Assert.True(found);
        Assert.Equal(1, wordPosition); // "Iron" is the second word (0-indexed = 1)
    }

    #endregion

    #region Axe Search Tests

    [Theory]
    [InlineData("Copper Axe", "axe", true, 1)]         // Full word match at position 1
    [InlineData("Scrap Axe", "axe", true, 1)]          // Full word match at position 1
    [InlineData("Waxed Cheese", "axe", false, -1)]     // "axe" inside "Waxed" - not a full word
    [InlineData("Axe", "axe", true, 0)]                // Exact match at position 0
    [InlineData("Battle Axe Head", "axe", true, 1)]    // Full word in middle
    public void AxeSearch_DistinguishesFullWordFromPartial(string text, string searchText, bool expectedFound, int expectedPosition)
    {
        // Act
        bool found = WordBoundaryMatcher.TryGetFullWordPosition(text.AsSpan(), searchText.AsSpan(), out int wordPosition);

        // Assert
        Assert.Equal(expectedFound, found);
        Assert.Equal(expectedPosition, wordPosition);
    }

    /// <summary>
    /// Tests expected weight bonuses for "axe" search across different item names.
    /// Simulates what the HandbookSearchPatch would calculate.
    /// </summary>
    [Fact]
    public void AxeSearch_WeightBonusCalculation_FullWordMatchesGetBoost()
    {
        // Constants from HandbookSearchPatch
        const float FullWordBonus = 0.4f;
        const float MaxPositionBonus = 0.1f;
        const float PositionPenaltyPerWord = 0.02f;

        var testCases = new[]
        {
            ("Copper Axe", "axe", 1.0f),   // Base weight for "title contains"
            ("Scrap Axe", "axe", 1.0f),    // Base weight for "title contains"
            ("Waxed Cheese", "axe", 1.0f), // Base weight for "title contains" (partial match)
            ("Axe", "axe", 1.0f),          // Base weight (would actually be 3.0 exact, but testing contains scenario)
        };

        var results = new List<(string Title, float OriginalWeight, float BonusApplied, float FinalWeight)>();

        foreach (var (title, searchText, baseWeight) in testCases)
        {
            float bonus = 0f;

            if (WordBoundaryMatcher.TryGetFullWordPosition(title.AsSpan(), searchText.AsSpan(), out int wordPosition))
            {
                bonus = FullWordBonus + Math.Max(0f, MaxPositionBonus - (wordPosition * PositionPenaltyPerWord));
            }

            results.Add((title, baseWeight, bonus, baseWeight + bonus));
        }

        // Copper Axe: full word at position 1 -> bonus = 0.4 + (0.1 - 0.02) = 0.48
        Assert.Equal(0.48f, results[0].BonusApplied, precision: 2);

        // Scrap Axe: full word at position 1 -> bonus = 0.4 + (0.1 - 0.02) = 0.48
        Assert.Equal(0.48f, results[1].BonusApplied, precision: 2);

        // Waxed Cheese: "axe" is NOT a full word (inside "Waxed") -> bonus = 0
        Assert.Equal(0f, results[2].BonusApplied, precision: 2);

        // Verify ranking: Copper Axe and Scrap Axe should rank higher than Waxed Cheese
        Assert.True(results[0].FinalWeight > results[2].FinalWeight, 
            $"Copper Axe ({results[0].FinalWeight}) should rank higher than Waxed Cheese ({results[2].FinalWeight})");
        Assert.True(results[1].FinalWeight > results[2].FinalWeight,
            $"Scrap Axe ({results[1].FinalWeight}) should rank higher than Waxed Cheese ({results[2].FinalWeight})");
    }

    #endregion
}
