using UnityEngine;

/// <summary>
/// Cover system. Walk up facing a <see cref="CoverObstacle"/> and press Q to take cover:
/// plays "Stand To Cover", settles into the cover idle, then A/D slides you left/right
/// along the wall.
///
/// While in cover, press C to toggle between the crouched and standing cover idle (movement
/// works in either pose). Press Q again, press S, or push away from the wall to play
/// "Cover To Stand" and return to locomotion.
///
/// The cover collider's height picks the set — a low box enters the crouched cover set and
/// stays crouch-only (C does nothing); a tall box (<see cref="highCoverThreshold"/>) enters
/// the standing set, after which C toggles between standing and crouching.
///
/// At an extreme edge (cover stops continuing that way), holding aim (right-click) draws the
/// pistol and aims it: the full-body cover animation keeps playing on the base layer while the
/// arms are overridden by the aim rig's IK (see <see cref="AimRigController"/>) to point the gun
/// at the cover aim target. Release aim to settle back behind cover.
///
/// This script does NOT rotate the player at all — the turn into/out of cover is authored
/// in the animation clips. It only translates the player along the wall and clamps that
/// movement at the cover's edges. While in cover, <see cref="PlayerController"/> is locked.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerController))]
public class CoverController : MonoBehaviour
{
    [Header("Detection")]
    [Tooltip("How far in front the player can reach to grab cover.")]
    public float detectDistance = 1.2f;
    [Tooltip("Height off the feet the detection ray fires from. Keep below the shortest cover box.")]
    public float detectHeight = 0.6f;
    public LayerMask coverMask = ~0;

    [Header("Cover fit")]
    [Tooltip("Cover height at/above which the STANDING set is used; below it uses the crouched set.")]
    public float highCoverThreshold = 1.3f;
    [Tooltip("Extra gap from the wall on top of the body radius. 0 = capsule sits flush against the wall; raise slightly if the mesh clips into it, lower toward 0 if it floats.")]
    public float wallPadding = 0f;

    // Perpendicular standoff from the wall surface that puts the body capsule flush against
    // it (radius + skin keeps it touching without the CharacterController de-penetrating).
    float Surface => _cc.radius + _cc.skinWidth + wallPadding;

    [Header("Movement along cover")]
    public float moveSpeed = 1.4f;
    public float moveDamp  = 0.1f;
    [Tooltip("How far ahead along the wall to require cover before allowing a step that way (edge detection).")]
    public float edgeProbeAhead = 0.4f;

    [Header("Transition lengths (seconds)")]
    [Tooltip("Time before A/D movement unlocks after pressing C (match Stand-To-Cover length).")]
    public float enterDuration = 1.2f;
    [Tooltip("Time the Cover-To-Stand clip plays before control returns (match its length).")]
    public float exitDuration  = 1.4f;

    [Header("Edge peek (hold right-click at an extreme edge)")]
    [Tooltip("How far the body leans out past the corner (metres) at full peek.")]
    public float peekDistance = 0.35f;
    [Tooltip("Extra body yaw toward the open side (degrees). The peek clip already defines the " +
             "lean, so leave at 0 unless you want the lower body to turn out a little more.")]
    public float peekYaw = 0f;
    [Tooltip("Smoothing time for easing the peek in and out.")]
    public float peekDamp = 0.12f;

    [Header("Debug")]
    [Tooltip("Log cover movement & edge state to the console each frame while in cover.")]
    public bool debugCover = false;

    CharacterController _cc;
    Animator            _anim;
    PlayerController     _player;

    enum St { None, Entering, In, Exiting }
    St       _st = St.None;
    Vector3  _normal;     // wall outward normal (horizontal)
    Vector3  _moveAxis;   // along-wall slide axis = player's right at entry (D = +, A = -)
    Collider _cover;
    float    _probeHeight = 0.5f;
    float    _timer;
    float    _move, _moveVel;
    bool     _mirror;                     // true while the active side is "right" → idle mirrors
    bool     _high;                       // current cover pose: true = standing, false = crouched
    bool     _canStand;                   // tall cover allows standing; low cover is crouch-only
    Quaternion _approachRot, _alignRot;   // facing at entry, and the square-to-wall target

