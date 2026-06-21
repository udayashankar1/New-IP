using UnityEngine;

/// <summary>
/// Non-generic base shared by every player-side interaction scanner. Holds the
/// serialized tuning (so Unity serialises it reliably) plus the single-prompt
/// arbitration state that lets sibling interactors cooperate: only ONE Q prompt is
/// ever visible at a time, and whichever candidate is closer to screen centre wins.
/// </summary>
public abstract class PlayerInteractorBase : MonoBehaviour
{
    [Header("Detection")]
    public float detectionRange = 5f;
    [Tooltip("Max degrees from camera centre — the target must sit inside this cone.")]
    public float viewAngle = 60f;
    [Tooltip("Height above the target pivot the camera aims at for the angle / occlusion test.")]
    public float aimHeight = 1f;

    /// <summary>Camera angle to this scanner's best candidate (∞ if none). Read by the sibling scanner to keep only one prompt visible.</summary>
    public float BestAngle { get; protected set; } = Mathf.Infinity;

    /// <summary>When two scanners tie on angle, does THIS one win? (Stops both prompts flickering on an exact tie.)</summary>
    protected abstract bool WinsAngleTie { get; }

    PlayerInteractorBase _sibling;

    protected Camera Camera => GameManager.Instance.MainCamera;

    protected virtual void Awake()
    {
        // The other interaction scanner on the player (if any) — its BestAngle is
        // what we arbitrate against.
        foreach (var other in GetComponents<PlayerInteractorBase>())
            if (other != this) { _sibling = other; break; }
    }

    /// <summary>True when this scanner's candidate should take the shared prompt slot.</summary>
    protected bool BeatsSibling()
    {
        float otherAngle = _sibling != null ? _sibling.BestAngle : Mathf.Infinity;
        return WinsAngleTie ? BestAngle <= otherAngle : BestAngle < otherAngle;
    }
}

/// <summary>
/// Generic player-side interaction scanner. Each frame it picks the best visible
/// <typeparamref name="T"/> inside a view-cone + range, shows its prompt while it is
/// the chosen candidate, and fires <see cref="Activate"/> when Q is pressed.
///
/// Concrete scanners (<see cref="EnemyInteractionPrompt"/>,
/// <see cref="DistractionInteractor"/>) only supply the small differences:
/// what counts as interactable, how to toggle its prompt, and what Q does.
/// </summary>
public abstract class PlayerInteractor<T> : PlayerInteractorBase where T : Component
{
    T _shown;

    // ── Per-type policy supplied by subclasses ──────────────────────────────────
    /// <summary>Is this candidate currently interactable?</summary>
    protected abstract bool CanInteract(T target);
    /// <summary>Show / hide the prompt over a candidate.</summary>
    protected abstract void ShowPrompt(T target, bool show);
    /// <summary>What pressing Q does to the chosen candidate.</summary>
    protected abstract void Activate(T target);

    void Update()
    {
        if (Camera == null) return;

        T best = FindBestTarget(out float bestAngle);
        BestAngle = best != null ? bestAngle : Mathf.Infinity;

        // Take the shared prompt slot only if our candidate beats the sibling's.
        T toShow = (best != null && BeatsSibling()) ? best : null;

        if (!ReferenceEquals(_shown, toShow))
        {
            if (_shown != null) ShowPrompt(_shown, false);
            _shown = toShow;
        }

        if (_shown != null)
        {
            ShowPrompt(_shown, true);
            if (Input.GetKeyDown(KeyCode.Q)) Activate(_shown);
        }
    }

    // Best = interactable, visible, and closest to screen centre within the cone.
    T FindBestTarget(out float bestAngle)
    {
        Vector3 camPos = Camera.transform.position;
        Vector3 camFwd = Camera.transform.forward;

        T best = null;
        bestAngle = viewAngle;

        foreach (var col in Physics.OverlapSphere(transform.position, detectionRange))
        {
            T target = col.GetComponent<T>() ?? col.GetComponentInParent<T>();
            if (target == null || !CanInteract(target)) continue;

            Vector3 aim   = target.transform.position + Vector3.up * aimHeight;
            float   angle = Vector3.Angle(camFwd, aim - camPos);
            if (angle >= bestAngle) continue;                       // not closer to centre than current best

            // Skip anything you can't actually see (behind another body / a wall).
            if (StealthUtils.IsOccluded(Camera, aim, target.transform)) continue;

            bestAngle = angle;
            best = target;
        }
        return best;
    }

    protected virtual void OnDisable()
    {
        if (_shown != null) { ShowPrompt(_shown, false); _shown = null; }
    }
}
