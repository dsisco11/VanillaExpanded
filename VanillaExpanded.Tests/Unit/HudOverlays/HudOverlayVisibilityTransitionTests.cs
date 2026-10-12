using VanillaExpanded.HudOverlays.Rendering;

namespace VanillaExpanded.Tests.Unit.HudOverlays;

/// <summary>Checks transition timing, reversal, and retained-resource release with a deterministic clock.</summary>
public sealed class HudOverlayVisibilityTransitionTests
{
    #region Public API
    /// <summary>Normal visibility fades smoothly and releases content only after the exit completes.</summary>
    [Fact]
    public void FadeRetainsContentUntilExitCompletes()
    {
        long now = 0;
        int releases = 0;
        var transition = new HudOverlayVisibilityTransition(clockMs: () => now);
        transition.Enable();
        transition.Show();
        Assert.Equal(0, transition.Opacity);
        now = 125;
        Assert.Equal(.5f, transition.Opacity);
        now = 250;
        Assert.Equal(1, transition.Opacity);
        transition.Hide(() => releases++);
        now = 375;
        Assert.True(transition.Renderable);
        Assert.Equal(.5f, transition.Opacity);
        Assert.Equal(0, releases);
        now = 500;
        Assert.False(transition.Renderable);
        Assert.Equal(0, transition.Opacity);
        Assert.Equal(1, releases);
    }

    /// <summary>A reversal keeps its current opacity and cancels the pending resource release.</summary>
    [Fact]
    public void ReappearingCancelsReleaseWithoutOpacityJump()
    {
        long now = 0;
        var transition = new HudOverlayVisibilityTransition(clockMs: () => now);
        transition.Enable(); transition.Show(); now = 250;
        transition.Hide(() => Assert.Fail("Reappearing content must remain prepared."));
        now = 375;
        float opacity = transition.Opacity;
        transition.Show();
        Assert.Equal(opacity, transition.Opacity);
        now = 625;
        Assert.Equal(1, transition.Opacity);
        Assert.True(transition.Renderable);
    }

    /// <summary>Preparation after an unobserved exit deadline cannot release newly supplied content.</summary>
    [Fact]
    public void ReappearingAfterDeadlineCancelsOldRelease()
    {
        long now = 0;
        var transition = new HudOverlayVisibilityTransition(clockMs: () => now);
        transition.Enable(); transition.Show(); now = 250;
        transition.Hide(() => Assert.Fail("The old release must not clear the new preparation."));
        now = 600;
        transition.Show();
        Assert.True(transition.Renderable);
        Assert.Equal(0, transition.Opacity);
    }

    /// <summary>Immediate owner cleanup cancels callbacks and leaves no retained drawable state.</summary>
    [Fact]
    public void ResetCancelsPendingExit()
    {
        long now = 0;
        var transition = new HudOverlayVisibilityTransition(clockMs: () => now);
        transition.Enable(); transition.Show(); now = 250;
        transition.Hide(() => Assert.Fail("Owner cleanup already released the content."));
        transition.Reset(); now = 1000;
        Assert.False(transition.Renderable);
        Assert.Equal(0, transition.Opacity);
    }

    /// <summary>Storm entry and exit have visible brightness reversals and finish in their requested state.</summary>
    [Fact]
    public void StormFlashesOnEntryAndExit()
    {
        long now = 0;
        var transition = new HudOverlayVisibilityTransition(flash: true, clockMs: () => now);
        transition.Enable(); transition.Show();
        now = 500; float bright = transition.Opacity;
        now = 750; Assert.True(transition.Opacity < bright);
        now = 1000; Assert.Equal(1, transition.Opacity);
        transition.Hide(() => { });
        now = 1250; float dim = transition.Opacity;
        now = 1500; Assert.True(transition.Opacity > dim);
        now = 2000; Assert.False(transition.Renderable);
    }
    #endregion
}