    AimRigController _aimRig;             // optional aim rig — aims the arms while peeking
    float _peek, _peekVel;                // 0..1 peek blend, eased
    int   _peekSide;                      // edge we peek toward: +1 right, -1 left
    float _peekOffsetApplied;             // lateral lean currently applied along the wall
    bool  _peekAimToggle;                 // Ctrl-toggled hands-free peek (for tuning the aim IK)

    static readonly int HInCover    = Animator.StringToHash("InCover");
    static readonly int HCoverHigh  = Animator.StringToHash("CoverHigh");
    static readonly int HCoverMove  = Animator.StringToHash("CoverMove");
    static readonly int HCoverMirror = Animator.StringToHash("CoverMirror");

    void Awake()
    {
        _cc     = GetComponent<CharacterController>();
        _anim   = GetComponent<Animator>();
        _player = GetComponent<PlayerController>();
        _aimRig = GetComponent<AimRigController>();
    }

    void Update()
    {
        switch (_st)
        {
            case St.None:     TryEnter();        break;
            case St.Entering: EnterUpdate();     break;
            case St.In:       InCover();         break;
            case St.Exiting:  Countdown(St.None);break;
        }
    }

    // Enter clip + root motion walk the player in; meanwhile ease the facing to square up
    // with the wall so it's fully aligned by the end frame (smooth, not a snap).
    void EnterUpdate()
    {
        _timer -= Time.deltaTime;
        float t = 1f - Mathf.Clamp01(_timer / Mathf.Max(enterDuration, 0.0001f));
        transform.rotation = Quaternion.Slerp(_approachRot, _alignRot, Mathf.SmoothStep(0f, 1f, t));

        if (_timer > 0f) return;
        transform.rotation = _alignRot;          // fully square to the wall by the end
        _anim.applyRootMotion = false;           // hand control back to the manual slide
        _moveAxis = Vector3.ProjectOnPlane(transform.right, Vector3.up).normalized;
        _st = St.In;
    }

    // Counts out the exit clip, then returns control to locomotion.
    void Countdown(St next)
    {
        _timer -= Time.deltaTime;
        if (_timer > 0f) return;
        _st = next;
        if (next == St.None)
        {
            _anim.applyRootMotion = false;
            _player.UnlockControls();
        }
    }

    void TryEnter()
    {
        if (!Input.GetKeyDown(KeyCode.Q)) return;
        if (_player.IsAiming || _player.IsCrouching) return;

        Vector3 origin = transform.position + Vector3.up * detectHeight + transform.forward * 0.2f;
        if (!CastCover(origin, transform.forward, detectDistance, out var hit)) return;
        EnterCover(hit);
    }

    void EnterCover(RaycastHit hit)
    {
        _cover  = hit.collider;
        _normal = Vector3.ProjectOnPlane(hit.normal, Vector3.up).normalized;
        if (_normal.sqrMagnitude < 0.001f) _normal = -transform.forward;

        float coverHeight = _cover.bounds.size.y;     // cube's own height (sits on ground)
        _canStand         = coverHeight >= highCoverThreshold; // tall cover only; low cover is crouch-only
        _high             = _canStand;                // starting pose; C toggles it on tall cover
        _probeHeight      = Mathf.Clamp(coverHeight * 0.5f, 0.3f, 1.2f);

        // Ease the facing to square up with the wall over the enter clip (fully aligned by the
        // end frame) so the player ends laid flush/parallel to the cover, regardless of the
        // approach angle — a smooth turn rather than a snap.
        _approachRot = transform.rotation;
        _alignRot    = Quaternion.LookRotation(-_normal, Vector3.up);

        // No teleport: the Stand-To-Cover clip's root motion walks the player to the wall.
        // It's routed through the CharacterController (OnAnimatorMove), so collision stops the
        // body flush at the surface and the travel distance is whatever the clip needs.
        _player.LockControls();
        _anim.applyRootMotion = true;
        _anim.SetBool(HInCover, true);
        _anim.SetBool(HCoverHigh, _high);
        _anim.SetFloat(HCoverMove, 0f);
        _anim.SetBool(HCoverMirror, false);
        _move = 0f; _moveVel = 0f; _mirror = false;
        _timer = enterDuration;
        _st = St.Entering;
    }

