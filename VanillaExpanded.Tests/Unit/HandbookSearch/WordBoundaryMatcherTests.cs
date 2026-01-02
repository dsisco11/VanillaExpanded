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

    #region Special Boundary Characters

    [Theory]
    [InlineData("Fire-starter", "fire", true)]     // Hyphen boundary
    [InlineData("Fire_starter", "fire", true)]     // Underscore boundary
    [InlineData("(Fire) starter", "fire", true)]   // Parentheses boundary
    [InlineData("[Fire] starter", "fire", true)]   // Bracket boundary
    [InlineData("\"Fire\" starter", "fire", true)] // Quote boundary
    [InlineData("Fire, starter", "fire", true)]    // Comma boundary
    [InlineData("Fire. Starter", "fire", true)]    // Period boundary
    [InlineData("Fire: Starter", "fire", true)]    // Colon boundary
    public void ContainsFullWord_SpecialBoundaries_ReturnsTrue(string text, string searchText, bool expected)
    {
        // Act
        bool result = WordBoundaryMatcher.ContainsFullWord(text.AsSpan(), searchText.AsSpan());

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Number Boundaries

    [Theory]
    [InlineData("Fire2", "fire", false)]           // Number is not a boundary
    [InlineData("2Fire", "fire", false)]           // Number prefix is not a boundary
    [InlineData("Fire 2", "fire", true)]           // Space is a boundary
    [InlineData("Type2 Fire", "fire", true)]       // Word after alphanumeric
    public void ContainsFullWord_NumberBoundaries_HandledCorrectly(string text, string searchText, bool expected)
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
}
