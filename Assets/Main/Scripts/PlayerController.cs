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

    CharacterController _cc;
    Animator            _anim;
    Camera              _camera;

    float   _yawVelocity;
    float   _verticalSpeed;
    float   _currentSpeed;
    bool    _locked;

    Vector2 _smoothInput;
    Vector2 _smoothInputVel;
    float   _velX;          // smoothed lean fed to the animator
    float   _velXVel;

    bool _crouching;

    public bool IsCrouching => _crouching;

    static readonly int HashVelX        = Animator.StringToHash("VelocityX");
    static readonly int HashVelZ        = Animator.StringToHash("VelocityZ");
    static readonly int HashIsCrouching = Animator.StringToHash("IsCrouching");

    void Awake()
    {
        _cc     = GetComponent<CharacterController>();
        _anim   = GetComponent<Animator>();
        _camera = Camera.main;
        _anim.applyRootMotion = false;
    }

    void Update()
    {
        HandleCrouch();
        HandleMovement();
        ApplyGravity();
    }

    public void LockControls()   { _locked = true; }
    public void UnlockControls() { _locked = false; }

    void HandleCrouch()
    {
        if (_locked) return;
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

    void ApplyGravity()
    {
        if (!_cc.enabled) return;
        if (_cc.isGrounded && _verticalSpeed < 0f)
            _verticalSpeed = -2f;
        _verticalSpeed -= 20f * Time.deltaTime;
        _cc.Move(new Vector3(0f, _verticalSpeed, 0f) * Time.deltaTime);
    }
}