    void InCover()
    {
        // Q leaves cover, or push away from the wall (S / down). The player faces into the wall
        // while in cover, so a backward push is the "opposite to cover" direction.
        if (Input.GetKeyDown(KeyCode.Q) || Input.GetAxisRaw("Vertical") < -0.1f) { BeginExit(); return; }

        // C toggles the cover pose: crouched cover idle <-> standing cover idle. Only tall cover
        // lets you stand; low cover stays crouched. The animator crossfades between the matching
        // move sets on CoverHigh, so this works while sliding too.
        if (_canStand && Input.GetKeyDown(KeyCode.C))
        {
            _high = !_high;
            _anim.SetBool(HCoverHigh, _high);
        }

        // Edge peek: when planted at an extreme edge (cover no longer continues that way) and
        // holding aim, lean out around the corner with the upper-body gun-aim pose. Right edge
        // peeks right, left edge peeks left; for cover narrow on both sides, use the active side.
        bool edgeR    = !CoverContinues(1);
        bool edgeL    = !CoverContinues(-1);
        int  edgeSide = (edgeR && edgeL) ? (_mirror ? -1 : 1) : (edgeR ? 1 : (edgeL ? -1 : 0));
        if (edgeSide != 0) _peekSide = edgeSide;

        // Hold right-click to peek, OR press Ctrl to TOGGLE the peek on/off (hands-free — so you
        // can sit at a corner and tune the cover aim IK in the inspector without holding a button).
        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
            _peekAimToggle = !_peekAimToggle;
        bool peeking = edgeSide != 0 && (Input.GetMouseButton(1) || _peekAimToggle);
        UpdatePeek(peeking);

        // While leaning out we commit to the corner: hold the lower body idle and don't slide.
        if (_peek > 0.01f)
        {
            _move = Mathf.SmoothDamp(_move, 0f, ref _moveVel, moveDamp);
            if (Mathf.Abs(_move) < 0.001f) _move = 0f;
            _anim.SetFloat(HCoverMove, _mirror ? _move : -_move);
            StickToWall();
            return;
        }

        float h   = Input.GetAxisRaw("Horizontal");
        int   dir = h > 0.1f ? 1 : (h < -0.1f ? -1 : 0);

        // Edge detection: only allow a step if cover continues that way.
        bool blocked = dir != 0 && !CoverContinues(dir);
        if (blocked) dir = 0;

        // Remember the side we're moving toward (frozen while idle) so the idle mirrors to it.
        if (dir > 0) _mirror = false;
        else if (dir < 0) _mirror = true;   // moving left → mirror the idle

        _move = Mathf.SmoothDamp(_move, dir, ref _moveVel, moveDamp);
        if (Mathf.Abs(_move) < 0.001f) _move = 0f;

        // Mirroring the state flips it horizontally; flipping the blend sign WITH it keeps the
        // sneak clips looking identical, while the centre idle (sign-independent) mirrors to
        // the active side. Base sign is negated so the sneak clips match the slide direction.
        _anim.SetBool(HCoverMirror, _mirror);
        _anim.SetFloat(HCoverMove, _mirror ? _move : -_move);

        if (dir != 0)
            _cc.Move(_moveAxis * (dir * moveSpeed) * Time.deltaTime);

        StickToWall();

        if (debugCover)
            Debug.Log($"[Cover] h={h:F2} dir={dir} blocked={blocked} contL={CoverContinues(-1)} contR={CoverContinues(1)} move={_move:F2}");
    }

