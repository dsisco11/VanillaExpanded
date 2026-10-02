using System;
using System.Runtime.ExceptionServices;
using Vintagestory.API.Common;
using VanillaExpanded.AutoStashing.Targets;

namespace VanillaExpanded.AutoStashing;

/// <summary>Tracks applied or uncertain inventory mutations and preserves failures through target finalization and cleanup.</summary>
internal sealed class AutoStashMutationState
{
    private bool moved;
    private bool attemptPending;
    public bool NeedsFinalization => moved || attemptPending;

    #region Public API
    /// <summary>Records an engine call that may mutate inventory before throwing from a callback.</summary>
    public void BeginAttempt() => attemptPending = true;

    /// <summary>Records actual movement and clears uncertainty only after the engine call returns normally.</summary>
    public void CompleteAttempt(int quantity)
    {
        moved |= quantity > 0;
        attemptPending = false;
    }

    /// <summary>Finalizes possible changes before cleanup and retains the original failure when either boundary also throws.</summary>
    public void Finish(AutoStashTarget target, IPlayerInventoryManager owner, Exception? originalFailure, ILogger logger)
    {
        Exception? failure = originalFailure;
        try
        {
            if (NeedsFinalization) target.FinalizeChanges();
        }
        catch (Exception exception)
        {
            if (failure is null) failure = exception;
            else ReportSecondary(logger, "persist or synchronize", exception);
        }
        finally
        {
            try
            {
                target.Release(owner);
            }
            catch (Exception exception)
            {
                if (failure is null) failure = exception;
                else ReportSecondary(logger, "close the inventory session", exception);
            }
        }

        // An exception already unwinding the caller must keep its identity and stack.
        // Otherwise propagate the first finalization/cleanup failure with its original stack.
        if (originalFailure is null && failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
    #endregion

    #region Private
    /// <summary>Reports a secondary failure without allowing a broken logger to replace the primary exception.</summary>
    private static void ReportSecondary(ILogger logger, string action, Exception exception)
    {
        try
        {
            logger.Error("AutoStash could not {0} while handling another failure: {1}", action, exception);
        }
        catch (Exception)
        {
            // Logging is best-effort during failure recovery; the original operation failure remains authoritative.
        }
    }
    #endregion
}
