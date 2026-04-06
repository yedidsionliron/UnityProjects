using UnityEngine;

public enum DivertSide { Left, Right }

/// <summary>
/// Represents a single sort destination (one gaylord on one side of the belt).
/// Created by DiverterConfig.Build() at edit time.
/// Address range is baked in by AddressInit and survives play-mode entry.
/// </summary>
public class SortPoint : MonoBehaviour
{
    public int       id;
    public DivertSide side;

    [HideInInspector] public int addressMin;
    [HideInInspector] public int addressMax;

    public bool Contains(int address) => address >= addressMin && address <= addressMax;
}
