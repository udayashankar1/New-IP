using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Placed on a droppable "En Object". Shows a pooled Q prompt while the player
/// is aiming at it; pressing Q drops it (adds a Rigidbody so it falls). When it
/// lands it emits a noise that nearby enemies hear: the NEAREST hearer walks over
/// to investigate the landing spot, while every other hearer just glances toward
/// it and then resumes its patrol.
/// </summary>
[RequireComponent(typeof(Collider))]
public class Distraction : MonoBehaviour
{
    [Header("Prompt (pooled — same system as enemies)")]
    [Tooltip("Optional anchor the borrowed prompt is parented to. If null, this object is used.")]
    public Transform promptAnchor;
    [Tooltip("Local position of the Q prompt relative to the anchor (or this object).")]
    public Vector3 qPromptOffset = new Vector3(0f, 1.2f, 0f);

    [Header("Shared Config")]
    [Tooltip("ScriptableObject holding all shared tuning. Create via Assets ▸ Create ▸ Stealth ▸ Distraction Config, then assign the SAME asset to every distraction object.")]
    public DistractionConfig config;

    [Header("Table (object falls off the edge, away from the table centre)")]
    [Tooltip("The table this object rests on. Auto-detected as the nearest parent with a Collider when left empty.")]
    public Transform table;

    // ── Shared tuning (forwarded from DistractionConfig) ──
    float dropMass            => config.dropMass;
    float pushSpeed           => config.pushSpeed;
    float upKick              => config.upKick;
    float fallDelay           => config.fallDelay;
    float ghostStandoff       => config.ghostStandoff;
    float noiseRadiusOverride => config.noiseRadiusOverride;

    GameObject _qInstance;
    Rigidbody  _rb;
    bool       _dropped;
    bool       _noiseFired;
    Collider   _tableCollider;

    Transform PromptParent => promptAnchor != null ? promptAnchor : transform;

    void Awake()
    {
        if (config == null)
        {
            Debug.LogError($"Distraction on '{name}' has no DistractionConfig assigned — using runtime defaults.", this);
            config = ScriptableObject.CreateInstance<DistractionConfig>();
        }

        // Auto-detect the table = nearest ancestor that has a Collider.
        if (table == null)
        {
            for (Transform a = transform.parent; a != null; a = a.parent)
                if (a.GetComponent<Collider>() != null) { table = a; break; }
        }
        if (table != null) _tableCollider = table.GetComponent<Collider>();
    }

    // World centre of the table (collider bounds centre when available).
    Vector3 TableCenter => _tableCollider != null ? _tableCollider.bounds.center
                         : (table != null ? table.position : transform.position);

    // Horizontal direction from the table centre out to this object = the way it falls off.
    Vector3 OutwardDir()
    {
        Vector3 d = transform.position - TableCenter; d.y = 0f;
        return d.sqrMagnitude > 0.0001f ? d.normalized : transform.forward;
    }

    /// <summary>Still interactable until it has been dropped.</summary>
    public bool CanBeTriggered => !_dropped;

    public void ShowQPrompt(bool show)
    {
        if (show == (_qInstance != null)) return;        // already in the desired state

        var pool = GameManager.Instance.Prompts;
        if (show)
            _qInstance = pool != null ? pool.Acquire(PromptType.Q, PromptParent, qPromptOffset) : null;
        else
        {
            if (pool != null) pool.Release(_qInstance);
            _qInstance = null;
        }
    }

    /// <summary>
    /// Drop the object off the table. The fall direction and the Ghost's position are
    /// both derived from the table centre: the object flies off the nearest edge and the
    /// Ghost appears just beyond it, facing the object.
    /// </summary>
    public void Trigger()
    {
        if (_dropped) return;
        _dropped = true;
        ShowQPrompt(false);

        Vector3 outward = OutwardDir();

        // Ghost appears beyond the object (outward from the table) facing it, and calls immediately…
        var ghost = GameManager.Instance.Ghost;
        if (ghost != null)
        {
            Vector3 ghostPos = transform.position + outward * ghostStandoff;
            ghost.Summon(ghostPos, transform.position);
        }

        // …then the object falls after a delay so it can be synced with the animation.
        StartCoroutine(DropAfterDelay(outward));
    }

    IEnumerator DropAfterDelay(Vector3 outward)
    {
        if (fallDelay > 0f) yield return new WaitForSeconds(fallDelay);

        _rb = GetComponent<Rigidbody>();
        if (_rb == null) _rb = gameObject.AddComponent<Rigidbody>();
        _rb.isKinematic = false;
        _rb.useGravity  = true;
        _rb.mass        = dropMass;

        _rb.linearVelocity = outward * pushSpeed + Vector3.up * upKick;
    }

    void OnDisable() => ShowQPrompt(false);

    // First solid contact after the drop = it hit the ground → make noise.
    void OnCollisionEnter(Collision collision)
    {
        if (!_dropped || _noiseFired) return;

        // Ignore brushing against the player / enemies on the way down.
        if (collision.collider.GetComponentInParent<EnemyPatrol>()     != null) return;
        if (collision.collider.GetComponentInParent<PlayerController>() != null) return;

        _noiseFired = true;
        EmitNoise(collision.GetContact(0).point);
    }

    // Find every enemy that can hear the noise, then let the NEAREST investigate
    // while the rest only glance toward the spot.
    void EmitNoise(Vector3 spot)
    {
        var hearers = new List<EnemyPatrol>();
        foreach (var e in GameManager.Instance.Enemies)
        {
            if (e == null || !e.CanBeDistracted) continue;
            float range = noiseRadiusOverride > 0f ? noiseRadiusOverride : e.HearingRange;
            if (Vector3.Distance(e.transform.position, spot) <= range)
                hearers.Add(e);
        }
        if (hearers.Count == 0) return;

        EnemyPatrol nearest = null;
        float bestSqr = float.MaxValue;
        foreach (var e in hearers)
        {
            float d = (e.transform.position - spot).sqrMagnitude;
            if (d < bestSqr) { bestSqr = d; nearest = e; }
        }

        foreach (var e in hearers)
            e.NoticeDistraction(spot, investigate: e == nearest);
    }
}
