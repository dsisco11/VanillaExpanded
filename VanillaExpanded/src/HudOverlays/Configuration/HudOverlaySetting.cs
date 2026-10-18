using System;

namespace VanillaExpanded.HudOverlays.Configuration;

/// <summary>Reads authoritative configuration on demand and commits actual changes through its owner.</summary>
public sealed class HudOverlaySetting<T>
{
    private readonly Func<T> read;
    private readonly Action<T> write;
    private readonly Action commit;

    #region Public API
    /// <summary>Binds a live getter, configuration setter, and persistence/reload operation.</summary>
    public HudOverlaySetting(Func<T> read, Action<T> write, Action commit)
    {
        this.read = read ?? throw new ArgumentNullException(nameof(read));
        this.write = write ?? throw new ArgumentNullException(nameof(write));
        this.commit = commit ?? throw new ArgumentNullException(nameof(commit));
    }

    /// <summary>Returns the current configured value, including external configuration edits.</summary>
    public T Value => read();

    /// <summary>Commits a changed value; restores configuration if persistence fails.</summary>
    public void Set(T value)
    {
        T previous = read();
        if (System.Collections.Generic.EqualityComparer<T>.Default.Equals(previous, value)) return;
        // Keep the in-memory source consistent with storage when a save operation throws.
        write(value);
        try { commit(); }
        catch { write(previous); throw; }
    }
    #endregion
}
