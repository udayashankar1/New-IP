using UnityEngine;

// Runs before PlayerController so the aim yaw is updated first and the body can lock to
// the SAME value within the frame (camera + player stay in sync, no one-frame chase lag).
[DefaultExecutionOrder(-100)]
public class CameraFollow : MonoBehaviour
{
    public enum CamMode { OnFoot, InCar }

    [Header("Mode")]
    public CamMode mode = CamMode.OnFoot;

    [Header("Target")]
    public Transform target;
    public Rigidbody targetRigidbody;

    [Header("On Foot — Distance & Height")]
    [Range(1f, 12f)]  public float onFootDistance = 4f;
    [Range(0f,  6f)]  public float onFootHeight   = 2f;

    [Header("In Car — Distance & Height")]
    [Range(3f, 20f)]  public float inCarDistance  = 8f;
    [Range(1f,  8f)]  public float inCarHeight    = 4f;

    [Header("Aim — Over-Shoulder")]
    [Tooltip("Camera distance while aiming (zoomed in).")]
    [Range(0.8f, 5f)] public float aimDistance      = 1.8f;
    [Range(0.5f, 4f)] public float aimHeight        = 1.6f;
    [Tooltip("How far to push the camera RIGHT so the player sits on the LEFT and screen-centre is the aim point.")]
    [Range(0f, 1.5f)] public float aimShoulderOffset = 0.6f;
    [Tooltip("Field of view while aiming (lower = more zoom).")]
    [Range(20f, 70f)] public float aimFov           = 40f;
    [Tooltip("How fast the camera eases into / out of the aim framing.")]
    [Range(2f, 20f)]  public float aimLerpSpeed     = 10f;

    [Header("Zoom (scroll wheel)")]
    [Range(1f, 20f)]  public float zoomSpeed    = 4f;
    [Range(-5f, 0f)]  public float zoomMinDelta  = -2f;
    [Range(0f, 10f)]  public float zoomMaxDelta  =  3f;

    [Header("Smoothing")]
    [Range(1f, 20f)]  public float positionSmooth = 8f;
    [Range(1f, 20f)]  public float distanceSmooth = 5f;
    [Tooltip("Camera follow smoothing while aiming (seconds). Small = snappy. Filters the player's per-frame gravity/IK micro-jitter so the aim camera doesn't shake.")]
    [Range(0.01f, 0.2f)] public float aimPositionSmoothTime = 0.04f;
    [Tooltip("Aim look smoothing (seconds). Small = responsive. Filters raw mouse noise so the crosshair doesn't shake while you rotate.")]
    [Range(0f, 0.15f)]   public float aimLookSmoothTime     = 0.03f;

    [Header("Mouse")]
    [Range(0.5f, 10f)] public float mouseSensitivity = 3f;
    public bool invertY = false;
    [Range(-30f, 0f)]  public float minPitch = -5f;
    [Range(10f, 85f)]  public float maxPitch = 55f;
    [Range(0f, 60f)]   public float defaultPitch = 15f;

    [Header("InCar — Auto-Align")]
    [Range(0.5f,  5f)] public float autoAlignDelay    = 2f;
    [Range(1f,  20f)]  public float autoAlignSpeed    = 5f;
    [Range(0f,  10f)]  public float autoAlignMinSpeed = 2f;

    float _yaw;
    float _pitch;
    float _mouseIdleTimer;
    float _currentDistance;
    float _currentHeight;
    float _currentShoulder;
    float _zoomDelta;

    Vector3 _smoothPivot;  // eased pivot the camera orbits (player + height); only TRANSLATION is smoothed
    Vector3 _posVel;       // SmoothDamp velocity for the pivot follow
    float   _smoothYaw;    // mouse-filtered yaw/pitch used while aiming
    float   _smoothPitch;
    float   _yawSmoothVel;
    float   _pitchSmoothVel;

    Camera _cam;
    float  _defaultFov;

    void Awake()
    {
        transform.SetParent(null);
        _pitch           = defaultPitch;
        _currentDistance = onFootDistance;
        _currentHeight   = onFootHeight;
        _cam             = GetComponent<Camera>();
        _defaultFov      = _cam != null ? _cam.fieldOfView : 60f;

        if (target != null)
        {
            _yaw = target.eulerAngles.y;
            if (targetRigidbody == null)
                targetRigidbody = target.GetComponent<Rigidbody>();
        }

        _smoothYaw   = _yaw;
        _smoothPitch = _pitch;

        if (target != null)
            _smoothPivot = target.position + Vector3.up * _currentHeight;
    }

    /// <summary>
    /// The yaw (deg) the camera is looking along this frame — mouse-filtered while aiming.
    /// PlayerController reads this to face the body exactly where the camera points, so the
    /// player and camera turn together. Updated in <see cref="Update"/> (before
    /// PlayerController, via the execution-order attribute) so there is no one-frame lag.
    /// </summary>
    public float AimYaw => _smoothYaw;

    bool IsAiming() => mode == CamMode.OnFoot
                    && GameManager.Instance.Player != null
                    && GameManager.Instance.Player.IsAiming;

