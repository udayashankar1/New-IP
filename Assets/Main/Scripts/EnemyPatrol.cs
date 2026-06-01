using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class EnemyPatrol : MonoBehaviour
{
    [Header("Patrol")]
    public PatrolPath patrolPath;
    public float moveSpeed     = 2f;
    public float reachDistance = 1.2f;

    [Header("Rotation")]
    public float rotationSmoothTime = 0.22f;
    public float maxTurnSpeed       = 140f;

    [Header("Waypoint Wait")]
    public float waitTime    = 0f;
    public float endWaitTime = 8f;

    [Header("Idle Variations")]
    public string[] idleStateNames = { "Happy Idle", "Idle 2", "Idle 3" };

    [Header("Detection")]
    public float detectionRange      = 12f;
    public float fovAngle            = 90f;
    public float closeDetectionRange = 2.5f;
    public float eyeHeight           = 1.6f;

    [Header("Stealth — Crouch")]
    [Tooltip("Detection range multiplier when player is crouching (0.5 = half range)")]
    public float crouchRangeMultiplier  = 0.5f;
    [Tooltip("Close-range zone multiplier when crouching")]
    public float crouchCloseMultiplier  = 0.4f;
    [Tooltip("Raycast target height when player is crouching (hip-level obstacles block this)")]
    public float crouchHeadHeight       = 0.75f;
    [Tooltip("Raycast target height when player is standing")]
    public float standHeadHeight        = 1.6f;

    [Header("Takedown")]
    public Transform takedownPoint;

    [Header("Detection Gizmos")]
    public Color fovColor      = new Color(1f, 1f, 0f,   0.08f);
    public Color fovAlertColor = new Color(1f, 0.15f, 0f, 0.18f);
    public Color edgeColor     = new Color(1f, 1f, 0f,   0.85f);
    public Color edgeAlertColor= new Color(1f, 0.2f, 0f, 1.00f);

    // ── Runtime state ──────────────────────────────────────────
    CharacterController _cc;
    Animator            _anim;
    Transform           _player;
    PlayerController    _playerCtrl;

    int   _waypointIndex;
    int   _direction = 1;
    float _waitTimer;
    bool  _waiting;
    float _verticalSpeed;
    float _yawVelocity;

    bool  _playerInSight;

    enum Phase { Patrolling, Spotted, TakenDown }
    Phase _phase = Phase.Patrolling;

    public bool CanBeTakenDown => _phase == Phase.Patrolling || _phase == Phase.Spotted;

    static readonly int HashSpeed   = Animator.StringToHash("Speed");
    static readonly int HashSpotted = Animator.StringToHash("Spotted");

    // ── Init ───────────────────────────────────────────────────
    void Start()
    {
        _cc   = GetComponent<CharacterController>();
        _anim = GetComponent<Animator>();
        _anim.applyRootMotion = false;

        var playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
        {
            _player     = playerGO.transform;
            _playerCtrl = playerGO.GetComponent<PlayerController>();
        }
        else Debug.LogWarning("EnemyPatrol: no GameObject with tag 'Player' found.");

        _waypointIndex = NearestWaypointIndex();
    }

    // ── Main loop ──────────────────────────────────────────────
    void Update()
    {
        if (_phase == Phase.TakenDown) return;

        ApplyGravity();

        switch (_phase)
        {
            case Phase.Patrolling:
                _playerInSight = CanSeePlayer();
                if (_playerInSight) { OnPlayerSpotted(); return; }
                HandlePatrol();
                break;

            case Phase.Spotted:
                HandleSpottedPhase();
                break;
        }
    }

    // ── Detection ──────────────────────────────────────────────
    bool CanSeePlayer()
    {
        if (_player == null) return false;

        bool  crouching   = _playerCtrl != null && _playerCtrl.IsCrouching;

        // Effective ranges shrink when player is crouching
        float effectiveRange = detectionRange      * (crouching ? crouchRangeMultiplier : 1f);
        float effectiveClose = closeDetectionRange * (crouching ? crouchCloseMultiplier : 1f);

        // Raycast target drops to crouch head when player is crouching —
        // hip-level obstacles physically block the ray without any extra logic
        float   targetHeight = crouching ? crouchHeadHeight : standHeadHeight;
        Vector3 eyePos       = transform.position + Vector3.up * eyeHeight;
        Vector3 playerHead   = _player.position   + Vector3.up * targetHeight;
        Vector3 toPlayer     = playerHead - eyePos;
        float   dist         = toPlayer.magnitude;

        // Close-range zone: always detect (even from behind), reduced when crouching
        if (dist <= effectiveClose) return true;

        // Range gate
        if (dist > effectiveRange) return false;

        // FOV gate
        if (Vector3.Angle(transform.forward, toPlayer.normalized) > fovAngle * 0.5f) return false;

        // Line-of-sight: blocked by anything that isn't the player
        // When crouching + obstacle at hip level, this ray hits the obstacle → returns false
        if (Physics.Raycast(eyePos, toPlayer.normalized, out RaycastHit hit, dist))
            if (!hit.transform.IsChildOf(_player)) return false;

        return true;
    }

    void OnPlayerSpotted()
    {
        _phase = Phase.Spotted;
        _waiting = false;
        _anim.SetFloat(HashSpeed, 0f);
        _anim.SetTrigger(HashSpotted);

        // Snap to face player immediately
        Vector3 dir = _player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    void HandleSpottedPhase()
    {
        _anim.SetFloat(HashSpeed, 0f);

        // Once Angry Point finishes, return to patrol
        var info = _anim.GetCurrentAnimatorStateInfo(0);
        if (!_anim.IsInTransition(0) && info.IsName("Angry Point") && info.normalizedTime >= 0.9f)
        {
            _phase         = Phase.Patrolling;
            _playerInSight = false;
            _waypointIndex = NearestWaypointIndex();
        }
    }

    // ── Patrol ─────────────────────────────────────────────────
    void HandlePatrol()
    {
        if (patrolPath == null || patrolPath.Count == 0)
        {
            _anim.SetFloat(HashSpeed, 0f, 0.15f, Time.deltaTime);
            return;
        }

        if (_waiting)
        {
            _anim.SetFloat(HashSpeed, 0f, 0.08f, Time.deltaTime);
            _waitTimer -= Time.deltaTime;
            if (_waitTimer <= 0f) _waiting = false;
            return;
        }

        Transform target   = patrolPath.GetWaypoint(_waypointIndex);
        Vector3   toTarget = target.position - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude <= reachDistance)
        {
            _anim.SetFloat(HashSpeed, 0f, 0.08f, Time.deltaTime);
            int next = _waypointIndex + _direction;

            if (next >= patrolPath.Count || next < 0)
            {
                _direction = -_direction;
                next       = _waypointIndex + _direction;
                if (endWaitTime > 0f) { _waiting = true; _waitTimer = endWaitTime; PlayRandomIdle(); }
            }
            else if (waitTime > 0f) { _waiting = true; _waitTimer = waitTime; PlayRandomIdle(); }

            _waypointIndex = next;
            return;
        }

        // Rotate — SmoothDampAngle for organic GTA-style turn
        Vector3 dir      = toTarget.normalized;
        float targetYaw  = Quaternion.LookRotation(dir).eulerAngles.y;
        float newYaw     = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw,
                               ref _yawVelocity, rotationSmoothTime, maxTurnSpeed);
        transform.rotation = Quaternion.Euler(0f, newYaw, 0f);

        // Move in facing direction — arcs through turns naturally
        float angleOff  = Vector3.Angle(transform.forward, dir);
        float speedMult = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(angleOff / 90f));
        _cc.Move(transform.forward * moveSpeed * speedMult * Time.deltaTime);
        _anim.SetFloat(HashSpeed, moveSpeed * speedMult, 0.12f, Time.deltaTime);
    }

    void PlayRandomIdle()
    {
        if (idleStateNames == null || idleStateNames.Length == 0) return;
        _anim.CrossFadeInFixedTime(idleStateNames[Random.Range(0, idleStateNames.Length)], 0.25f);
    }

    void ApplyGravity()
    {
        if (_cc.isGrounded && _verticalSpeed < 0f) _verticalSpeed = -2f;
        _verticalSpeed -= 20f * Time.deltaTime;
        _cc.Move(new Vector3(0f, _verticalSpeed, 0f) * Time.deltaTime);
    }

    int NearestWaypointIndex()
    {
        if (patrolPath == null || patrolPath.Count == 0) return 0;
        int   nearest = 0;
        float minDist = float.MaxValue;
        for (int i = 0; i < patrolPath.Count; i++)
        {
            float d = Vector3.Distance(transform.position, patrolPath.GetWaypoint(i).position);
            if (d < minDist) { minDist = d; nearest = i; }
        }
        return nearest;
    }

    // ── Stealth Takedown ───────────────────────────────────────
    public void BeginTakedown()
    {
        _phase = Phase.TakenDown;
        _anim.SetFloat(HashSpeed, 0f);
        _anim.CrossFadeInFixedTime("Stealth Takedown", 0.05f);
        StartCoroutine(TakedownRoutine());
    }

    System.Collections.IEnumerator TakedownRoutine()
    {
        // wait to enter the takedown state
        float t = 0f;
        while (t < 2f)
        {
            t += Time.deltaTime;
            if (_anim.GetCurrentAnimatorStateInfo(0).IsName("Stealth Takedown")) break;
            yield return null;
        }

        // wait for the animation to nearly finish
        t = 0f;
        while (t < 10f)
        {
            t += Time.deltaTime;
            var info = _anim.GetCurrentAnimatorStateInfo(0);
            if (info.IsName("Stealth Takedown") && info.normalizedTime >= 0.9f) break;
            yield return null;
        }

        gameObject.SetActive(false);
    }

    // ── Gizmos ─────────────────────────────────────────────────
