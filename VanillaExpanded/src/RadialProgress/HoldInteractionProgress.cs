using System;

namespace VanillaExpanded.RadialProgress;

internal sealed class HoldInteractionProgress
{
    private readonly IProgressSystemProvider progressSystem;
    private readonly float gracePeriodSeconds;
    private readonly string text;

    public float ElapsedSeconds { get; private set; }
    public bool Completed { get; private set; }
    public IRadialProgressBar? ProgressBar { get; private set; }

    public HoldInteractionProgress(IProgressSystemProvider progressSystem, float gracePeriodSeconds, string text)
    {
        ArgumentNullException.ThrowIfNull(progressSystem);
        ArgumentNullException.ThrowIfNull(text);
        if (!float.IsFinite(gracePeriodSeconds) || gracePeriodSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(gracePeriodSeconds));
        }

        this.progressSystem = progressSystem;
        this.gracePeriodSeconds = gracePeriodSeconds;
        this.text = text;
    }

    public bool Advance(float deltaTime, float delaySeconds, bool rendersTopmost)
    {
        if (!float.IsFinite(deltaTime) || deltaTime < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(deltaTime));
        }

        return UpdateElapsed(ElapsedSeconds + deltaTime, delaySeconds, rendersTopmost);
    }

    // Engine interaction callbacks supply total elapsed time; tick-driven adapters supply deltas.
    public bool UpdateElapsed(float elapsedSeconds, float delaySeconds, bool rendersTopmost)
    {
        if (!float.IsFinite(elapsedSeconds) || elapsedSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
        }
        if (!float.IsFinite(delaySeconds) || delaySeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(delaySeconds));
        }
        if (Completed)
        {
            return false;
        }

        ElapsedSeconds = elapsedSeconds;
        if (ElapsedSeconds >= gracePeriodSeconds)
        {
            ProgressBar ??= progressSystem.CreateProgressBar(rendersTopmost);
            if (ProgressBar is not null)
            {
                ProgressBar.Progress = Math.Clamp(ElapsedSeconds / delaySeconds, 0f, 1f);
                ProgressBar.Text = text;
            }
        }

        if (ElapsedSeconds < delaySeconds)
        {
            return false;
        }

        Completed = true;
        RemoveProgressBar();
        return true;
    }

    public void Reset()
    {
        ElapsedSeconds = 0;
        Completed = false;
        RemoveProgressBar();
    }

    private void RemoveProgressBar()
    {
        IRadialProgressBar? progressBar = ProgressBar;
        if (progressBar is null)
        {
            return;
        }

        ProgressBar = null;
        progressSystem.RemoveProgressBar(progressBar);
    }
}
