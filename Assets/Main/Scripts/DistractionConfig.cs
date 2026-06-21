using UnityEngine;

/// <summary>
/// Shared, designer-tunable settings for every droppable "En Object" distraction.
/// Create one asset (Assets ▸ Create ▸ Stealth ▸ Distraction Config) and assign the
/// SAME asset to every <see cref="Distraction"/>. Tweaking it retunes them all at once.
///
/// Per-object values (the table reference, prompt anchor/offset) stay on the
/// <see cref="Distraction"/> component itself — they are unique to each object.
/// </summary>
[CreateAssetMenu(fileName = "DistractionConfig", menuName = "Stealth/Distraction Config", order = 1)]
public class DistractionConfig : ScriptableObject
{
    [Header("Drop")]
    [Tooltip("Mass given to the object when it is dropped.")]
    public float dropMass = 5f;
    [Tooltip("Launch speed (m/s) outward from the table centre, so the object falls off the nearest edge.")]
    public float pushSpeed = 3.5f;
    [Tooltip("Small upward hop added to the launch so it clears the table top.")]
    public float upKick = 1f;
    [Tooltip("Seconds to wait after pressing Q before the object actually falls — lets you sync the drop with the Ghost's calling animation.")]
    public float fallDelay = 0.5f;

    [Header("Ghost")]
    [Tooltip("How far beyond the object — outward from the table centre — the Ghost appears.")]
    public float ghostStandoff = 0.9f;

    [Header("Noise")]
    [Tooltip("0 = each enemy uses its own EnemyConfig hearing range. " +
             "> 0 = every enemy within this fixed radius of the landing spot reacts instead.")]
    public float noiseRadiusOverride = 0f;
}