    // Ease the peek in/out: ramp the masked aim-pose layer's weight, lean the body out past the
    // corner (lateral offset along the wall), and turn it toward the open side. At peek 0 this
    // restores the planted, square-to-wall pose, so it's safe to call every frame.
    void UpdatePeek(bool peeking)
    {
        // Tell the aim stack to draw the pistol + twist the torso toward the camera (and let
        // PistolRecoil fire) even though controls are locked. The animator's IsAiming bool stays
        // off, so the base layer keeps the cover pose.
        _player.SetCoverAiming(peeking);

        // Drive the aim from the hand-placed cover target (mirrored for the left side) instead of
        // the camera while peeking. The arms are overridden by IK; the body keeps its full cover
        // animation on the base layer.
        if (_aimRig != null) _aimRig.SetCoverPeek(peeking, _peekSide);

        _peek = Mathf.SmoothDamp(_peek, peeking ? 1f : 0f, ref _peekVel, peekDamp);
        if (_peek < 0.001f) _peek = 0f;

        // Lateral lean as an incremental delta so it composes with StickToWall's perpendicular fix.
        float desiredOffset = _peekSide * peekDistance * _peek;
        float delta = desiredOffset - _peekOffsetApplied;
        if (Mathf.Abs(delta) > 0.0001f) _cc.Move(_moveAxis * delta);
        _peekOffsetApplied = desiredOffset;

        // Turn toward the open side; collapses back to the square-to-wall facing at peek 0.
        Quaternion leanRot = _alignRot * Quaternion.Euler(0f, _peekSide * peekYaw, 0f);
        transform.rotation = Quaternion.Slerp(_alignRot, leanRot, _peek);
    }

    void BeginExit()
    {
        // Drop any active peek first: retract the lean, zero the aim layer, square back to the wall
        // so the Cover-To-Stand clip starts from the planted pose it was authored for.
        if (Mathf.Abs(_peekOffsetApplied) > 0.0001f) _cc.Move(_moveAxis * -_peekOffsetApplied);
        _peekOffsetApplied = 0f;
        _peek = 0f; _peekVel = 0f;
        _peekAimToggle = false;
        _player.SetCoverAiming(false);
        if (_aimRig != null) _aimRig.SetCoverPeek(false, _peekSide);
        transform.rotation = _alignRot;

        _anim.SetBool(HInCover, false);
        _anim.SetFloat(HCoverMove, 0f);
        _anim.applyRootMotion = true;   // Cover-To-Stand root motion drives the step out
        _timer = exitDuration;
        _st = St.Exiting;
    }

    // During enter/exit, the cover clips' root motion drives the player. Route it through the
    // CharacterController so wall collision stops the body flush at the surface (no clipping
    // inside, no teleport). Rotation is baked into the clips, so deltaRotation is ~identity.
    void OnAnimatorMove()
    {
        if (_anim == null || !_anim.applyRootMotion) return;
        _cc.Move(_anim.deltaPosition);
        transform.rotation *= _anim.deltaRotation;
    }

    // Outward margin the wall-probes start from (just outside the player). The cast distance
    // is always surfaceOffset + this + slack, so the ray is guaranteed to reach the wall.
    const float ProbeStartOut = 0.15f;
    float CastLen => Surface + ProbeStartOut + 0.3f;

    // Step a little along the wall in `dir`, then cast straight at the wall: cover still there
    // → the move is allowed; nothing there → we're at the cover's edge, so block that side.
    bool CoverContinues(int dir)
    {
        Vector3 from = transform.position + Vector3.up * _probeHeight
                     + _moveAxis * (dir * edgeProbeAhead) + _normal * ProbeStartOut;
        return CastCover(from, -_normal, CastLen, out _);
    }

    // Keep the player glued at surfaceOffset from the cover face (perpendicular only).
    void StickToWall()
    {
        Vector3 from = transform.position + Vector3.up * _probeHeight + _normal * ProbeStartOut;
        if (!CastCover(from, -_normal, CastLen, out var hit)) return;
        Vector3 desired = hit.point + _normal * Surface;
        Vector3 delta = new Vector3(desired.x - transform.position.x, 0f, desired.z - transform.position.z);
        if (delta.sqrMagnitude > 0.0001f) _cc.Move(delta);
    }

    // Nearest hit that is actually a CoverObstacle (filters out player, ground, etc.).
    bool CastCover(Vector3 from, Vector3 dir, float dist, out RaycastHit best)
    {
        best = default;
        float bd = float.MaxValue;
        bool found = false;
        foreach (var h in Physics.RaycastAll(from, dir, dist, coverMask, QueryTriggerInteraction.Ignore))
            if (h.collider.GetComponent<CoverObstacle>() != null && h.distance < bd)
            { bd = h.distance; best = h; found = true; }
        return found;
    }
}
