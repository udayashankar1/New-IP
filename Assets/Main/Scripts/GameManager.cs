using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central service-locator for the whole game. A single point of truth for the
/// shared, cross-cutting references that scattered scripts used to fetch on their
/// own (<c>Camera.main</c>, <c>FindObjectOfType</c>, <c>FindGameObjectWithTag</c>,
/// the <see cref="PromptPool"/> / <see cref="GhostCaller"/> singletons).
///
/// Usage from anywhere:
/// <code>
///     GameManager.Instance.MainCamera
///     GameManager.Instance.Player          // PlayerController
///     GameManager.Instance.Prompts         // PromptPool
///     GameManager.Instance.Ghost           // GhostCaller
///     GameManager.Instance.Enemies         // live enemy registry
/// </code>
///
/// Setup is optional: drop a GameManager on a scene object and (optionally) wire the
/// references in the inspector. If none exists, one is created automatically the first
/// time it is accessed, and every reference is resolved lazily. Either way the rest of
/// the codebase never touches <c>Camera.main</c> / <c>Find*</c> directly again.
/// </summary>
[DisallowMultipleComponent]
public class GameManager : MonoBehaviour
{
    static GameManager _instance;

    /// <summary>
    /// Runs automatically at game start (right after the first scene loads) so the
    /// manager is alive from the very beginning — every system can rely on it existing
    /// without anyone having to access <see cref="Instance"/> first. If the scene already
    /// contains a GameManager it is used; otherwise one is created.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Bootstrap()
    {
        if (_instance == null)
        {
            _instance = FindAnyObjectByType<GameManager>();
            if (_instance == null)
                _instance = new GameObject("GameManager").AddComponent<GameManager>();
        }
    }

    /// <summary>The active manager (guaranteed to exist from startup via <see cref="Bootstrap"/>).</summary>
    public static GameManager Instance
    {
        get
        {
            if (_instance == null) Bootstrap();   // safety net for edit-mode / early calls
            return _instance;
        }
    }

    /// <summary>True when a manager already exists — use to avoid auto-creating one during teardown.</summary>
    public static bool Exists => _instance != null;

    // ── Inspector overrides (optional — left empty = auto-resolved at runtime) ──
    [Header("Scene References (optional — auto-resolved if left empty)")]
    [SerializeField] Camera           _mainCamera;
    [SerializeField] PlayerController _player;
    [SerializeField] PromptPool       _prompts;
    [SerializeField] GhostCaller      _ghost;

    readonly List<EnemyPatrol> _enemies = new List<EnemyPatrol>();

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
    }

    void OnDestroy() { if (_instance == this) _instance = null; }

    // ── Lazily-resolved shared references ──────────────────────────────────────
    // Each getter caches the first non-null lookup, so order of script execution
    // never matters and the cost is paid at most once.

    /// <summary>The gameplay camera (falls back to <c>Camera.main</c>).</summary>
    public Camera MainCamera => _mainCamera != null ? _mainCamera : (_mainCamera = Camera.main);

    /// <summary>The player controller in the scene.</summary>
    public PlayerController Player => _player != null ? _player : (_player = FindAnyObjectByType<PlayerController>());

    /// <summary>Convenience accessor for the player's transform (null if no player).</summary>
    public Transform PlayerTransform => Player != null ? Player.transform : null;

    /// <summary>The pooled Q / F interaction-prompt provider.</summary>
    public PromptPool Prompts => _prompts != null ? _prompts : (_prompts = PromptPool.Instance ?? FindAnyObjectByType<PromptPool>());

    /// <summary>The summonable Ghost companion.</summary>
    public GhostCaller Ghost => _ghost != null ? _ghost : (_ghost = GhostCaller.Instance ?? FindAnyObjectByType<GhostCaller>());

    // ── Enemy registry ─────────────────────────────────────────────────────────
    // Enemies register themselves on Start and unregister on disable, so systems
    // like Distraction can iterate the live set without a per-event FindObjectsOfType.

    /// <summary>Add an enemy to the live registry (called by <see cref="EnemyPatrol"/>).</summary>
    public void RegisterEnemy(EnemyPatrol enemy)
    {
        if (enemy != null && !_enemies.Contains(enemy)) _enemies.Add(enemy);
    }

    /// <summary>Remove an enemy from the live registry (called when it is disabled / taken down).</summary>
    public void UnregisterEnemy(EnemyPatrol enemy) => _enemies.Remove(enemy);

    /// <summary>
    /// Every enemy currently alive in the scene. Seeds itself from a one-off scan
    /// the first time it is read empty, so it works even before any enemy has
    /// registered.
    /// </summary>
    public IReadOnlyList<EnemyPatrol> Enemies
    {
        get
        {
            if (_enemies.Count == 0) _enemies.AddRange(FindObjectsOfType<EnemyPatrol>());
            return _enemies;
        }
    }
}
