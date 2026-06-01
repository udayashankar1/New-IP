using UnityEngine;

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

    [Header("Zoom (scroll wheel)")]
    [Range(1f, 20f)]  public float zoomSpeed    = 4f;
    [Range(-5f, 0f)]  public float zoomMinDelta  = -2f;
    [Range(0f, 10f)]  public float zoomMaxDelta  =  3f;

    [Header("Smoothing")]
    [Range(1f, 20f)]  public float positionSmooth = 8f;
    [Range(1f, 20f)]  public float distanceSmooth = 5f;

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
    float _zoomDelta;

    void Awake()
    {
        transform.SetParent(null);
        _pitch           = defaultPitch;
        _currentDistance = onFootDistance;
        _currentHeight   = onFootHeight;

        if (target != null)
        {
            _yaw = target.eulerAngles.y;
            if (targetRigidbody == null)
                targetRigidbody = target.GetComponent<Rigidbody>();
        }
    }

    void Update()
    {
        HandleOrbit();
        HandleZoom();
    }

    void LateUpdate()
    {
        if (target == null) return;

        float targetDist = (mode == CamMode.InCar ? inCarDistance : onFootDistance) + _zoomDelta;
        float targetHgt  =  mode == CamMode.InCar ? inCarHeight   : onFootHeight;
        _currentDistance = Mathf.Lerp(_currentDistance, targetDist, distanceSmooth * Time.deltaTime);
        _currentHeight   = Mathf.Lerp(_currentHeight,   targetHgt,  distanceSmooth * Time.deltaTime);

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

        Quaternion camRot    = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3    desiredPos = target.position + camRot * new Vector3(0f, _currentHeight, -_currentDistance);
        desiredPos.y = Mathf.Max(desiredPos.y, target.position.y + 0.5f);

        transform.position = Vector3.Lerp(transform.position, desiredPos, positionSmooth * Time.deltaTime);
        transform.LookAt(target.position + Vector3.up * 1.2f);
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
