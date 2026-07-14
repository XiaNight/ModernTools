using Base.Framework.Utilities;

namespace ArmouryProtocol.Lighting;

/// <summary>
/// Per-model lighting data for the advanced-lighting feature.
///
/// A profile bundles everything that differs between keyboard models:
///   - the physical KLE layout file (parsed by <c>LayoutConverter</c>),
///   - the firmware-level light-key matrix (model specific), and
///   - the mapping from KLE layout labels to matrix key names.
///
/// The KLE layout labels come from https://www.keyboard-layout-editor.com/ and
/// are fixed by that tool, so the mapping lives here (per model) rather than in
/// the converter. Each supported model provides its own concrete profile;
/// register them in <see cref="KeyboardLightingProfiles"/>.
/// </summary>
public abstract class KeyboardLightingProfile
{
    /// <summary>Human-readable model name (also the registry key), e.g. "M708".</summary>
    public abstract string ModelName { get; }

    /// <summary>
    /// KLE layout file consumed by <c>LayoutConverter.Convert</c>, e.g. "M708.txt".
    /// Describes the physical key positions/labels, not the lighting matrix.
    /// </summary>
    public abstract string LayoutFileName { get; }

    /// <summary>
    /// Firmware light-key matrix. Indexed as [x, y]; an empty string marks a cell
    /// with no addressable key. This is defined by the model's firmware.
    /// </summary>
    public abstract string[,] MatrixLightKeyTable { get; }

    /// <summary>
    /// Maps a (trimmed) KLE layout label to its matrix key name.
    /// Concrete profiles supply this; callers go through <see cref="TryConvertToMatrixKey"/>.
    /// </summary>
    protected abstract IReadOnlyDictionary<string, string> LayoutToMatrix { get; }

    /// <summary>
    /// Translates a KLE layout label to its matrix key name.
    /// Returns false (instead of throwing) for unknown labels so callers can skip them.
    /// </summary>
    public bool TryConvertToMatrixKey(string layoutKey, out string matrixKey)
    {
        matrixKey = string.Empty;
        return layoutKey != null
            && LayoutToMatrix.TryGetValue(layoutKey.Trim(), out matrixKey);
    }

    /// <summary>
    /// Resolves a KLE layout label to its (x, y) cell in <see cref="MatrixLightKeyTable"/>.
    /// Returns false if the label is unknown or not present in the matrix.
    /// </summary>
    public bool TryGetMatrixPosition(string layoutKey, out Vector2Int position)
    {
        position = new Vector2Int(-1, -1);
        if (!TryConvertToMatrixKey(layoutKey, out string matrixKey))
            return false;

        position = FindMatrixPosition(MatrixLightKeyTable, matrixKey);
        return position.x >= 0 && position.y >= 0;
    }

    // Matrix is stored with x on the first axis and y on the second.
    private static Vector2Int FindMatrixPosition(string[,] matrixTable, string matrixKey)
    {
        for (int y = 0; y < matrixTable.GetLength(1); y++)
        {
            for (int x = 0; x < matrixTable.GetLength(0); x++)
            {
                if (matrixTable[x, y] == matrixKey)
                    return new Vector2Int(x, y);
            }
        }
        return new Vector2Int(-1, -1);
    }
}
