using UnityEngine;

[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed    = 2f;
    public float runSpeed     = 5f;
    public float crouchSpeed  = 1.2f;
    public float acceleration = 10f;
    public float deceleration = 14f;

    [Header("Rotation")]
    public float rotationSmoothTime = 0.1f;
    public float maxTurnSpeed       = 360f;

    [Header("Input Smoothing")]
    [Tooltip("How quickly the movement input ramps to the keys you're pressing. Higher = snappier, lower = floatier. Smooths rapid taps and direction flips.")]
    public float inputSmoothTime = 0.12f;

    [Header("Lean / Bank")]
    [Tooltip("Turn rate (deg/sec) that maps to a full lean (VelocityX = ±1).")]
    public float leanReferenceTurnRate = 200f;
    [Tooltip("How quickly the lean eases in and out. Higher = floatier lean.")]
    public float leanSmoothTime = 0.14f;

    [Header("Animation")]
    public float animDampTime = 0.08f;

    [Header("Aim (hold right-click)")]
    [Tooltip("Move speed while aiming a pistol (slow strafe locomotion).")]
    public float aimSpeed = 1.6f;
    [Tooltip("How tightly the body tracks the camera while aiming (seconds). Lower = snappier/more in-sync, higher = floatier. Drives the over-shoulder turn-with-camera.")]
    public float aimTurnSmoothTime = 0.06f;
    [Tooltip("How much movement bleeds into the aim stance (0 = locked stand-aim, hips stay stable; 1 = full strafe). Low values keep the hips steady like a planted aim and let only the upper body move a little while moving.")]
    [Range(0f, 1f)] public float aimMoveBlend = 0.4f;

    CharacterController _cc;
    Animator            _anim;
    Camera              _camera;
    CameraFollow        _camFollow;

    float   _yawVelocity;
    float   _verticalSpeed;
    float   _currentSpeed;
    bool    _locked;

    Vector2 _smoothInput;
    Vector2 _smoothInputVel;
    float   _velX;          // smoothed lean fed to the animator
    float   _velXVel;

    bool _crouching;
    bool _aiming;
    bool _aimToggle;       // Ctrl-toggled aim (hands-free, for tuning)
    bool _coverAiming;     // CoverController is peeking — aim the gun while controls are locked

    public bool IsCrouching => _crouching;
    // Cover peek aims the gun while the player is locked, so OR it in: the aim rig draws the
    // pistol and twists the torso toward the camera, while the animator's IsAiming bool stays
    // false so the base layer keeps the cover pose (the masked aim layer supplies the arms).
    public bool IsAiming    => _aiming || _coverAiming;

    // Called by CoverController while peeking from cover. Independent of the normal aim input,
    // which is suppressed by LockControls() during cover.
    public void SetCoverAiming(bool on) => _coverAiming = on;

    static readonly int HashVelX        = Animator.StringToHash("VelocityX");
    static readonly int HashVelZ        = Animator.StringToHash("VelocityZ");
    static readonly int HashIsCrouching = Animator.StringToHash("IsCrouching");
    static readonly int HashIsAiming    = Animator.StringToHash("IsAiming");

    void Awake()
    {
        _cc       = GetComponent<CharacterController>();
        _anim     = GetComponent<Animator>();
        _camera   = GameManager.Instance.MainCamera;
        _camFollow = _camera != null ? _camera.GetComponent<CameraFollow>() : null;
        _anim.applyRootMotion = false;
    }

    void Update()
    {
        HandleAim();
        HandleCrouch();
        HandleMovement();
        ApplyGravity();
    }

    public void LockControls()   { _locked = true; UpdateAiming(false); }
    public void UnlockControls() { _locked = false; }

    // Hold right-click to aim, OR press Ctrl to toggle aim on/off (hands-free — handy
    // for tuning values in the inspector while staying in aim). Aiming and crouching are
    // mutually exclusive — entering aim cancels crouch so the animator can move into the
    // pistol locomotion state.
    void HandleAim()
    {
        if (_locked) { _aimToggle = false; UpdateAiming(false); return; }

        if (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl))
            _aimToggle = !_aimToggle;

        UpdateAiming(Input.GetMouseButton(1) || _aimToggle);
    }

    void UpdateAiming(bool on)
    {
        if (_aiming == on) return;
        _aiming = on;
        if (on && _crouching) { _crouching = false; _anim.SetBool(HashIsCrouching, false); }
        _anim.SetBool(HashIsAiming, on);
    }

    void HandleCrouch()
    {
        if (_locked || _aiming) return;
        if (Input.GetKeyDown(KeyCode.C))
        {
            _crouching = !_crouching;
            _anim.SetBool(HashIsCrouching, _crouching);
        }
    }

    void HandleMovement()
    {
        if (_locked)
        {
            _currentSpeed   = 0f;
            _smoothInput    = Vector2.zero;
            _smoothInputVel = Vector2.zero;
            _velX           = Mathf.SmoothDamp(_velX, 0f, ref _velXVel, leanSmoothTime);
            _anim.SetFloat(HashVelX, _velX, animDampTime, Time.deltaTime);
            _anim.SetFloat(HashVelZ, 0f,    animDampTime, Time.deltaTime);
            return;
        }

        bool isRunning = !_crouching
                       && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

        // Raw input drives speed (snappy start/stop); a smoothed copy drives facing.
        Vector2 rawInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        if (rawInput.sqrMagnitude > 1f) rawInput.Normalize();
        bool hasInput = rawInput.sqrMagnitude > 0.01f;

        _smoothInput = Vector2.SmoothDamp(_smoothInput, rawInput, ref _smoothInputVel, inputSmoothTime);
        if (_smoothInput.sqrMagnitude < 0.000001f) _smoothInput = Vector2.zero;

        Vector3 camFwd   = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
        Vector3 camRight = Vector3.ProjectOnPlane(_camera.transform.right,   Vector3.up).normalized;

        // Aiming uses a completely different model: face the camera and strafe.
        if (_aiming) { HandleAimMovement(camFwd, camRight, hasInput); return; }

        Vector3 faceDir  = camFwd * _smoothInput.y + camRight * _smoothInput.x;

        float maxSpd      = _crouching ? crouchSpeed : (isRunning ? runSpeed : walkSpeed);
        float targetSpeed = hasInput ? maxSpd : 0f;
        float ramp        = _currentSpeed < targetSpeed ? acceleration : deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, ramp * Time.deltaTime);

        // Rotate toward the *smoothed* direction so rapid taps / 180° flips stay fluid.
        float prevYaw = transform.eulerAngles.y;
        if (faceDir.sqrMagnitude > 0.0025f)
        {
            float targetYaw = Quaternion.LookRotation(faceDir.normalized).eulerAngles.y;
            float newYaw    = Mathf.SmoothDampAngle(prevYaw, targetYaw,
                                  ref _yawVelocity, rotationSmoothTime, maxTurnSpeed);
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
        }

        if (_currentSpeed > 0.001f)
            _cc.Move(transform.forward * _currentSpeed * Time.deltaTime);

        // VelocityZ: crouch → 0..0.5 range;  stand → 0(idle) 0.5(walk) 1.0(run)
        float velZ;
        if (_currentSpeed < 0.001f)
            velZ = 0f;
        else if (_crouching)
            velZ = (_currentSpeed / crouchSpeed) * 0.5f;
        else if (_currentSpeed <= walkSpeed)
            velZ = (_currentSpeed / walkSpeed) * 0.5f;
        else
            velZ = 0.5f + ((_currentSpeed - walkSpeed) / (runSpeed - walkSpeed)) * 0.5f;

        // VelocityX: lean/bank from actual angular velocity (deg/sec), eased in & out.
        // No per-frame max-turn normalization, so hard turns no longer slam to ±1.
        float yawDelta  = Mathf.DeltaAngle(prevYaw, transform.eulerAngles.y);
        float turnRate  = Time.deltaTime > 0.0001f ? yawDelta / Time.deltaTime : 0f;
        float targetVelX = _currentSpeed > 0.1f && leanReferenceTurnRate > 0.001f
                           ? Mathf.Clamp(-turnRate / leanReferenceTurnRate, -1f, 1f)
                           : 0f;
        _velX = Mathf.SmoothDamp(_velX, targetVelX, ref _velXVel, leanSmoothTime);

        _anim.SetFloat(HashVelX, _velX, animDampTime, Time.deltaTime);
        _anim.SetFloat(HashVelZ, velZ,  animDampTime, Time.deltaTime);
    }

    // Pistol-aim locomotion: the body turns WITH the camera and the player strafes in any
    // direction. VelocityX/VelocityZ become the strafe axes (right / forward, each -1..1)
    // feeding the 8-direction PistolLocomotion blend tree.
    void HandleAimMovement(Vector3 camFwd, Vector3 camRight, bool hasInput)
    {
        // Aim yaw = exactly where the camera looks THIS frame. Reading the camera's shared,
        // mouse-filtered AimYaw (instead of its transform, which is a frame behind) keeps the
        // player and camera locked together with no chase lag.
        float aimYaw = _camFollow != null
                     ? _camFollow.AimYaw
                     : (camFwd.sqrMagnitude > 0.0001f
                            ? Quaternion.LookRotation(camFwd).eulerAngles.y
                            : transform.eulerAngles.y);

        // Over-shoulder aim: the body turns to face the camera every frame — moving OR idle —
        // so the player rotates in sync with the camera and the gun stays under the crosshair.
        // (The torso aim rig then only adds pitch + a small offset on top.)
        float newYaw = Mathf.SmoothDampAngle(transform.eulerAngles.y, aimYaw,
                           ref _yawVelocity, aimTurnSmoothTime, maxTurnSpeed);
        transform.rotation = Quaternion.Euler(0f, newYaw, 0f);

        // Camera-relative move direction (can be sideways / backward relative to facing).
        Vector3 moveDir   = camFwd * _smoothInput.y + camRight * _smoothInput.x;
        float targetSpeed = hasInput ? aimSpeed : 0f;
        float ramp        = _currentSpeed < targetSpeed ? acceleration : deceleration;
        _currentSpeed     = Mathf.MoveTowards(_currentSpeed, targetSpeed, ramp * Time.deltaTime);

        if (_currentSpeed > 0.001f && moveDir.sqrMagnitude > 0.0001f)
            _cc.Move(moveDir.normalized * _currentSpeed * Time.deltaTime);

        // Project the intended move onto the body axes → strafe blend params. The blend is
        // scaled by aimMoveBlend so the lower body stays near the planted stand-aim pose
        // (stable hips) and only eases a little toward the strafe while moving — the player
        // still travels at full aimSpeed, only the *animation* lean is damped.
        float velZ = Vector3.Dot(moveDir, transform.forward) * aimMoveBlend;   // forward(+) / backward(-)
        float velX = Vector3.Dot(moveDir, transform.right)   * aimMoveBlend;   // right(+)   / left(-)
        _anim.SetFloat(HashVelX, velX, animDampTime, Time.deltaTime);
        _anim.SetFloat(HashVelZ, velZ, animDampTime, Time.deltaTime);
    }

    void ApplyGravity()
    {
        if (!_cc.enabled) return;
        if (_cc.isGrounded && _verticalSpeed < 0f)
            _verticalSpeed = -2f;
        _verticalSpeed -= 20f * Time.deltaTime;
        _cc.Move(new Vector3(0f, _verticalSpeed, 0f) * Time.deltaTime);
    }
}
