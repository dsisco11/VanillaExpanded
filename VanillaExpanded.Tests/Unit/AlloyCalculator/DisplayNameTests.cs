using VanillaExpanded.AlloyCalculator;

using Vintagestory.API.Common;

namespace VanillaExpanded.Tests.Unit.AlloyCalculator;

/// <summary>
/// Tests for display name extraction logic in AlloyCalculatorLogic.
/// </summary>
[Trait("Category", "Unit")]
public class DisplayNameTests
{
    #region ExtractMaterialCode

    [Theory]
    [InlineData("metalbit-copper", "copper")]
    [InlineData("ingot-iron", "iron")]
    [InlineData("nugget-gold", "gold")]
    [InlineData("ore-nativecopper-limonite", "limonite")]
    public void ExtractMaterialCode_PathWithDash_ReturnsLastSegment(string path, string expected)
    {
        var result = AlloyCalculatorLogic.ExtractMaterialCode(path);

        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("game:metalbit-copper", "copper")]
    [InlineData("game:ingot-bronze", "bronze")]
    [InlineData("mymod:ore-custom-iron", "iron")]
    public void ExtractMaterialCode_PathWithDomain_RemovesDomainAndExtractsCode(string path, string expected)
    {
        var result = AlloyCalculatorLogic.ExtractMaterialCode(path);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ExtractMaterialCode_NoDash_ReturnsFullPath()
    {
        var result = AlloyCalculatorLogic.ExtractMaterialCode("copper");

        Assert.Equal("copper", result);
    }

    [Fact]
    public void ExtractMaterialCode_NullPath_ReturnsUnknown()
    {
        var result = AlloyCalculatorLogic.ExtractMaterialCode(null);

        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ExtractMaterialCode_EmptyPath_ReturnsUnknown()
    {
        var result = AlloyCalculatorLogic.ExtractMaterialCode("");

        Assert.Equal("unknown", result);
    }

    [Fact]
    public void ExtractMaterialCode_TrailingDash_ReturnsFullPath()
    {
        // Edge case: path ends with dash - no content after, return full path
        var result = AlloyCalculatorLogic.ExtractMaterialCode("metalbit-");

        // Returns full path since nothing after the dash
        Assert.Equal("metalbit-", result);
    }

    #endregion

    #region GetMaterialDisplayName

    [Fact]
    public void GetMaterialDisplayName_NullAssetLocation_ReturnsUnknown()
    {
        var result = AlloyCalculatorLogic.GetMaterialDisplayName(null);

        Assert.Equal("unknown", result);
    }

    // Note: Tests for GetMaterialDisplayName with valid AssetLocation are skipped
    // because Lang.GetMatching requires runtime initialization that's not available in unit tests.
    // The display name logic is tested indirectly through E2E tests.

    #endregion

    #region GetAlloyDisplayName / GetIngredientDisplayName

    [Fact]
    public void GetAlloyDisplayName_NullCode_ReturnsUnknown()
    {
        var result = AlloyCalculatorLogic.GetAlloyDisplayName(null);

        Assert.Equal("unknown", result);
    }

    [Fact]
    public void GetIngredientDisplayName_NullCode_ReturnsUnknown()
    {
        var result = AlloyCalculatorLogic.GetIngredientDisplayName(null);

        Assert.Equal("unknown", result);
    }

    // Note: Tests for GetAlloyDisplayName/GetIngredientDisplayName with valid codes
    // are skipped because they call Lang.GetMatching which requires runtime initialization.

    #endregion
}
