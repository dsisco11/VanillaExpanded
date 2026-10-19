using System;
using System.Collections.Generic;
using System.Linq;

namespace VanillaExpanded.EcosystemMap.Storage;

/// <summary>Immutable semantic surface categories: zero is unknown, one is land/non-ocean, two is ocean.</summary>
internal sealed class OceanMask
{
    internal IReadOnlyList<byte> Columns { get; }
    internal int Size { get; }

    #region Public API
    /// <summary>Copies validated categories independently of terrain height or habitat eligibility.</summary>
    internal OceanMask(int size, IEnumerable<byte> columns)
    {
        byte[] copy = columns.ToArray();
        if (size <= 0 || (long)size * size != copy.Length || copy.Any(value => value > 2))
            throw new ArgumentException("Invalid ocean classification grid.", nameof(columns));
        Size = size;
        Columns = Array.AsReadOnly(copy);
    }
    #endregion
}