    void Update()
    {
        HandleOrbit();
        HandleZoom();

        // Filter the look while aiming so raw mouse noise doesn't shake the crosshair, and
        // so the body (which locks to AimYaw) follows a smooth value. Done here in Update —
        // before PlayerController — so both read the same up-to-date yaw this frame.
        if (IsAiming())
        {
            _smoothYaw   = Mathf.SmoothDampAngle(_smoothYaw,   _yaw,   ref _yawSmoothVel,   aimLookSmoothTime);
            _smoothPitch = Mathf.SmoothDampAngle(_smoothPitch, _pitch, ref _pitchSmoothVel, aimLookSmoothTime);
        }
        else
        {
            _smoothYaw = _yaw; _smoothPitch = _pitch;
            _yawSmoothVel = _pitchSmoothVel = 0f;
        }
    }

    void LateUpdate()
    {
        if (target == null) return;

        // Over-shoulder aim framing when the player is aiming on foot.
        bool aiming = IsAiming();

        float targetDist     = aiming ? aimDistance : (mode == CamMode.InCar ? inCarDistance : onFootDistance) + _zoomDelta;
        float targetHgt      = aiming ? aimHeight   : (mode == CamMode.InCar ? inCarHeight   : onFootHeight);
        float targetShoulder = aiming ? aimShoulderOffset : 0f;

        _currentDistance = Mathf.Lerp(_currentDistance, targetDist,     distanceSmooth * Time.deltaTime);
        _currentHeight   = Mathf.Lerp(_currentHeight,   targetHgt,      distanceSmooth * Time.deltaTime);
        _currentShoulder = Mathf.Lerp(_currentShoulder, targetShoulder, aimLerpSpeed   * Time.deltaTime);

        if (_cam != null)
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, aiming ? aimFov : _defaultFov, aimLerpSpeed * Time.deltaTime);

        if (mode == CamMode.InCar)
        {
            _mouseIdleTimer += Time.deltaTime;
            if (_mouseIdleTimer >= autoAlignDelay)
            {
                float speed = targetRigidbody != null ? targetRigidbody.linearVelocity.magnitude : 0f;
                if (speed >= autoAlignMinSpeed)
                    _yaw = Mathf.LerpAngle(_yaw, target.eulerAngles.y, autoAlignSpeed * Time.deltaTime);
            }
        }

        // _smoothYaw / _smoothPitch are filtered (while aiming) in Update; off-aim they
        // track the raw values exactly. Re-sync here so the InCar auto-align tweak to _yaw
        // above (which happens after Update) is reflected without a one-frame lag.
        if (!aiming) { _smoothYaw = _yaw; _smoothPitch = _pitch; }
        Quaternion camRot = Quaternion.Euler(_smoothPitch, _smoothYaw, 0f);

        // The camera orbits a PIVOT at the player's chest/head (root + height). Smooth ONLY the
        // pivot's translation — this filters the player's per-frame gravity / footstep / IK
        // micro-jitter, which is what made the follow shake. The orbit itself is applied
        // RIGIDLY below, so rotating the mouse moves the camera around the pivot instantly with
        // no positional lag. (Smoothing the whole world position is what made rotation glitch:
        // the look was instant but the position lagged, so the view swam while turning.)
        Vector3 pivotTarget    = target.position + Vector3.up * _currentHeight;
        float   pivotSmoothTime = aiming ? aimPositionSmoothTime : 1f / Mathf.Max(positionSmooth, 0.01f);
        _smoothPivot = Vector3.SmoothDamp(_smoothPivot, pivotTarget, ref _posVel, pivotSmoothTime);

        // Rigid orbit: shoulder (X) pushes the camera right so the player sits left; distance
        // (−Z) pulls it back. Rotation = orbit direction, so screen-centre is where you look.
        Vector3 desiredPos = _smoothPivot + camRot * new Vector3(_currentShoulder, 0f, -_currentDistance);
        desiredPos.y = Mathf.Max(desiredPos.y, target.position.y + 0.5f);

        transform.position = desiredPos;
        // GTA-style: the camera ALWAYS looks along the orbit direction the mouse points —
        // identical in normal and aim, so entering / leaving aim never changes the look
        // direction (only the framing cross-fades). This is what removes the "look down when
        // aiming" jump: the pitch you built up in free-look is the SAME pitch the aim uses.
        transform.rotation = camRot;
    }

    void HandleOrbit()
    {
        float mx = Input.GetAxis("Mouse X");
        float my = Input.GetAxis("Mouse Y");

        _yaw   += mx * mouseSensitivity;

        // invertY = false (default): mouse up → camera rises (pitch decreases)
        // invertY = true           : mouse up → camera dips  (pitch increases)
        float pitchDelta = my * mouseSensitivity;
        _pitch += invertY ? pitchDelta : -pitchDelta;
        _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);

        if (Mathf.Abs(mx) > 0.01f || Mathf.Abs(my) > 0.01f)
            _mouseIdleTimer = 0f;
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        if (Mathf.Abs(scroll) > 0.01f)
        {
            _zoomDelta -= scroll * zoomSpeed;
            _zoomDelta  = Mathf.Clamp(_zoomDelta, zoomMinDelta, zoomMaxDelta);
        }
    }
}
