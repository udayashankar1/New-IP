using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class EnemyInteractionPrompt : MonoBehaviour
{
    [Header("Detection")]
    public float detectionRange = 5f;
    [Tooltip("Max degrees from camera centre — enemy must be inside this cone")]
    public float viewAngle = 60f;

    /// <summary>Camera angle to this system's best candidate (∞ if none). Read by the sibling prompt to keep only one Q visible.</summary>
    public float BestAngle { get; private set; } = Mathf.Infinity;

    Camera               _camera;
    DistractionInteractor _distractor;
    EnemyPatrol          _shownTarget;

    void Awake()
    {
        _camera     = Camera.main;
        _distractor = GetComponent<DistractionInteractor>();
    }

    void Update()
    {
        if (_camera == null) _camera = Camera.main;

        EnemyPatrol best = FindBestTarget(out float bestAngle);
        BestAngle = best != null ? bestAngle : Mathf.Infinity;

        // Single-prompt rule: the distraction prompt wins exact ties, so the
        // enemy prompt only shows when it is *strictly* closer to centre.
        float otherAngle = _distractor != null ? _distractor.BestAngle : Mathf.Infinity;
        EnemyPatrol toShow = (best != null && BestAngle < otherAngle) ? best : null;

        if (_shownTarget != toShow)
        {
            if (_shownTarget != null) _shownTarget.ShowQPrompt(false);
            _shownTarget = toShow;
        }

        if (_shownTarget != null)
        {
            _shownTarget.ShowQPrompt(true);

            if (Input.GetKeyDown(KeyCode.Q))
                _shownTarget.LureFromBehind();
        }
    }

    EnemyPatrol FindBestTarget(out float bestAngle)
    {
        var cols = Physics.OverlapSphere(transform.position, detectionRange);
        EnemyPatrol best = null;
        bestAngle = viewAngle;

        foreach (var col in cols)
        {
            var e = col.GetComponent<EnemyPatrol>()
                 ?? col.GetComponentInParent<EnemyPatrol>();

            if (e == null || !e.CanBeTakenDown || e.IsFPromptActive) continue;

            float a = CameraAngleTo(e);
            if (a >= bestAngle) continue;                       // not closer to centre than current best

            // Only focus an enemy you can actually see — one hidden behind another
            // body or a wall is skipped even if its angle is smaller.
            if (IsOccluded(e.transform.position + Vector3.up * 1.0f, e.transform)) continue;

            bestAngle = a;
            best = e;
        }
        return best;
    }

    float CameraAngleTo(EnemyPatrol e)
    {
        Vector3 toEnemy = (e.transform.position + Vector3.up * 1.0f) - _camera.transform.position;
        return Vector3.Angle(_camera.transform.forward, toEnemy);
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
