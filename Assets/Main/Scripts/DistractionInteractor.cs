using UnityEngine;

/// <summary>
/// Player-side scanner for droppable "En Object" distractions. Mirrors
/// <see cref="EnemyInteractionPrompt"/>: shows a pooled Q prompt over the best
/// distraction inside a view cone + range, and drops it when Q is pressed.
///
/// Only ONE Q prompt is ever visible at a time. This component and the sibling
/// <see cref="EnemyInteractionPrompt"/> each publish their best candidate's
/// camera angle via <see cref="BestAngle"/>; whichever is closer to screen
/// centre wins (distractions win exact ties). The dropped object is launched in
/// the direction it sits on screen — far left → flies left, centre → straight down.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class DistractionInteractor : MonoBehaviour
{
    [Header("Detection")]
    public float detectionRange = 5f;
    [Tooltip("Max degrees from camera centre — object must be inside this cone")]
    public float viewAngle = 60f;
    [Tooltip("Height above the object pivot the camera aims at for the angle test.")]
    public float aimHeight = 0.8f;

    /// <summary>Camera angle to this system's best candidate (∞ if none). Read by the sibling prompt to keep only one Q visible.</summary>
    public float BestAngle { get; private set; } = Mathf.Infinity;

    Camera                 _camera;
    EnemyInteractionPrompt _enemyPrompt;
    Distraction            _shownTarget;

    void Awake()
    {
        _camera      = Camera.main;
        _enemyPrompt = GetComponent<EnemyInteractionPrompt>();
    }

    void Update()
    {
        if (_camera == null) _camera = Camera.main;

        Distraction best = FindBestTarget(out float bestAngle);
        BestAngle = best != null ? bestAngle : Mathf.Infinity;

        // Single-prompt rule: defer to the enemy prompt only if it is strictly
        // closer to centre (distractions win exact ties).
        float otherAngle = _enemyPrompt != null ? _enemyPrompt.BestAngle : Mathf.Infinity;
        Distraction toShow = (best != null && BestAngle <= otherAngle) ? best : null;

        if (_shownTarget != toShow)
        {
            if (_shownTarget != null) _shownTarget.ShowQPrompt(false);
            _shownTarget = toShow;
        }

        if (_shownTarget != null)
        {
            _shownTarget.ShowQPrompt(true);

            if (Input.GetKeyDown(KeyCode.Q))
                _shownTarget.Trigger();
        }
    }

    Distraction FindBestTarget(out float bestAngle)
    {
        var cols = Physics.OverlapSphere(transform.position, detectionRange);
        Distraction best = null;
        bestAngle = viewAngle;

        foreach (var col in cols)
        {
            var d = col.GetComponent<Distraction>()
                 ?? col.GetComponentInParent<Distraction>();

            if (d == null || !d.CanBeTriggered) continue;

            float a = CameraAngleTo(d);
            if (a >= bestAngle) continue;                       // not closer to centre than current best

            // Only the object you can actually SEE gets focus — an object hidden
            // behind another (or a wall) is skipped even if its angle is smaller.
            if (IsOccluded(d.transform.position + Vector3.up * aimHeight, d.transform)) continue;

            bestAngle = a;
            best = d;
        }
        return best;
    }

    float CameraAngleTo(Distraction d)
    {
        Vector3 to = (d.transform.position + Vector3.up * aimHeight) - _camera.transform.position;
        return Vector3.Angle(_camera.transform.forward, to);
    }

    // True if anything solid (other than the candidate itself or the player) sits
    // between the camera and the candidate — i.e. the candidate is behind something.
    bool IsOccluded(Vector3 targetPoint, Transform self)
    {
        Vector3 from = _camera.transform.position;
        Vector3 dir  = targetPoint - from;
        float   dist = dir.magnitude;
        if (dist < 0.05f) return false;

        foreach (var h in Physics.RaycastAll(from, dir / dist, dist - 0.1f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (h.collider.transform == self || h.collider.transform.IsChildOf(self)) continue;
            if (h.collider.GetComponentInParent<PlayerController>() != null)           continue;
            return true;
        }
        return false;
    }

    void OnDisable()
    {
        if (_shownTarget != null) { _shownTarget.ShowQPrompt(false); _shownTarget = null; }
    }
}
