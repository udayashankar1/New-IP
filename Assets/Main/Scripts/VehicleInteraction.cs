using UnityEngine;

public class VehicleInteraction : MonoBehaviour
{
    public enum ControlMode { OnFoot, InCar }

    [Header("References")]
    public CarController car;
    public float         enterRadius = 3f;

    [Header("Exit Placement")]
    public Vector3 exitOffset = new Vector3(-2f, 0.5f, 0f);

    [Header("State (read-only)")]
    [SerializeField] ControlMode _mode = ControlMode.OnFoot;

    CharacterController _cc;
    PlayerController    _pc;
    Animator            _anim;
    CameraFollow        _cam;

    static readonly int HashDriving = Animator.StringToHash("IsDriving");
    static readonly int HashVelX    = Animator.StringToHash("VelocityX");
    static readonly int HashVelZ    = Animator.StringToHash("VelocityZ");

    void Awake()
    {
        _cc   = GetComponent<CharacterController>();
        _pc   = GetComponent<PlayerController>();
        _anim = GetComponent<Animator>();
        _cam  = Camera.main.GetComponent<CameraFollow>();
    }

    void Update()
    {
        if (!Input.GetKeyDown(KeyCode.E)) return;

        switch (_mode)
        {
            case ControlMode.OnFoot: TryEnter(); break;
            case ControlMode.InCar:  Exit();     break;
        }
    }

    void TryEnter()
    {
        if (car == null) return;
        if (Vector3.Distance(transform.position, car.transform.position) > enterRadius) return;
        SetMode(ControlMode.InCar);
    }

    void SetMode(ControlMode next)
    {
        switch (next)
        {
            case ControlMode.InCar:
                _cc.enabled = false;
                _pc.enabled = false;

                Transform seat = car.driverSeat != null ? car.driverSeat : car.transform;
                transform.SetParent(seat);
                transform.localPosition = Vector3.zero;
                transform.localRotation = Quaternion.identity;

                _anim.SetFloat(HashVelX, 0f);
                _anim.SetFloat(HashVelZ, 0f);
                _anim.SetBool(HashDriving, true);

                car.SetPlayerControlled(true);

                _cam.target          = car.transform;
                _cam.targetRigidbody = car.GetComponent<Rigidbody>();
                _cam.mode            = CameraFollow.CamMode.InCar;
                break;

            case ControlMode.OnFoot:
                transform.SetParent(null);
                transform.position = car.transform.TransformPoint(exitOffset);
                transform.rotation = car.transform.rotation;

                _cc.enabled = true;
                _pc.enabled = true;

                _anim.SetBool(HashDriving, false);

                car.SetPlayerControlled(false);

                _cam.target          = transform;
                _cam.targetRigidbody = null;
                _cam.mode            = CameraFollow.CamMode.OnFoot;
                break;
        }

        _mode = next;
    }

    void Exit() => SetMode(ControlMode.OnFoot);

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, enterRadius);
    }
}
