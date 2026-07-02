using UnityEngine;
using UnityEngine.Animations.Rigging;

/// <summary>
/// Procedural pistol recoil driven entirely through the existing aim IK — no animation
/// layer — kept in exact sync with the pistol model's own "Fire" clip (slide/hammer/trigger).
///
/// On left-click while aiming a single shared timeline of <see cref="recoilDuration"/>
/// seconds runs. Over that timeline:
///   • the two hand IK grip targets kick UP/BACK along <see cref="recoilCurve"/>, then settle;
///   • the pistol's <see cref="fireClip"/> is sampled time-scaled so its WHOLE length plays
///     across the same duration — so the gun mechanism and the hand recoil start and finish
///     together, and the pistol animation's total duration is exactly the IK recoil duration.
///
/// Because both hand targets get the same local offset the gun stays rigid and just kicks —
/// only the hands/gun move, the rest of the body keeps doing locomotion.
/// Lives on the Player next to <see cref="PlayerController"/>.
/// </summary>
[RequireComponent(typeof(PlayerController))]
public class PistolRecoil : MonoBehaviour
{
    [Header("IK targets (auto-found from the arm IK if empty)")]
    public TwoBoneIKConstraint rightArmIK;
    public TwoBoneIKConstraint leftArmIK;

    [Header("Pistol model (auto-found by name 'Pistol' if empty)")]
    [Tooltip("The pistol GameObject whose 'Fire' clip drives the slide/hammer/trigger.")]
    public Transform pistol;
    [Tooltip("The pistol's 'Fire' AnimationClip. Its full length is replayed over 'recoilDuration'.")]
    public AnimationClip fireClip;

    [Header("Timing — shared by the hand kick AND the pistol clip")]
    [Tooltip("Total duration of one shot's recoil. The whole Fire clip is time-scaled to fit exactly this.")]
    public float recoilDuration = 0.25f;
    [Tooltip("Hand kick amount over the timeline (0 = rest, 1 = full kick). Up then back to 0.")]
    public AnimationCurve recoilCurve = new AnimationCurve(
        new Keyframe(0f, 0f), new Keyframe(0.18f, 1f), new Keyframe(1f, 0f));

    [Header("Recoil distance (target local space — follows where you aim)")]
    [Tooltip("How far the hands kick UP at full kick (metres).")]
    public float kickUp   = 0.05f;
    [Tooltip("How far the hands kick BACK toward the body at full kick (metres).")]
    public float kickBack = 0.035f;

    [Header("Fire")]
    [Tooltip("Only fire while aiming (right-click / Ctrl-toggle), matching when the pistol is drawn.")]
    public bool requireAiming = true;

    PlayerController _player;
    Transform _rightTarget, _leftTarget;
    Vector3   _rightRest,   _leftRest;
    float     _t = -1f;     // recoil time; <0 = idle

    static TwoBoneIKConstraint FindIK(Transform root, string name)
    {
        var t = root.Find("AimRig/" + name);
        return t != null ? t.GetComponent<TwoBoneIKConstraint>() : null;
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
        if (rightArmIK == null) rightArmIK = FindIK(transform, "RightArmIK");
        if (leftArmIK  == null) leftArmIK  = FindIK(transform, "LeftArmIK");
        if (pistol     == null) pistol     = FindDeep(transform, "Pistol");

        _rightTarget = rightArmIK != null ? rightArmIK.data.target : null;
        _leftTarget  = leftArmIK  != null ? leftArmIK.data.target  : null;
        if (_rightTarget != null) _rightRest = _rightTarget.localPosition;
        if (_leftTarget  != null) _leftRest  = _leftTarget.localPosition;

        if (_rightTarget == null && _leftTarget == null)
            Debug.LogWarning("[PistolRecoil] No hand IK targets found — hand recoil disabled.");
        if (fireClip == null)
            Debug.LogWarning("[PistolRecoil] No fireClip assigned — pistol mechanism won't animate.");
    }

    void Fire() => _t = 0f;   // (re)start the shared timeline

    void Update()
    {
        bool canFire = !requireAiming || _player.IsAiming;
        if (canFire && Input.GetMouseButtonDown(0)) Fire();
    }

    // LateUpdate: the IK solves inside the Animator pass, so feeding the targets here (a
    // one-frame delay) is imperceptible — same pattern AimRigController uses for the aim target.
    void LateUpdate()
    {
        if (_t < 0f) return;                  // idle: leave hands/pistol at rest
        _t += Time.deltaTime;
        float p = Mathf.Clamp01(recoilDuration > 0f ? _t / recoilDuration : 1f);

        // Hands: kick along the curve.
        Vector3 off = new Vector3(0f, kickUp, -kickBack) * recoilCurve.Evaluate(p);
        if (_rightTarget != null) _rightTarget.localPosition = _rightRest + off;
        if (_leftTarget  != null) _leftTarget.localPosition  = _leftRest  + off;

        // Pistol mechanism: replay the whole Fire clip across the same timeline.
        if (fireClip != null && pistol != null)
            fireClip.SampleAnimation(pistol.gameObject, p * fireClip.length);

        if (p >= 1f)
        {
            // Settle everything back to rest and go idle.
            if (_rightTarget != null) _rightTarget.localPosition = _rightRest;
            if (_leftTarget  != null) _leftTarget.localPosition  = _leftRest;
            if (fireClip != null && pistol != null) fireClip.SampleAnimation(pistol.gameObject, 0f);
            _t = -1f;
        }
    }
}
