using UnityEngine;
using UnityEngine.AI;
#if UNITY_EDITOR
using UnityEditor;
#endif

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class EnemyPatrol : MonoBehaviour
{
    [Header("Shared Config")]
    [Tooltip("ScriptableObject holding all shared tuning. Create via Assets ▸ Create ▸ Stealth ▸ Enemy Config, then assign the SAME asset to every enemy.")]
    public EnemyConfig config;

    // ── Per-instance (unique to each enemy) ────────────────────
    [Header("Patrol")]
    public PatrolPath patrolPath;

    [Header("Waypoint Wait")]
    public float waitTime    = 0f;
    public float endWaitTime = 8f;

    [Header("Takedown")]
    public Transform takedownPoint;

    [Header("Prompts (pooled)")]
    [Tooltip("Optional anchor the borrowed prompt is parented to. If null, the enemy root is used. Use a unit-scale empty at head height for cleanest results.")]
    public Transform promptAnchor;
    [Tooltip("Local position of the Q prompt relative to the anchor (or enemy root).")]
    public Vector3 qPromptOffset = new Vector3(0f, 2f, 0f);
    [Tooltip("Local position of the F prompt relative to the anchor (or enemy root).")]
    public Vector3 fPromptOffset = new Vector3(0f, 2f, 0f);

    GameObject _qInstance;
    GameObject _fInstance;
    bool       _fPromptActive;
    public bool IsFPromptActive => _fPromptActive;

    Transform PromptParent => promptAnchor != null ? promptAnchor : transform;

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

    public void ShowFPrompt(bool show)
    {
        _fPromptActive = show;
        if (show == (_fInstance != null)) return;

        var pool = GameManager.Instance.Prompts;
        if (show)
            _fInstance = pool != null ? pool.Acquire(PromptType.F, PromptParent, fPromptOffset) : null;
        else
        {
            if (pool != null) pool.Release(_fInstance);
            _fInstance = null;
        }
    }

    // ── Shared tuning (forwarded from EnemyConfig) ─────────────
    // The rest of the class reads these exactly as before; only the storage moved.
    float    moveSpeed             => config.moveSpeed;
    float    reachDistance         => config.reachDistance;
    float    rotationSmoothTime    => config.rotationSmoothTime;
    float    maxTurnSpeed          => config.maxTurnSpeed;
    string[] idleStateNames        => config.idleStateNames;
    float    detectionRange        => config.detectionRange;
    float    fovAngle              => config.fovAngle;
    float    closeDetectionRange   => config.closeDetectionRange;
    float    eyeHeight             => config.eyeHeight;
    float    crouchRangeMultiplier => config.crouchRangeMultiplier;
    float    crouchCloseMultiplier => config.crouchCloseMultiplier;
    float    crouchHeadHeight      => config.crouchHeadHeight;
    float    standHeadHeight       => config.standHeadHeight;
    float    suspicionRange        => config.suspicionRange;
    float    suspicionFOV          => config.suspicionFOV;
    float    suspicionDuration     => config.suspicionDuration;
    float    facingDuration        => config.facingDuration;
    float    investigateDuration   => config.investigateDuration;
    float    distractionGlanceDuration => config.distractionGlanceDuration;
    float    distractionInvestigateDuration => config.distractionInvestigateDuration;
    float    lureGhostDistance     => config.lureGhostDistance;
    float    lureTurnDelay         => config.lureTurnDelay;
    Color    fovColor              => config.fovColor;
    Color    fovAlertColor         => config.fovAlertColor;
    Color    edgeColor             => config.edgeColor;
    Color    edgeAlertColor        => config.edgeAlertColor;

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

    // Suspicion / investigate
    float   _suspicionTimer;
    Vector3 _lastSeenPosition;
    float   _facingTimer;
    float   _investigateTimer;

    // Distraction (dropped object noise) / lure
    Vector3 _distractionSpot;
    float   _glanceTimer;
    float   _glanceWait;

    // Seconds to wait at the spot for the current investigation (varies by trigger).
    float   _investigateWait;

    // NavMesh path following
    NavMeshPath _navPath;
    float       _pathRecalcTimer;
    Vector3     _lastNavDestination;
    const float PathRecalcInterval = 0.35f;

    enum Phase { Patrolling, Investigating, Glancing, Spotted, TakenDown }
    Phase _phase = Phase.Patrolling;

    enum InvestigateStep { Facing, Moving, Waiting }
    InvestigateStep _investigateStep;

    public bool CanBeTakenDown => _phase == Phase.Patrolling || _phase == Phase.Investigating || _phase == Phase.Glancing || _phase == Phase.Spotted;

    // ── Distraction API ────────────────────────────────────────
    /// <summary>Sound radius within which this enemy notices a dropped distraction (no line-of-sight needed).</summary>
    public float HearingRange => config != null ? config.distractionHearingRange : 18f;

    /// <summary>An already-alerted or taken-down enemy ignores distractions.</summary>
    public bool CanBeDistracted => _phase == Phase.Patrolling || _phase == Phase.Investigating || _phase == Phase.Glancing;

    /// <summary>
    /// React to a distraction that landed at <paramref name="spot"/>.
    /// The caller decides who investigates: the nearest hearer gets
    /// <paramref name="investigate"/>=true (walks to the spot), everyone else
    /// just glances toward it and then resumes patrol.
    /// </summary>
    public void NoticeDistraction(Vector3 spot, bool investigate)
    {
        if (_phase == Phase.Spotted || _phase == Phase.TakenDown) return;

        _suspicionTimer = 0f;
        _waiting        = false;

        if (investigate)
        {
            _lastSeenPosition = spot;
            _facingTimer      = 0f;
            _investigateTimer = 0f;
            _investigateWait  = distractionInvestigateDuration;
            _investigateStep  = InvestigateStep.Facing;
            _phase            = Phase.Investigating;
        }
        else
        {
            _distractionSpot = spot;
            _glanceTimer     = 0f;
            _glanceWait      = distractionGlanceDuration;
            _phase           = Phase.Glancing;
        }
    }

    /// <summary>
    /// Player pressed Q on this enemy: the Ghost appears behind it and calls; the
    /// enemy turns around and inspects that spot in place for a while, then resumes.
    /// </summary>
    public void LureFromBehind()
    {
        if (!CanBeDistracted) return;

        Vector3 behind = transform.position - transform.forward * lureGhostDistance;
        behind.y = transform.position.y;

        // Ghost appears behind, facing the enemy's back, and does its calling gesture immediately…
        var ghost = GameManager.Instance.Ghost;
        if (ghost != null)
            ghost.Summon(behind, transform.position + Vector3.up * 1f);

        // …then the enemy turns around after a delay (synced with the calling animation).
        StartCoroutine(LureTurnRoutine(behind));
    }

    System.Collections.IEnumerator LureTurnRoutine(Vector3 spot)
    {
        if (lureTurnDelay > 0f) yield return new WaitForSeconds(lureTurnDelay);
        if (!CanBeDistracted) yield break;                   // got spotted / taken down during the delay

        // Turn around and investigate the spot in place (no walking).
        _suspicionTimer  = 0f;
        _waiting         = false;
        _distractionSpot = spot;
        _glanceTimer     = 0f;
        _glanceWait      = distractionInvestigateDuration;   // ~5 s
        _phase           = Phase.Glancing;
    }

    // Suspicion UI hooks
    public bool  IsSuspicious      => _suspicionTimer > 0f;
    public float SuspicionProgress => Mathf.Clamp01(_suspicionTimer / suspicionDuration);
    public bool  IsInvestigating   => _phase == Phase.Investigating;
    public bool  IsDetected        => _phase == Phase.Spotted;

    static readonly int HashSpeed   = Animator.StringToHash("Speed");
    static readonly int HashSpotted = Animator.StringToHash("Spotted");

    // ── Init ───────────────────────────────────────────────────
    void Start()
    {
        if (config == null)
        {
            Debug.LogError($"EnemyPatrol on '{name}' has no EnemyConfig assigned — using runtime defaults.", this);
            config = ScriptableObject.CreateInstance<EnemyConfig>();
        }

        _cc   = GetComponent<CharacterController>();
        _anim = GetComponent<Animator>();
        _anim.applyRootMotion = false;

        // Player comes from the central manager (no per-enemy tag search).
        _playerCtrl = GameManager.Instance.Player;
        _player     = GameManager.Instance.PlayerTransform;
        if (_player == null) Debug.LogWarning("EnemyPatrol: no PlayerController found in the scene.");

        GameManager.Instance.RegisterEnemy(this);

        _waypointIndex = NearestWaypointIndex();
        _navPath       = new NavMeshPath();
    }

    void OnEnable()
    {
        if (GameManager.Exists) GameManager.Instance.RegisterEnemy(this);
    }

    // Pooled prompts are returned to the pool, and the enemy leaves the registry,
    // whenever it is disabled (e.g. after a takedown).
    void OnDisable()
    {
        ShowQPrompt(false);
        ShowFPrompt(false);
        if (GameManager.Exists) GameManager.Instance.UnregisterEnemy(this);
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
                if (!HandleSuspicion()) HandlePatrol();
                break;

            case Phase.Investigating:
                _playerInSight = CanSeePlayer();
                if (_playerInSight) { OnPlayerSpotted(); return; }
                HandleInvestigating();
                break;

            case Phase.Glancing:
                _playerInSight = CanSeePlayer();
                if (_playerInSight) { OnPlayerSpotted(); return; }
                HandleGlancing();
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

        bool  crouching      = _playerCtrl != null && _playerCtrl.IsCrouching;
        float effectiveRange = detectionRange      * (crouching ? crouchRangeMultiplier : 1f);
        float effectiveClose = closeDetectionRange * (crouching ? crouchCloseMultiplier : 1f);

        float   targetHeight = crouching ? crouchHeadHeight : standHeadHeight;
        Vector3 eyePos       = transform.position + Vector3.up * eyeHeight;
        Vector3 playerHead   = _player.position   + Vector3.up * targetHeight;
        Vector3 toPlayer     = playerHead - eyePos;
        float   dist         = toPlayer.magnitude;

        if (dist <= effectiveClose) return true;
        if (dist > effectiveRange)  return false;
        if (Vector3.Angle(transform.forward, toPlayer.normalized) > fovAngle * 0.5f) return false;

        if (Physics.Raycast(eyePos, toPlayer.normalized, out RaycastHit hit, dist))
            if (!hit.transform.IsChildOf(_player)) return false;

        return true;
    }

    // Cone-shaped suspicion check — same crouching rules, wider FOV, no close zone
    bool IsInSuspicionCone()
    {
        if (_player == null) return false;

        bool    crouching    = _playerCtrl != null && _playerCtrl.IsCrouching;
        float   effectiveRange = suspicionRange * (crouching ? crouchRangeMultiplier : 1f);
        float   targetHeight   = crouching ? crouchHeadHeight : standHeadHeight;
        Vector3 eyePos         = transform.position + Vector3.up * eyeHeight;
        Vector3 playerHead     = _player.position   + Vector3.up * targetHeight;
        Vector3 toPlayer       = playerHead - eyePos;
        float   dist           = toPlayer.magnitude;

        if (dist > effectiveRange) return false;
        if (Vector3.Angle(transform.forward, toPlayer.normalized) > suspicionFOV * 0.5f) return false;

        if (Physics.Raycast(eyePos, toPlayer.normalized, out RaycastHit hit, dist))
            if (!hit.transform.IsChildOf(_player)) return false;

        return true;
    }

    void OnPlayerSpotted()
    {
        _phase          = Phase.Spotted;
        _waiting        = false;
        _suspicionTimer = 0f;
        _anim.SetFloat(HashSpeed, 0f);
        _anim.SetTrigger(HashSpotted);

        Vector3 dir = _player.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);
    }

    void HandleSpottedPhase()
    {
        _anim.SetFloat(HashSpeed, 0f);

        var info = _anim.GetCurrentAnimatorStateInfo(0);
        if (!_anim.IsInTransition(0) && info.IsName("Angry Point") && info.normalizedTime >= 0.9f)
        {
            _phase         = Phase.Patrolling;
            _playerInSight = false;
            _waypointIndex = NearestWaypointIndex();
        }
    }

    // ── Suspicion ──────────────────────────────────────────────
    // Returns true while actively suspicious so the caller can suppress patrol movement.
    bool HandleSuspicion()
    {
        if (IsInSuspicionCone())
        {
            _lastSeenPosition = _player.position;
            _suspicionTimer  += Time.deltaTime;

            // Stop and face the player while the timer builds
            _anim.SetFloat(HashSpeed, 0f, 0.08f, Time.deltaTime);
            StealthUtils.FaceWorldPoint(transform, _player.position, ref _yawVelocity, rotationSmoothTime, maxTurnSpeed);

            if (_suspicionTimer >= suspicionDuration)
            {
                _suspicionTimer   = 0f;
                _facingTimer      = 0f;
                _investigateTimer = 0f;
                _investigateWait  = investigateDuration;
                _investigateStep  = InvestigateStep.Facing;
                _phase            = Phase.Investigating;
            }

            return true;
        }
        else
        {
            _suspicionTimer = Mathf.Max(0f, _suspicionTimer - Time.deltaTime);
            return false;
        }
    }

    // ── Investigate ────────────────────────────────────────────
    void HandleInvestigating()
    {
        switch (_investigateStep)
        {
            case InvestigateStep.Facing:  HandleFacing();        break;
            case InvestigateStep.Moving:  HandleMovingToSpot();  break;
            case InvestigateStep.Waiting: HandleWaitingAtSpot(); break;
        }
    }

    // Step 1 — stand still and rotate to face last-seen direction
    void HandleFacing()
    {
        _anim.SetFloat(HashSpeed, 0f, 0.08f, Time.deltaTime);
        StealthUtils.FaceWorldPoint(transform, _lastSeenPosition, ref _yawVelocity, rotationSmoothTime, maxTurnSpeed);

        _facingTimer += Time.deltaTime;
        if (_facingTimer >= facingDuration)
            _investigateStep = InvestigateStep.Moving;
    }

    // Step 2 — walk to last-seen position
    void HandleMovingToSpot()
    {
        Vector3 toTarget = _lastSeenPosition - transform.position;
        toTarget.y = 0f;

        if (toTarget.magnitude <= reachDistance)
        {
            _investigateTimer = 0f;
            _investigateStep  = InvestigateStep.Waiting;
            _anim.SetFloat(HashSpeed, 0f, 0.08f, Time.deltaTime);
            return;
        }

        DriveToward(_lastSeenPosition);
    }

    // Step 3 — stand at the spot for investigateDuration then return to patrol
    void HandleWaitingAtSpot()
    {
        _anim.SetFloat(HashSpeed, 0f, 0.08f, Time.deltaTime);
        _investigateTimer += Time.deltaTime;

        if (_investigateTimer >= _investigateWait)
        {
            _phase         = Phase.Patrolling;
            _suspicionTimer = 0f;
            _waypointIndex = NearestWaypointIndex();
        }
    }

    // ── Glance (heard a distraction but isn't the closest) ─────
    // Stand still, turn to look toward the noise, then resume patrol.
    void HandleGlancing()
    {
        _anim.SetFloat(HashSpeed, 0f, 0.08f, Time.deltaTime);
        StealthUtils.FaceWorldPoint(transform, _distractionSpot, ref _yawVelocity, rotationSmoothTime, maxTurnSpeed);

        _glanceTimer += Time.deltaTime;
        if (_glanceTimer >= _glanceWait)
        {
            _phase         = Phase.Patrolling;
            _waypointIndex = NearestWaypointIndex();
        }
    }

    // ── NavMesh steering ───────────────────────────────────────
    // Returns the next corner on a NavMesh path toward `destination`.
    // Falls back to `destination` directly when no valid path exists.
    Vector3 GetNavSteeringTarget(Vector3 destination)
    {
        // Recalculate if destination moved or timer expired
        if ((destination - _lastNavDestination).sqrMagnitude > 0.25f)
        {
            _lastNavDestination = destination;
            _pathRecalcTimer    = 0f;
        }

        _pathRecalcTimer -= Time.deltaTime;
        if (_pathRecalcTimer <= 0f)
        {
            _pathRecalcTimer = PathRecalcInterval;
            NavMesh.CalculatePath(transform.position, destination, NavMesh.AllAreas, _navPath);
        }

        if (_navPath.status == NavMeshPathStatus.PathInvalid || _navPath.corners.Length == 0)
            return destination;

        // corners[0] ≈ current position; corners[1] is the next real waypoint to steer toward
        return _navPath.corners.Length >= 2 ? _navPath.corners[1] : _navPath.corners[0];
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

        DriveToward(target.position);
    }

    // Steer toward `destination` along the NavMesh and advance, easing the turn and
    // slowing down on sharp corners. Shared by patrol and investigate-move so both
    // walk with exactly the same feel.
    void DriveToward(Vector3 destination)
    {
        Vector3 flat = destination - transform.position;
        flat.y = 0f;

        Vector3 steer = GetNavSteeringTarget(destination) - transform.position;
        steer.y       = 0f;
        Vector3 dir   = steer.sqrMagnitude > 0.001f ? steer.normalized
                      : (flat.sqrMagnitude > 0.0001f ? flat.normalized : transform.forward);

        float targetYaw = Quaternion.LookRotation(dir).eulerAngles.y;
        float newYaw    = Mathf.SmoothDampAngle(transform.eulerAngles.y, targetYaw,
                              ref _yawVelocity, rotationSmoothTime, maxTurnSpeed);
        transform.rotation = Quaternion.Euler(0f, newYaw, 0f);

        float angleOff  = Vector3.Angle(transform.forward, dir);
        float speedMult = Mathf.Lerp(1f, 0.4f, Mathf.Clamp01(angleOff / 90f));
        if (!_cc.enabled) return;
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
        if (!_cc.enabled) return;
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
    /// <summary>
    /// Freeze the enemy the instant a takedown is committed (F pressed), BEFORE the
    /// player walks in. Stops all detection/AI so the enemy keeps facing away and never
    /// snaps round to look at the player during the approach. The takedown animation
    /// itself is triggered later by <see cref="BeginTakedown"/> once the player arrives.
    /// </summary>
    public void LockForTakedown()
    {
        ShowQPrompt(false);
        ShowFPrompt(false);
        _phase          = Phase.TakenDown;   // Update() early-returns → no detection, no rotation
        _suspicionTimer = 0f;
        _waiting        = false;
        _anim.SetFloat(HashSpeed, 0f);
    }

    public void BeginTakedown()
    {
        ShowQPrompt(false);
        ShowFPrompt(false);
        _phase = Phase.TakenDown;
        _anim.SetFloat(HashSpeed, 0f);
        _anim.CrossFadeInFixedTime("Stealth Takedown", 0.05f);
        StartCoroutine(TakedownRoutine());
    }

    System.Collections.IEnumerator TakedownRoutine()
    {
        float t = 0f;
        while (t < 2f)
        {
            t += Time.deltaTime;
            if (_anim.GetCurrentAnimatorStateInfo(0).IsName("Stealth Takedown")) break;
            yield return null;
        }

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
        if (config == null) return;   // nothing to draw until a config is assigned

        DrawVisionCone();

        if (Application.isPlaying && patrolPath != null && patrolPath.Count > 0)
        {
            Handles.color = Color.red;
            Handles.DrawDottedLine(transform.position, patrolPath.GetWaypoint(_waypointIndex).position, 3f);
        }

        if (Application.isPlaying && _phase == Phase.Investigating)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.9f);
            Gizmos.DrawSphere(_lastSeenPosition + Vector3.up * 0.15f, 0.25f);
            Handles.color = new Color(1f, 0.5f, 0f, 0.7f);
            Handles.DrawDottedLine(transform.position, _lastSeenPosition, 4f);
        }
    }

    void DrawVisionCone()
    {
        bool    alert    = Application.isPlaying && _playerInSight;
        bool    suspect  = Application.isPlaying && _suspicionTimer > 0f;
        Color   fillCol  = alert ? fovAlertColor  : fovColor;
        Color   lineCol  = alert ? edgeAlertColor : edgeColor;
        Vector3 origin   = transform.position + Vector3.up * eyeHeight;
        int     segs     = 36;

        // ── Suspicion cone (orange, outermost) ──────────────────
        float suspHalf  = suspicionFOV * 0.5f;
        Vector3 sLeftDir  = Quaternion.Euler(0, -suspHalf, 0) * transform.forward;
        Vector3 sRightDir = Quaternion.Euler(0,  suspHalf, 0) * transform.forward;

        float suspFill = Application.isPlaying
            ? Mathf.Lerp(0.04f, 0.16f, _suspicionTimer / Mathf.Max(suspicionDuration, 0.001f))
            : 0.04f;
        Handles.color = new Color(1f, 0.55f, 0f, suspFill);
        Handles.DrawSolidArc(origin, Vector3.up, sLeftDir, suspicionFOV, suspicionRange);

        Gizmos.color = new Color(1f, 0.55f, 0f, suspect ? 0.85f : 0.35f);
        Gizmos.DrawLine(origin, origin + sLeftDir  * suspicionRange);
        Gizmos.DrawLine(origin, origin + sRightDir * suspicionRange);

        Vector3 sPrev = origin + sLeftDir * suspicionRange;
        for (int i = 1; i <= segs; i++)
        {
            Vector3 d    = Quaternion.Euler(0, Mathf.Lerp(-suspHalf, suspHalf, (float)i / segs), 0) * transform.forward;
            Vector3 curr = origin + d * suspicionRange;
            Gizmos.DrawLine(sPrev, curr);
            sPrev = curr;
        }

        // ── Standing detection cone (yellow/orange) ──────────────
        float   half     = fovAngle * 0.5f;
        Vector3 leftDir  = Quaternion.Euler(0, -half, 0) * transform.forward;
        Vector3 rightDir = Quaternion.Euler(0,  half, 0) * transform.forward;

        Handles.color = fillCol;
        Handles.DrawSolidArc(origin, Vector3.up, leftDir, fovAngle, detectionRange);

        Gizmos.color = lineCol;
        Gizmos.DrawLine(origin, origin + leftDir  * detectionRange);
        Gizmos.DrawLine(origin, origin + rightDir * detectionRange);

        Vector3 prev = origin + leftDir * detectionRange;
        for (int i = 1; i <= segs; i++)
        {
            Vector3 d    = Quaternion.Euler(0, Mathf.Lerp(-half, half, (float)i / segs), 0) * transform.forward;
            Vector3 curr = origin + d * detectionRange;
            Gizmos.DrawLine(prev, curr);
            prev = curr;
        }

        // ── Crouch detection cone (cyan, inner) ──────────────────
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

        // ── Close-range rings ────────────────────────────────────
        Handles.color = new Color(lineCol.r, lineCol.g, lineCol.b, 0.5f);
        Handles.DrawWireDisc(transform.position, Vector3.up, closeDetectionRange);

        Handles.color = new Color(0f, 0.8f, 1f, 0.35f);
        Handles.DrawWireDisc(transform.position, Vector3.up, closeDetectionRange * crouchCloseMultiplier);

        // ── Label ────────────────────────────────────────────────
        Color labelCol = alert ? Color.red : (suspect || _phase == Phase.Investigating ? new Color(1f, 0.5f, 0f) : Color.yellow);
        GUIStyle style = new GUIStyle { normal = { textColor = labelCol }, fontStyle = FontStyle.Bold };

        string label;
        if (alert)
            label = "! SPOTTED !";
        else if (_phase == Phase.Investigating)
        {
            label = _investigateStep switch
            {
                InvestigateStep.Facing  => $"INVESTIGATING — facing ({facingDuration - _facingTimer:F1}s)",
                InvestigateStep.Moving  => "INVESTIGATING — moving to spot",
                InvestigateStep.Waiting => $"INVESTIGATING — waiting ({investigateDuration - _investigateTimer:F1}s)",
                _                       => "INVESTIGATING"
            };
        }
        else if (suspect)
            label = $"Suspicious  {_suspicionTimer:F1} / {suspicionDuration:F1}s";
        else
            label = $"FOV {fovAngle}°  Stand {detectionRange}m  Crouch {crouchRange:F1}m  Suspicion {suspicionFOV}° {suspicionRange}m";

        Handles.Label(transform.position + Vector3.up * 2.8f, label, style);
    }
#endif
}
