using Moq;
using VanillaExpanded.ItemRendering;
using Vintagestory.API.Client;

namespace VanillaExpanded.Tests.Unit.ItemRendering;

/// <summary>Exercises the production uniform snapshot algorithm against nested and exceptional mutations.</summary>
public sealed class ToolHeadShaderUniformStateTests
{
    #region Public API
    /// <summary>Snapshots clone backend values, restore inner then outer callers, and dispose idempotently.</summary>
    [Fact]
    public void NestedAndExceptionalMutationsRestoreExactCallerValues()
    {
        var shader = new Mock<IShaderProgram>();
        shader.Setup(s => s.HasUniform(It.IsAny<string>())).Returns(true);
        var backend = new Dictionary<string,float[]>();
        var initial = new Dictionary<string,float[]>();
        int writes = 0;
        float[] Read(string name, int count, bool integer)
        {
            if (!backend.TryGetValue(name,out var value))
            {
                value = Enumerable.Range(0,count).Select(i => (float)(backend.Count * 10 + i + 1)).ToArray();
                backend[name] = value;
                initial[name] = (float[])value.Clone();
            }
            return value;
        }
        void Write(string name, bool integer, float[] values) { backend[name] = (float[])values.Clone(); writes++; }
        using var outer = new ToolHeadShaderUniformState(shader.Object, Read, Write);
        foreach (var value in backend.Values) Array.Fill(value, 123);
        var enclosing = backend.ToDictionary(pair => pair.Key,pair => (float[])pair.Value.Clone());
        Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var inner = new ToolHeadShaderUniformState(shader.Object, Read, Write);
            foreach (var value in backend.Values) Array.Fill(value, 456);
            throw new InvalidOperationException();
        }));
        foreach (var pair in enclosing) Assert.Equal(pair.Value, backend[pair.Key]);
        outer.Dispose();
        foreach (var pair in initial) Assert.Equal(pair.Value, backend[pair.Key]);
        int completedWrites = writes;
        outer.Dispose();
        Assert.Equal(completedWrites, writes);
        Assert.Contains("modelMatrix",initial.Keys);
        Assert.Contains("rgbaGlowIn",initial.Keys);
        Assert.Contains("damageEffect",initial.Keys);
        Assert.Contains("overlayOpacity",initial.Keys);
        Assert.Contains("applyAnimation",initial.Keys);
    }
    #endregion
}
