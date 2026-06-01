using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Wheel Colliders")]
    public WheelCollider wheelFL;
    public WheelCollider wheelFR;
    public WheelCollider wheelRL;
    public WheelCollider wheelRR;

    [Header("Wheel Meshes")]
    public Transform meshFL;
    public Transform meshFR;
    public Transform meshRL;
    public Transform meshRR;
    public Transform steeringWheel;
    public float steeringWheelMaxAngle = 180f;
    private Quaternion initialSteeringWheelRot;
    private Transform steeringPivot;

    [Header("Drive Settings")]
    public DriveMode driveMode = DriveMode.RWD;
    [Range(500f, 6000f)] public float maxMotorTorque = 2500f;
    [Range(0f, 60f)]     public float maxSpeed = 40f;
    [Range(20f, 50f)]    public float maxSteerAngle = 35f;
    public float brakeTorque = 6000f;
    public float handBrakeTorque = 10000f;

    [Header("Suspension")]
    public float suspensionDistance = 0.2f;
    public float springForce = 42000f;
    public float damperForce = 5000f;
    [Range(0f, 1f)] public float targetPosition = 0.5f;

    [Header("Anti-Roll Bar")]
    public float antiRollForce = 5000f;

    [Header("Friction (Base)")]
    public float forwardStiffness = 1.5f;
    public float sidewaysStiffness = 1.8f;

    [Header("Drift / Slip (GTA-style)")]
    [Tooltip("Sideways stiffness when fully drifting (lower = more slip)")]
    [Range(0.1f, 1.2f)] public float driftSidewaysStiffness = 0.35f;
    [Tooltip("Lateral slip amount (from WheelHit) before grip starts reducing")]
    [Range(0.05f, 0.8f)] public float slipThreshold = 0.25f;
    [Tooltip("How fast grip recovers after slip (lose grip is 2.5x faster)")]
    [Range(1f, 10f)] public float driftRecoverySpeed = 4f;
    [Tooltip("Extra rear slip contribution when braking AND steering at speed")]
    [Range(0f, 1f)] public float brakeSteerDriftFactor = 0.65f;
    [Tooltip("Speed (km/h) where steering angle begins to reduce for stability")]
    [Range(0f, 40f)] public float steerReductionStartKmh = 20f;

    [Header("Vehicle Entry")]
    public Transform driverSeat;          // child transform — player snaps here on enter

    [Header("Body")]
    public Vector3 centerOfMassOffset = Vector3.zero;

    // ── internals ──────────────────────────────────────────────────────────
    Rigidbody _rb;
    float _motorInput;
    float _steerInput;
    bool  _handBrake;
    bool  _playerControlled;    // set true by VehicleInteraction on enter

    float _currentRearSideways;
    float _currentFrontSideways;

    public enum DriveMode { FWD, RWD, AWD }

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.mass = 1500f;
        _rb.linearDamping = 0.05f;
        _rb.angularDamping = 0.5f;
        if (centerOfMassOffset != Vector3.zero)
            _rb.centerOfMass = centerOfMassOffset;

        _currentRearSideways  = sidewaysStiffness;
        _currentFrontSideways = sidewaysStiffness;

        if (steeringWheel != null)
        {
            initialSteeringWheelRot = steeringWheel.localRotation;
            MeshRenderer mr = steeringWheel.GetComponent<MeshRenderer>();
            if (mr != null)
            {
                steeringPivot = new GameObject("SteeringWheelPivot").transform;
                steeringPivot.SetParent(steeringWheel.parent);
                steeringPivot.position = mr.bounds.center;
                steeringPivot.localRotation = initialSteeringWheelRot;
                steeringWheel.SetParent(steeringPivot, true);
            }
            else
            {
                steeringPivot = steeringWheel;
            }
        }

        ConfigureWheel(wheelFL);
        ConfigureWheel(wheelFR);
        ConfigureWheel(wheelRL);
        ConfigureWheel(wheelRR);

        SetFriction(wheelFL, forwardStiffness, sidewaysStiffness);
        SetFriction(wheelFR, forwardStiffness, sidewaysStiffness);
        SetFriction(wheelRL, forwardStiffness, sidewaysStiffness);
        SetFriction(wheelRR, forwardStiffness, sidewaysStiffness);
    }

    void ConfigureWheel(WheelCollider wc)
    {
        wc.suspensionDistance = suspensionDistance;
        var spring = wc.suspensionSpring;
        spring.spring         = springForce;
        spring.damper         = damperForce;
        spring.targetPosition = targetPosition;
        wc.suspensionSpring   = spring;
        wc.forceAppPointDistance = 0.05f;
    }

    void SetFriction(WheelCollider wc, float fwd, float side)
    {
        var fwdFriction = wc.forwardFriction;
        fwdFriction.stiffness = fwd;
        wc.forwardFriction = fwdFriction;

        var sideFriction = wc.sidewaysFriction;
        sideFriction.stiffness = side;
        wc.sidewaysFriction = sideFriction;
    }

    public void SetPlayerControlled(bool on)
    {
        _playerControlled = on;
        if (!on) { _motorInput = 0f; _steerInput = 0f; _handBrake = false; }
    }

    void Update()
    {
        if (!_playerControlled) return;
        _motorInput = Input.GetAxis("Vertical");
        _steerInput = Input.GetAxis("Horizontal");
        _handBrake  = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.Space);
    }

    void ApplyParkBrake()
    {
        wheelFL.motorTorque = 0f; wheelFR.motorTorque = 0f;
        wheelRL.motorTorque = 0f; wheelRR.motorTorque = 0f;
        wheelFL.brakeTorque = handBrakeTorque; wheelFR.brakeTorque = handBrakeTorque;
        wheelRL.brakeTorque = handBrakeTorque; wheelRR.brakeTorque = handBrakeTorque;
        wheelFL.steerAngle  = 0f; wheelFR.steerAngle  = 0f;
    }

    void FixedUpdate()
    {
        if (!_playerControlled) { ApplyParkBrake(); return; }

        float speed        = _rb.linearVelocity.magnitude * 3.6f; // km/h
        float forwardSpeed = Vector3.Dot(transform.forward, _rb.linearVelocity);

        // ── Speed-sensitive steering ─────────────────────────────────────────
        // Reduce max steer angle at high speed (more stable, GTA-like)
        float speedOverBase = Mathf.Clamp01((speed - steerReductionStartKmh) / Mathf.Max(1f, maxSpeed - steerReductionStartKmh));
        float effectiveSteer = Mathf.Lerp(maxSteerAngle, maxSteerAngle * 0.5f, speedOverBase);
        float steer = _steerInput * effectiveSteer;
        wheelFL.steerAngle = steer;
        wheelFR.steerAngle = steer;

        // ── Torque & Dynamic Braking ─────────────────────────────────────
        float torque = 0f;
        float brake  = 0f;

        if (_motorInput < 0)
        {
            if (forwardSpeed > 1f)
                brake  = Mathf.Abs(_motorInput) * brakeTorque;
            else
                torque = speed < maxSpeed ? _motorInput * maxMotorTorque : 0f;
        }
        else if (_motorInput > 0)
        {
            if (forwardSpeed < -1f)
                brake  = Mathf.Abs(_motorInput) * brakeTorque;
            else
                torque = speed < maxSpeed ? _motorInput * maxMotorTorque : 0f;
        }

        // ── Drive mode ───────────────────────────────────────────────────
        switch (driveMode)
        {
            case DriveMode.FWD:
                wheelFL.motorTorque = torque;
                wheelFR.motorTorque = torque;
                wheelRL.motorTorque = 0f;
                wheelRR.motorTorque = 0f;
                break;
            case DriveMode.RWD:
                wheelFL.motorTorque = 0f;
                wheelFR.motorTorque = 0f;
                wheelRL.motorTorque = torque;
                wheelRR.motorTorque = torque;
                break;
            case DriveMode.AWD:
                wheelFL.motorTorque = torque * 0.5f;
                wheelFR.motorTorque = torque * 0.5f;
                wheelRL.motorTorque = torque * 0.5f;
                wheelRR.motorTorque = torque * 0.5f;
                break;
        }

        // ── Braking ──────────────────────────────────────────────────────
        float hb = _handBrake ? handBrakeTorque : 0f;
        wheelFL.brakeTorque = brake;
        wheelFR.brakeTorque = brake;
        wheelRL.brakeTorque = brake + hb;
        wheelRR.brakeTorque = brake + hb;

        // ── GTA-style drift friction ─────────────────────────────────────
        UpdateDriftFriction(speed, brake);

        // ── Anti-roll bars ───────────────────────────────────────────────
        ApplyAntiRoll(wheelFL, wheelFR);
        ApplyAntiRoll(wheelRL, wheelRR);

        // ── Sync visual meshes ───────────────────────────────────────────
        SyncMesh(wheelFL, meshFL);
        SyncMesh(wheelFR, meshFR);
        SyncMesh(wheelRL, meshRL);
        SyncMesh(wheelRR, meshRR);

        if (steeringPivot != null)
            steeringPivot.localRotation = initialSteeringWheelRot * Quaternion.Euler(0, 0, -_steerInput * steeringWheelMaxAngle);
    }

    void UpdateDriftFriction(float speedKmh, float brakeAmount)
    {
        WheelHit hit;

        // ── Sample actual lateral slip from each axle ────────────────────
        float rearSlip = 0f; int rearN = 0;
        if (wheelRL.GetGroundHit(out hit)) { rearSlip += Mathf.Abs(hit.sidewaysSlip); rearN++; }
        if (wheelRR.GetGroundHit(out hit)) { rearSlip += Mathf.Abs(hit.sidewaysSlip); rearN++; }
        if (rearN > 0) rearSlip /= rearN;

        float frontSlip = 0f; int frontN = 0;
        if (wheelFL.GetGroundHit(out hit)) { frontSlip += Mathf.Abs(hit.sidewaysSlip); frontN++; }
        if (wheelFR.GetGroundHit(out hit)) { frontSlip += Mathf.Abs(hit.sidewaysSlip); frontN++; }
        if (frontN > 0) frontSlip /= frontN;

        // ── Build drift factors ──────────────────────────────────────────
        bool isBraking  = brakeAmount > 0f;
        bool isSteering = Mathf.Abs(_steerInput) > 0.1f;

        // Slip already happening beyond threshold
        float rearSlipFactor  = Mathf.Clamp01((rearSlip  - slipThreshold) / Mathf.Max(0.01f, slipThreshold));
        float frontSlipFactor = Mathf.Clamp01((frontSlip - slipThreshold) / Mathf.Max(0.01f, slipThreshold));

        // Brake + steer at speed -> rear starts to step out (oversteer)
        float brakeTurnFactor = (isBraking && isSteering)
            ? brakeSteerDriftFactor * Mathf.Clamp01(speedKmh / 30f)
            : 0f;

        // Handbrake: instantly kill rear grip
        float handBrakeFactor = _handBrake ? 1f : 0f;

        float rearDrift  = Mathf.Clamp01(rearSlipFactor  + brakeTurnFactor + handBrakeFactor);
        float frontDrift = Mathf.Clamp01(frontSlipFactor + brakeTurnFactor * 0.25f);

        // ── Target stiffness values ──────────────────────────────────────
        float targetRear  = Mathf.Lerp(sidewaysStiffness, driftSidewaysStiffness,         rearDrift);
        float targetFront = Mathf.Lerp(sidewaysStiffness, driftSidewaysStiffness * 1.4f,  frontDrift);

        // Lose grip fast, regain slowly (authentic slip feel)
        float loseRate     = driftRecoverySpeed * 2.5f;
        float recoverRate  = driftRecoverySpeed;

        _currentRearSideways = Mathf.Lerp(
            _currentRearSideways, targetRear,
            (targetRear < _currentRearSideways ? loseRate : recoverRate) * Time.fixedDeltaTime);

        _currentFrontSideways = Mathf.Lerp(
            _currentFrontSideways, targetFront,
            (targetFront < _currentFrontSideways ? loseRate : recoverRate) * Time.fixedDeltaTime);

        SetFriction(wheelRL, forwardStiffness, _currentRearSideways);
        SetFriction(wheelRR, forwardStiffness, _currentRearSideways);
        SetFriction(wheelFL, forwardStiffness, _currentFrontSideways);
        SetFriction(wheelFR, forwardStiffness, _currentFrontSideways);
    }

    void ApplyAntiRoll(WheelCollider left, WheelCollider right)
    {
        WheelHit hit;
        float travelL = 1f, travelR = 1f;

        bool groundedL = left.GetGroundHit(out hit);
        if (groundedL)
            travelL = (-left.transform.InverseTransformPoint(hit.point).y - left.radius)
                      / left.suspensionDistance;

        bool groundedR = right.GetGroundHit(out hit);
        if (groundedR)
            travelR = (-right.transform.InverseTransformPoint(hit.point).y - right.radius)
                      / right.suspensionDistance;

        float force = (travelL - travelR) * antiRollForce;
        if (groundedL) _rb.AddForceAtPosition(left.transform.up  * -force, left.transform.position);
        if (groundedR) _rb.AddForceAtPosition(right.transform.up *  force, right.transform.position);
    }

    void SyncMesh(WheelCollider wc, Transform mesh)
    {
        if (mesh == null) return;
        wc.GetWorldPose(out Vector3 pos, out Quaternion rot);
        mesh.position = pos;
        mesh.rotation = rot;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.TransformPoint(centerOfMassOffset), 0.08f);
    }
#endif
}
