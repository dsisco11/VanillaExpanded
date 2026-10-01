namespace VanillaExpanded.Tests.Unit.AutoStashing.Support;

/// <summary>Records lifecycle callbacks and controls tick delivery without invoking a game loop.</summary>
internal sealed class AutoStashObservation : IDisposable
{
    private readonly List<Action> cleanup = [];
    private Action<float>? tick;
    public List<string> Events { get; } = [];

    #region Public API
    /// <summary>Records a lifecycle event, such as opening, closing, persistence, or dirty notification.</summary>
    public void Record(string name) => Events.Add(name);

    /// <summary>Captures a listener callback and its exact unregister action for deterministic tick tests.</summary>
    public void RegisterTick(Action<float> listener, Action unregister)
    {
        if (tick is not null) throw new InvalidOperationException("A tick listener is already registered.");
        tick = listener;
        cleanup.Add(unregister);
        Record("register");
    }

    /// <summary>Delivers one tick to the captured listener.</summary>
    public void Tick(float elapsed)
    {
        if (tick is null) throw new InvalidOperationException("No tick listener is registered.");
        tick(elapsed);
    }

    /// <summary>Registers cleanup for test-owned patches, configuration, or callback subscriptions.</summary>
    public void OnDispose(Action action) => cleanup.Add(action);

    /// <summary>Releases owned resources in reverse order, attempting all cleanup even when an action fails.</summary>
    public void Dispose()
    {
        List<Exception> errors = [];
        foreach (Action action in cleanup.AsEnumerable().Reverse())
        {
            try { action(); }
            catch (Exception error) { errors.Add(error); }
        }
        cleanup.Clear();
        tick = null;
        if (errors.Count > 0) throw new AggregateException(errors);
    }
    #endregion
}