#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        DrawVisionCone();

        if (Application.isPlaying && patrolPath != null && patrolPath.Count > 0)
        {
            Handles.color = Color.red;
            Handles.DrawDottedLine(transform.position, patrolPath.GetWaypoint(_waypointIndex).position, 3f);
        }
    }

    void DrawVisionCone()
    {
        bool    alert   = Application.isPlaying && _playerInSight;
        Color   fillCol = alert ? fovAlertColor  : fovColor;
        Color   lineCol = alert ? edgeAlertColor : edgeColor;
        Vector3 origin  = transform.position + Vector3.up * eyeHeight;
        float   half    = fovAngle * 0.5f;

        Vector3 leftDir  = Quaternion.Euler(0, -half, 0) * transform.forward;
        Vector3 rightDir = Quaternion.Euler(0,  half, 0) * transform.forward;

        // ── Standing detection cone (yellow/orange) ──
        Handles.color = fillCol;
        Handles.DrawSolidArc(origin, Vector3.up, leftDir, fovAngle, detectionRange);

        Gizmos.color = lineCol;
        Gizmos.DrawLine(origin, origin + leftDir  * detectionRange);
        Gizmos.DrawLine(origin, origin + rightDir * detectionRange);

        int     segs = 36;
        Vector3 prev = origin + leftDir * detectionRange;
        for (int i = 1; i <= segs; i++)
        {
            Vector3 d    = Quaternion.Euler(0, Mathf.Lerp(-half, half, (float)i / segs), 0) * transform.forward;
            Vector3 curr = origin + d * detectionRange;
            Gizmos.DrawLine(prev, curr);
            prev = curr;
        }

        // ── Crouch detection cone (cyan, inner) ──
        float crouchRange = detectionRange * crouchRangeMultiplier;
        Handles.color = new Color(0f, 0.8f, 1f, 0.07f);
        Handles.DrawSolidArc(origin, Vector3.up, leftDir, fovAngle, crouchRange);

        Gizmos.color = new Color(0f, 0.8f, 1f, 0.5f);
        prev = origin + leftDir * crouchRange;
        for (int i = 1; i <= segs; i++)
        {
            Vector3 d    = Quaternion.Euler(0, Mathf.Lerp(-half, half, (float)i / segs), 0) * transform.forward;
            Vector3 curr = origin + d * crouchRange;
            Gizmos.DrawLine(prev, curr);
            prev = curr;
        }

        // ── Close-range rings ──
        Handles.color = new Color(lineCol.r, lineCol.g, lineCol.b, 0.5f);
        Handles.DrawWireDisc(transform.position, Vector3.up, closeDetectionRange);

        Handles.color = new Color(0f, 0.8f, 1f, 0.35f);
        Handles.DrawWireDisc(transform.position, Vector3.up, closeDetectionRange * crouchCloseMultiplier);

        // ── Label ──
        GUIStyle style = new GUIStyle { normal = { textColor = alert ? Color.red : Color.yellow }, fontStyle = FontStyle.Bold };
        string label = alert ? "! SPOTTED !" : $"FOV {fovAngle}°  Stand {detectionRange}m  Crouch {crouchRange:F1}m";
        Handles.Label(transform.position + Vector3.up * 2.8f, label, style);

    }
#endif
}
