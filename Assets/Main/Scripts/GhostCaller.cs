using System.Collections;
using UnityEngine;

/// <summary>
/// Summonable "Ghost" companion. Hidden until <see cref="Summon"/> is called:
/// it then appears at a spot facing a target, plays the Calling animation once,
/// and vanishes again.
///
/// Lives on the Ghost GameObject and keeps it active so the routine can run —
/// visibility is toggled via its renderers, not SetActive.
/// </summary>
public class GhostCaller : MonoBehaviour
{
    public static GhostCaller Instance { get; private set; }

    [Header("Animation")]
    public string callStateName = "Calling";
    public string idleStateName = "Idle";
    [Tooltip("Crossfade time into the Calling state.")]
    public float fadeIn = 0.05f;
    [Tooltip("Extra seconds to linger after the Calling clip finishes before vanishing.")]
    public float lingerAfterCall = 0.15f;
    [Tooltip("Safety cap so the ghost always vanishes even if the clip loops.")]
    public float maxCallSeconds = 6f;

    [Header("Placement")]
    [Tooltip("Snap the ghost down onto the ground under the spawn point.")]
    public bool snapToGround = true;
    public LayerMask groundMask = ~0;

    [Header("Smoke FX")]
    [Tooltip("Magic-smoke particle prefab spawned at the ghost both when it appears and when it disappears.")]
    public GameObject smokePrefab;
    [Tooltip("Vertical offset for the smoke burst (raise it toward the body's centre).")]
    public float smokeHeight = 1f;

    Animator   _anim;
    Renderer[] _renderers;
    Coroutine  _routine;

    void Awake()
    {
        Instance   = this;
        _anim      = GetComponentInChildren<Animator>(true);
        _renderers = GetComponentsInChildren<Renderer>(true);
        SetVisible(false);                                  // hidden until summoned
    }

    void OnDestroy() { if (Instance == this) Instance = null; }

    /// <summary>Appear at <paramref name="spawnPos"/> facing <paramref name="faceTarget"/>, play Calling once, then vanish.</summary>
    public void Summon(Vector3 spawnPos, Vector3 faceTarget)
    {
        Vector3 spawn = spawnPos;

        if (snapToGround &&
            Physics.Raycast(spawn + Vector3.up * 3f, Vector3.down, out var hit, 12f, groundMask, QueryTriggerInteraction.Ignore))
            spawn.y = hit.point.y;

        transform.position = spawn;

        // Always face the object — never the player/camera.
        Vector3 face = faceTarget - spawn; face.y = 0f;
        if (face.sqrMagnitude > 0.0001f) transform.rotation = Quaternion.LookRotation(face.normalized);

        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(CallRoutine());
    }

    IEnumerator CallRoutine()
    {
        PlaySmoke();                                        // puff in
        SetVisible(true);

        if (_anim != null)
        {
            _anim.CrossFadeInFixedTime(callStateName, fadeIn, 0);

            // Wait until we're actually in the Calling state…
            float t = 0f;
            while (t < 1f && !_anim.GetCurrentAnimatorStateInfo(0).IsName(callStateName))
            { t += Time.deltaTime; yield return null; }

            // …then until one full play-through (normalizedTime is cumulative, so
            // this fires after one cycle even if the clip loops). Capped for safety.
            t = 0f;
            while (t < maxCallSeconds)
            {
                t += Time.deltaTime;
                var info = _anim.GetCurrentAnimatorStateInfo(0);
                if (info.IsName(callStateName) && info.normalizedTime >= 1f) break;
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(2f);
        }

        if (lingerAfterCall > 0f) yield return new WaitForSeconds(lingerAfterCall);

        PlaySmoke();                                        // puff out
        SetVisible(false);                                  // disappear
        if (_anim != null) _anim.Play(idleStateName, 0, 0f);
        _routine = null;
    }

    void SetVisible(bool v)
    {
        if (_renderers == null) return;
        foreach (var r in _renderers) if (r != null) r.enabled = v;
    }

    // Spawn a one-shot magic-smoke burst at the ghost and clean it up after it finishes.
    void PlaySmoke()
    {
        if (smokePrefab == null) return;

        var fx = Instantiate(smokePrefab, transform.position + Vector3.up * smokeHeight, Quaternion.identity);
        float life = 3f;
        var ps = fx.GetComponentInChildren<ParticleSystem>();
        if (ps != null)
        {
            var m = ps.main;
            life = m.duration + m.startLifetime.constantMax + 0.5f;
            if (!m.playOnAwake) ps.Play(true);
        }
        Destroy(fx, life);
    }
}
