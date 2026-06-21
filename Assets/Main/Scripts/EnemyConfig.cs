using UnityEngine;

/// <summary>
/// Shared, designer-tunable settings for every patrolling enemy.
/// Create one asset (Assets ▸ Create ▸ Stealth ▸ Enemy Config) and assign the
/// SAME asset to every EnemyPatrol. Tweaking it retunes all enemies at once.
///
/// Per-enemy values (patrol path, wait times, takedown point, prompt UIs) stay
/// on the EnemyPatrol component itself — they are unique to each enemy.
/// </summary>
[CreateAssetMenu(fileName = "EnemyConfig", menuName = "Stealth/Enemy Config", order = 0)]
public class EnemyConfig : ScriptableObject
{
    [Header("Movement")]
    public float moveSpeed     = 2f;
    public float reachDistance = 1.2f;

    [Header("Rotation")]
    public float rotationSmoothTime = 0.22f;
    public float maxTurnSpeed       = 140f;

    [Header("Idle Variations")]
    public string[] idleStateNames = { "Happy Idle", "Idle 2", "Idle 3" };

    [Header("Detection")]
    public float detectionRange      = 12f;
    public float fovAngle            = 90f;
    public float closeDetectionRange = 2.5f;
    public float eyeHeight           = 1.6f;

    [Header("Stealth — Crouch")]
    [Tooltip("Detection range multiplier when player is crouching (0.5 = half range)")]
    public float crouchRangeMultiplier = 0.5f;
    [Tooltip("Close-range zone multiplier when crouching")]
    public float crouchCloseMultiplier = 0.4f;
    [Tooltip("Raycast target height when player is crouching (hip-level obstacles block this)")]
    public float crouchHeadHeight      = 0.75f;
    [Tooltip("Raycast target height when player is standing")]
    public float standHeadHeight       = 1.6f;

    [Header("Suspicion")]
    [Tooltip("Cone distance for suspicion — should be larger than detectionRange")]
    public float suspicionRange      = 20f;
    [Tooltip("Cone angle for suspicion — wider than the detection FOV")]
    public float suspicionFOV        = 120f;
    [Tooltip("Seconds the player must stay inside the suspicion cone before the enemy reacts")]
    public float suspicionDuration   = 3f;
    [Tooltip("Seconds the enemy stands facing the last-seen direction before walking to the spot")]
    public float facingDuration      = 1f;
    [Tooltip("Seconds the enemy waits at the last-seen position before returning to patrol")]
    public float investigateDuration = 2f;

    [Header("Distraction (thrown / dropped objects)")]
    [Tooltip("How close a dropped distraction object must land for this enemy to notice the noise (sound radius, ignores line-of-sight).")]
    public float distractionHearingRange = 18f;
    [Tooltip("Seconds a non-investigating enemy stands looking at the distraction spot before returning to patrol.")]
    public float distractionGlanceDuration = 1.5f;
    [Tooltip("Seconds the NEAREST enemy spends inspecting the fallen distraction object before returning to patrol.")]
    public float distractionInvestigateDuration = 5f;
    [Tooltip("How far behind the enemy the Ghost appears when the player presses Q on that enemy.")]
    public float lureGhostDistance = 1.5f;
    [Tooltip("Seconds after the Ghost appears before the enemy turns around — lets you sync the turn with the calling animation.")]
    public float lureTurnDelay = 0.5f;

    [Header("Detection Gizmos")]
    public Color fovColor       = new Color(1f, 1f, 0f,    0.08f);
    public Color fovAlertColor  = new Color(1f, 0.15f, 0f, 0.18f);
    public Color edgeColor      = new Color(1f, 1f, 0f,    0.85f);
    public Color edgeAlertColor = new Color(1f, 0.2f, 0f,  1.00f);
}
