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
    bool  _locked;

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
            _currentSpeed = 0f;
            _anim.SetFloat(HashVelX, 0f, animDampTime, Time.deltaTime);
            _anim.SetFloat(HashVelZ, 0f, animDampTime, Time.deltaTime);
            return;
        }

        float h         = Input.GetAxis("Horizontal");
        float v         = Input.GetAxis("Vertical");
        bool  isRunning = !_crouching
                       && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));

        Vector2 input = new Vector2(h, v);
        if (input.sqrMagnitude > 1f) input.Normalize();

        Vector3 camFwd   = Vector3.ProjectOnPlane(_camera.transform.forward, Vector3.up).normalized;
        Vector3 camRight = Vector3.ProjectOnPlane(_camera.transform.right,   Vector3.up).normalized;
        Vector3 inputDir = camFwd * input.y + camRight * input.x;
        bool    hasInput = inputDir.sqrMagnitude > 0.01f;

        float maxSpd      = _crouching ? crouchSpeed : (isRunning ? runSpeed : walkSpeed);
        float targetSpeed = hasInput ? maxSpd : 0f;
        float ramp        = _currentSpeed < targetSpeed ? acceleration : deceleration;
        _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, ramp * Time.deltaTime);

        float prevYaw = transform.eulerAngles.y;
        if (hasInput)
        {
            float targetYaw = Quaternion.LookRotation(inputDir.normalized).eulerAngles.y;
            float newYaw    = Mathf.SmoothDampAngle(prevYaw, targetYaw,
                                  ref _yawVelocity, rotationSmoothTime, maxTurnSpeed);
            transform.rotation = Quaternion.Euler(0f, newYaw, 0f);
        }

        if (_currentSpeed > 0.001f)
            _cc.Move(transform.forward * _currentSpeed * Time.deltaTime);

        // VelocityZ: crouch → 0..0.5 range;  stand → 0(idle) 0.5(walk) 1.0(run)
        float velZ;
        if (_currentSpeed < 0.001f)
        {
            velZ = 0f;
        }
        else if (_crouching)
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

        // VelocityX: angular velocity → lean/bank + crouch left-right blend
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
        if (!_cc.enabled) return;
        if (_cc.isGrounded && _verticalSpeed < 0f)
            _verticalSpeed = -2f;
        _verticalSpeed -= 20f * Time.deltaTime;
        _cc.Move(new Vector3(0f, _verticalSpeed, 0f) * Time.deltaTime);
    }
}
