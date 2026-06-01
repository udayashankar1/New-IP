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

    [Header("Animation")]
    public float animDampTime = 0.08f;

    CharacterController _cc;
    Animator            _anim;
    Camera              _camera;

    float _yawVelocity;
    float _verticalSpeed;
    float _currentSpeed;

    enum CrouchPhase { Standing, EnteringCrouch, Crouching, ExitingCrouch }
    CrouchPhase _crouchPhase = CrouchPhase.Standing;

    public bool IsCrouching => _crouchPhase == CrouchPhase.Crouching;

    static readonly int HashVelX = Animator.StringToHash("VelocityX");
    static readonly int HashVelZ = Animator.StringToHash("VelocityZ");

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

    void HandleCrouch()
    {
        bool pressedC = Input.GetKeyDown(KeyCode.C);

        switch (_crouchPhase)
        {
            case CrouchPhase.Standing:
                if (pressedC)
                {
                    // Play "Crouch To Stand" reversed (speed=-1, cycleOffset=1 set in animator)
                    _anim.CrossFadeInFixedTime("CrouchEnter", 0.05f);
                    _crouchPhase = CrouchPhase.EnteringCrouch;
                }
                break;

            case CrouchPhase.EnteringCrouch:
                if (pressedC)
                {
                    // Cancel: quickly blend back to standing
                    _anim.CrossFadeInFixedTime("Locomotion", 0.2f);
                    _crouchPhase = CrouchPhase.Standing;
                    break;
                }
                // Wait until reversed animation finishes (normalizedTime counts down from 1 → 0)
                if (!_anim.IsInTransition(0))
                {
                    var info = _anim.GetCurrentAnimatorStateInfo(0);
                    if (info.IsName("CrouchEnter") && info.normalizedTime <= 0.05f)
                    {
                        _anim.CrossFadeInFixedTime("CrouchLocomotion", 0.1f);
                        _crouchPhase = CrouchPhase.Crouching;
                    }
                }
                break;

            case CrouchPhase.Crouching:
                if (pressedC)
                {
                    // Play "Crouch To Stand" forward (crouch → stand)
                    _anim.CrossFadeInFixedTime("CrouchExit", 0.05f);
                    _crouchPhase = CrouchPhase.ExitingCrouch;
                }
                break;

            case CrouchPhase.ExitingCrouch:
                // Animator drives CrouchExit → Locomotion via hasExitTime.
                // Track when we're fully back in Locomotion.
                if (!_anim.IsInTransition(0))
                {
                    var info = _anim.GetCurrentAnimatorStateInfo(0);
                    if (!info.IsName("CrouchExit"))
                        _crouchPhase = CrouchPhase.Standing;
                }
                break;
        }
    }

    void HandleMovement()
    {
        bool isCrouching   = _crouchPhase == CrouchPhase.Crouching;
        bool lockMovement  = _crouchPhase == CrouchPhase.EnteringCrouch
                          || _crouchPhase == CrouchPhase.ExitingCrouch;

        float h         = Input.GetAxis("Horizontal");
        float v         = Input.GetAxis("Vertical");
        bool  isRunning = !isCrouching
                       && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

        Vector2 input = new Vector2(h, v);
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector3 camFwd   = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
        Vector3 camRight = Vector3.ProjectOnPlane(_camera.transform.right,   Vector3.up).normalized;
        Vector3 inputDir = camFwd * input.y + camRight * input.x;
        bool    hasInput = !lockMovement && inputDir.sqrMagnitude > 0.01f;

        // Smooth acceleration / deceleration
        float maxSpd      = isCrouching ? crouchSpeed : (isRunning ? runSpeed : walkSpeed);
        float targetSpeed = hasInput ? maxSpd : 0f;
        float ramp        = _currentSpeed < targetSpeed ? acceleration : deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, ramp * Time.deltaTime);

        // Rotate toward input direction
        float prevYaw = transform.eulerAngles.y;
        if (hasInput)
        {
            float targetYaw = Quaternion.LookRotation(inputDir.normalized).eulerAngles.y;
            float newYaw    = Mathf.SmoothDampAngle(prevYaw, targetYaw,
                                  ref _yawVelocity, rotationSmoothTime, maxTurnSpeed);
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
        }

        // Move along facing direction (creates natural arc through turns)
        if (_currentSpeed > 0.001f)
            _cc.Move(transform.forward * _currentSpeed * Time.deltaTime);

        // VelocityZ: crouch → 0..0.5 range;  stand → 0(idle) 0.5(walk) 1.0(run)
        float velZ;
        if (_currentSpeed < 0.001f)
        {
            velZ = 0f;
        }
        else if (isCrouching)
        {
            velZ = (_currentSpeed / crouchSpeed) * 0.5f;
        }
        else if (_currentSpeed <= walkSpeed)
        {
            velZ = (_currentSpeed / walkSpeed) * 0.5f;
        }
        else
        {
            velZ = 0.5f + ((_currentSpeed - walkSpeed) / (runSpeed - walkSpeed)) * 0.5f;
        }

        // VelocityX: angular velocity this frame → lean/bank + crouch left-right blend
        float yawDelta = Mathf.DeltaAngle(prevYaw, transform.eulerAngles.y);
        float maxYaw   = maxTurnSpeed * Time.deltaTime;
        float velX     = _currentSpeed > 0.01f && maxYaw > 0.001f
                         ? Mathf.Clamp(-yawDelta / maxYaw, -1f, 1f)
                         : 0f;

        _anim.SetFloat(HashVelX, velX, animDampTime, Time.deltaTime);
        _anim.SetFloat(HashVelZ, velZ, animDampTime, Time.deltaTime);
    }

    void ApplyGravity()
    {
        if (_cc.isGrounded && _verticalSpeed < 0f)
            _verticalSpeed = -2f;
        _verticalSpeed -= 20f * Time.deltaTime;
        _cc.Move(new Vector3(0f, _verticalSpeed, 0f) * Time.deltaTime);
    }
}
