using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Drives the upper-body aim rig. While the player is aiming (see
/// <see cref="PlayerController.IsAiming"/>), the spine Multi-Aim Constraints twist the
/// torso toward an <see cref="aimTarget"/> placed along the camera's view direction —
/// so moving the camera rotates the hips/torso only, while the legs stay planted.
///
/// The rig weight fades in/out smoothly so entering and leaving aim mode never pops.
/// Lives on the Player (next to <see cref="PlayerController"/>); auto-wires the rig,
/// target, and chest anchor it created at edit time.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class AimRigController : MonoBehaviour
{
    [Header("Rig (auto-found if empty)")]
    public Rig       aimRig;
    public Transform aimTarget;

    [Header("Pistol")]
    [Tooltip("The pistol held in the right hand (auto-found by name 'Pistol'). Shown only while aiming.")]
    public GameObject pistol;

    [Header("Tuning")]
    [Tooltip("Distance ahead the aim target sits along the camera direction. Larger = subtler torso twist.")]
    public float targetDistance  = 8f;
    [Tooltip("Height (off the player root) the aim ray originates from — roughly chest height. Anchored to the ROOT, not the chest bone, to avoid IK feedback jitter.")]
    public float aimOriginHeight = 1.4f;
    [Tooltip("How fast the torso-aim rig fades in/out when you start/stop aiming.")]
    public float weightLerpSpeed = 12f;

    [Header("Aim Calibration (tune live in Play mode until the gun is centred)")]
    [Tooltip("Yaw nudge (deg). + pushes the aim RIGHT, - pushes it LEFT. Use this to cancel the gun's left bias.")]
    [Range(-45f, 45f)] public float aimYawOffset   = 0f;
    [Tooltip("Pitch nudge (deg). + aims UP, - aims DOWN.")]
    [Range(-30f, 30f)] public float aimPitchOffset = 0f;

    [Header("Cover-Peek Aim")]
    [Tooltip("IK aim target used ONLY while peeking from cover — the camera does not drive it. " +
             "Position this GameObject (parented under the player) by hand to set where the gun " +
             "points in the peek pose. It's mirrored automatically for a left-edge peek. " +
             "Auto-found by the child name 'CoverAimTarget'.")]
    public Transform coverAimTarget;
    [Tooltip("Spine aim constraints — faded to 0 while peeking so the torso/hips stay in the cover " +
             "pose. Auto-found by child names 'Spine_Aim' / 'Spine1_Aim'.")]
    public MultiAimConstraint spineAim;
    public MultiAimConstraint spine1Aim;
    [Tooltip("Two-bone-IK hand targets (children of the chest). While peeking, BOTH orbit around " +
             "the chest to aim the whole arms — elbows and hands, left and right — at the target, " +
             "with the spine fixed. Auto-found by names 'RightHandTarget' / 'LeftHandTarget'.")]
    public Transform rightHandTarget;
    public Transform leftHandTarget;
    [Tooltip("Extra rotation (deg) applied to the arm-aim so the gun BARREL lines up with the " +
             "target, since the barrel is offset from the arm direction. X=pitch, Y=yaw, Z=roll.")]
    public Vector3 coverArmAimOffset = Vector3.zero;

    [Header("Debug")]
    [Tooltip("Draw aim rays in the Scene view + log aim angles to the console while aiming.")]
    public bool  debugAim     = true;
    [Tooltip("Seconds between debug console readouts.")]
    public float debugLogEvery = 0.4f;

    PlayerController _player;
    Transform        _aimOrigin;   // chest bone — anchors the target at torso height
    Transform        _spine;       // spine bone (where the IK aims)
    Transform        _rightHand;   // gun hand — to read the actual aiming direction
    float            _weight;
    float            _coverBlend;   // 0 = spine aims (normal), 1 = arms aim (cover peek)
    float            _dbgTimer;
    bool             _pistolShown;

    // Authored rest pose of the hand IK targets (captured once) — the arm-aim orbits from here.
    Vector3    _restPosR, _restPosL;
    Quaternion _restRotR, _restRotL;

    // Cover peek: aim is driven by the hand-placed coverAimTarget, not the camera.
    bool _coverPeekActive;
    int  _coverPeekSide = 1;   // +1 right edge, -1 left edge (mirrors the target across the body)

    /// <summary>
    /// While peeking from cover, drive the aim from <see cref="coverAimTarget"/> (a hand-placed
    /// GameObject) instead of the camera. The target is mirrored across the body for a left-edge
    /// peek. Pass active=false to hand aiming back to the camera.
    /// </summary>
    public void SetCoverPeek(bool active, int side)
    {
        _coverPeekActive = active;
        if (side != 0) _coverPeekSide = side;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform c in root) { var r = FindDeep(c, name); if (r != null) return r; }
        return null;
    }

    void Awake()
    {
        _player = GetComponent<PlayerController>();

        if (aimRig == null)
        {
            var t = transform.Find("AimRig");
            if (t != null) aimRig = t.GetComponent<Rig>();
        }
        if (aimTarget == null && aimRig != null)
        {
            var t = aimRig.transform.Find("AimTarget");
            if (t != null) aimTarget = t;
        }
        if (coverAimTarget == null)
        {
            var t = FindDeep(transform, "CoverAimTarget");
            if (t != null) coverAimTarget = t;
        }
        if (aimRig != null)
        {
            if (spineAim  == null) { var t = aimRig.transform.Find("Spine_Aim");  if (t) spineAim  = t.GetComponent<MultiAimConstraint>(); }
            if (spine1Aim == null) { var t = aimRig.transform.Find("Spine1_Aim"); if (t) spine1Aim = t.GetComponent<MultiAimConstraint>(); }
        }
        if (rightHandTarget == null) rightHandTarget = FindDeep(transform, "RightHandTarget");
        if (leftHandTarget  == null) leftHandTarget  = FindDeep(transform, "LeftHandTarget");
        if (rightHandTarget != null) { _restPosR = rightHandTarget.localPosition; _restRotR = rightHandTarget.localRotation; }
        if (leftHandTarget  != null) { _restPosL = leftHandTarget.localPosition;  _restRotL = leftHandTarget.localRotation; }

        var anim = GetComponent<Animator>();
        if (anim != null && anim.isHuman)
        {
            _aimOrigin = anim.GetBoneTransform(HumanBodyBones.UpperChest)
                      ?? anim.GetBoneTransform(HumanBodyBones.Chest);
            _spine     = anim.GetBoneTransform(HumanBodyBones.Spine);
            _rightHand = anim.GetBoneTransform(HumanBodyBones.RightHand);
        }

        if (aimRig != null) aimRig.weight = 0f;

        if (pistol == null)
        {
            var p = FindDeep(transform, "Pistol");
            if (p != null) pistol = p.gameObject;
        }
        if (pistol != null) pistol.SetActive(false);   // holstered until aiming
        _pistolShown = false;
    }

    // LateUpdate: the rig evaluates inside the Animator pass, so feeding the target
    // here is fine (a one-frame delay on torso aim is imperceptible).
    void LateUpdate()
    {
        if (aimRig == null || aimTarget == null) return;

        Camera cam = GameManager.Instance.MainCamera;

        if (_coverPeekActive && coverAimTarget != null)
        {
            // Cover peek: aim at the hand-placed target, NOT the camera. Mirror its position
            // across the body (local X) for a left-edge peek so one placement serves both sides.
            Vector3 local = transform.InverseTransformPoint(coverAimTarget.position);
            if (_coverPeekSide < 0) local.x = -local.x;
            aimTarget.position = transform.TransformPoint(local);
        }
        else if (cam != null)
        {
            // Camera view direction, nudged by the calibration offsets so the GUN
            // (not the spine) ends up centred. Yaw about world-up, pitch about cam-right.
            Vector3 dir = cam.transform.forward;
            dir = Quaternion.AngleAxis(aimYawOffset,   Vector3.up)          * dir;
            dir = Quaternion.AngleAxis(aimPitchOffset, cam.transform.right) * dir;

            // Anchor at the ROOT (+ fixed height), NOT the chest bone — the chest is what
            // the IK rotates, so anchoring there would create a twist→move→twist feedback
            // loop that reads as shaking.
            Vector3 origin = transform.position + Vector3.up * aimOriginHeight;
            aimTarget.position = origin + dir * targetDistance;
        }

        float target = _player.IsAiming ? 1f : 0f;
        _weight = Mathf.MoveTowards(_weight, target, weightLerpSpeed * Time.deltaTime);
        aimRig.weight = _weight;

        // While peeking, fade the spine-aim out (torso/hips stay in the cover pose) and aim the
        // arms instead by orbiting the hand IK targets — see DriveCoverArmAim.
        _coverBlend = Mathf.MoveTowards(_coverBlend, _coverPeekActive ? 1f : 0f, weightLerpSpeed * Time.deltaTime);
        if (spineAim  != null) spineAim.weight  = 1f - _coverBlend;
        if (spine1Aim != null) spine1Aim.weight = 1f - _coverBlend;
        DriveCoverArmAim();

        // Draw the pistol only while aiming.
        if (pistol != null && _pistolShown != _player.IsAiming)
        {
            _pistolShown = _player.IsAiming;
            pistol.SetActive(_pistolShown);
        }

        if (debugAim && _player.IsAiming && cam != null && !_coverPeekActive) DrawAimDebug(cam);
    }

    // Cover aim: instead of rotating the spine (which moves the torso/hips), orbit BOTH hand IK
    // targets around the chest by the rotation that points the gun arm at the target. The two-bone
    // arm IKs then bend the whole arms — elbows and hands, left and right — to follow, while the
    // spine stays in the cover pose. The two targets share one rotation so the grip stays rigid.
    void DriveCoverArmAim()
    {
        if (_coverBlend < 0.001f || rightHandTarget == null || rightHandTarget.parent == null) return;

        Transform chest = rightHandTarget.parent;      // hand targets are parented under the chest
        Vector3   pivot = chest.position;

        Vector3 restWorldR = chest.TransformPoint(_restPosR);
        Vector3 restDir = restWorldR - pivot;
        Vector3 aimDir  = aimTarget.position - pivot;  // aimTarget already sits on the cover target
        if (restDir.sqrMagnitude < 1e-6f || aimDir.sqrMagnitude < 1e-6f) return;

        Quaternion full = Quaternion.FromToRotation(restDir.normalized, aimDir.normalized)
                        * Quaternion.Euler(coverArmAimOffset);
        Quaternion q = Quaternion.Slerp(Quaternion.identity, full, _coverBlend);

        OrbitTarget(rightHandTarget, _restPosR, _restRotR, pivot, q);
        OrbitTarget(leftHandTarget,  _restPosL, _restRotL, pivot, q);
    }

    void OrbitTarget(Transform t, Vector3 restLocalPos, Quaternion restLocalRot, Vector3 pivot, Quaternion q)
    {
        if (t == null || t.parent == null) return;
        Vector3    restWorldPos = t.parent.TransformPoint(restLocalPos);
        Quaternion restWorldRot = t.parent.rotation * restLocalRot;
        t.position = pivot + q * (restWorldPos - pivot);
        t.rotation = q * restWorldRot;
    }

    // Visualises the aim while aiming so the straight-ahead reference can be compared
    // against where the spine/gun actually point. Scene-view rays + a throttled console
    // readout. Positive angle = right of the camera centre, negative = left.
    void DrawAimDebug(Camera cam)
    {
        Vector3 origin = _aimOrigin != null ? _aimOrigin.position : transform.position + Vector3.up * 1.4f;
        Vector3 camFwd = cam.transform.forward;

        // GREEN  = camera centre line (the "straight" you want to aim at)
        Debug.DrawRay(origin, camFwd * 6f, Color.green);
        // RED    = spine forward (where the IK is pointing the torso)
        if (_spine != null)     Debug.DrawRay(_spine.position, _spine.forward * 3f, Color.red);
        // CYAN   = direction to the aim target
        if (aimTarget != null)  Debug.DrawLine(origin, aimTarget.position, Color.cyan);
        // Right-hand axes (find which one is the gun barrel): BLUE=fwd, MAGENTA=right, YELLOW=up
        if (_rightHand != null)
        {
            Debug.DrawRay(_rightHand.position, _rightHand.forward * 2f, Color.blue);
            Debug.DrawRay(_rightHand.position, _rightHand.right   * 1f, Color.magenta);
            Debug.DrawRay(_rightHand.position, _rightHand.up      * 1f, Color.yellow);
        }

        _dbgTimer += Time.deltaTime;
        if (_dbgTimer < debugLogEvery) return;
        _dbgTimer = 0f;

        Vector3 cf = Vector3.ProjectOnPlane(camFwd, Vector3.up).normalized;
        float spineYaw = _spine     != null ? Vector3.SignedAngle(cf, Vector3.ProjectOnPlane(_spine.forward,     Vector3.up).normalized, Vector3.up) : 0f;
        float handYaw  = _rightHand != null ? Vector3.SignedAngle(cf, Vector3.ProjectOnPlane(_rightHand.forward, Vector3.up).normalized, Vector3.up) : 0f;
        float bodyYaw  = Vector3.SignedAngle(cf, Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized, Vector3.up);
        Debug.Log($"[AIM DEBUG] rigWeight={_weight:F2} | spineVsCam={spineYaw:F1}°  handVsCam={handYaw:F1}°  bodyVsCam={bodyYaw:F1}°  (+ right / - left)");
    }
}
