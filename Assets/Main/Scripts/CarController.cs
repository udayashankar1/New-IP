using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
//  Arcade car controller — GTA-style handling.
//
//  Instead of Unity's stiff WheelCollider physics this uses a raycast suspension
//  + custom tire-force model:
//    • 4 raycasts provide spring/damper suspension (squat, dive, body roll).
//    • A per-tire lateral grip force gives controllable cornering & drifting.
//    • Drive/brake forces applied at the contact patch create real weight transfer.
//  The result is fast, smooth, weighty-but-responsive arcade handling.
//
//  The WheelCollider references are reused ONLY as wheel anchor positions and are
//  disabled at runtime, so existing scene wiring keeps working unchanged.
// ─────────────────────────────────────────────────────────────────────────────
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("Wheel Anchors (reused as positions, disabled at runtime)")]
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

    [Header("Drive Mode")]
    public DriveMode driveMode = DriveMode.RWD;
    public enum DriveMode { FWD, RWD, AWD }

    [Header("Body")]
    public float mass = 1400f;
    [Tooltip("Local center of mass. Keep it LOW for a planted, GTA-like feel.")]
    public Vector3 centerOfMass = new Vector3(0f, -0.4f, 0f);

    [Header("Suspension (per wheel)")]
    [Tooltip("Natural ride height — distance from the wheel anchor down to the wheel centre at rest.")]
    public float restLength = 0.18f;
    [Tooltip("How far the suspension can compress/extend from rest. Smaller = firmer, less bob.")]
    public float springTravel = 0.12f;
    public float wheelRadius = 0.35f;
    [Tooltip("Spring force. Higher = stiffer, less body roll/dive.")]
    public float springStiffness = 50000f;
    [Tooltip("Damping. Higher = settles faster, less bounce. Near-critical kills the bobbing.")]
    public float damperStiffness = 8000f;

    [Header("Engine / Speed")]
    [Tooltip("Top forward speed in m/s. (×3.6 = km/h, so 33 ≈ 120 km/h)")]
    public float maxSpeed = 33f;
    public float maxReverseSpeed = 12f;
    [Tooltip("Acceleration force. Higher = punchier pickup.")]
    public float enginePower = 16000f;
    [Tooltip("How accel tapers as you approach top speed (x = speed %, y = power %).")]
    public AnimationCurve powerCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f);
    public float brakePower = 20000f;
    [Tooltip("Natural slow-down when off the throttle (engine braking / rolling drag).")]
    [Range(0f, 1f)] public float rollingResistance = 0.06f;

    [Header("Steering")]
    public float maxSteerAngle = 32f;
    [Tooltip("How fast the wheels turn toward target angle (deg/sec). Lower = smoother turn-in.")]
    public float steerSpeed = 150f;
    [Tooltip("How fast wheels return to centre (deg/sec).")]
    public float steerReturnSpeed = 280f;
    [Tooltip("Steering authority kept at top speed (0.5 = half lock at max speed for stability).")]
    [Range(0.2f, 1f)] public float highSpeedSteer = 0.5f;

    [Header("Grip / Traction")]
    [Tooltip("Front tyre lateral grip. Lower = smoother, more natural turn-in (1 = robotic snap).")]
    [Range(0f, 1f)] public float frontGrip = 0.85f;
    [Tooltip("Rear tyre lateral grip when driving normally. Keep ≥ front for stable handling.")]
    [Range(0f, 1f)] public float rearGrip = 0.9f;
    [Tooltip("Rear grip while actively drifting (brake+steer). Lower = slides more.")]
    [Range(0f, 1f)] public float driftRearGrip = 0.4f;
    [Tooltip("Rear grip while the handbrake is held. Very low = instant slide.")]
    [Range(0f, 1f)] public float handbrakeGrip = 0.12f;
    [Tooltip("How quickly grip returns after a slide (lose grip is faster).")]
    public float gripRecoverySpeed = 5f;

    [Header("High-Speed Stability")]
    [Tooltip("Downforce per m/s of speed — keeps the car planted at speed.")]
    public float downforce = 25f;

    [Header("Vehicle Entry")]
    public Transform driverSeat;

    // ── internals ───────────────────────────────────────────────────────────
    Rigidbody _rb;
    Wheel[]   _wheels;
    float _motorInput, _steerInput;
    bool  _handBrake;
    bool  _playerControlled;
    float _currentSteerAngle;
    float _currentRearGrip;
    readonly RaycastHit[] _hitBuf = new RaycastHit[8];

    class Wheel
    {
        public Transform anchor;   // stable position (the WheelCollider's transform)
        public Transform mesh;     // visual wheel
        public bool steer;         // front wheels turn
        public bool power;         // driven wheels get engine torque
        public float spin;         // accumulated roll angle for the visual
        public bool grounded;
        public Vector3 meshPos;    // world position to render the wheel mesh at
    }

    // ── setup ────────────────────────────────────────────────────────────────
    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.mass = mass;
        _rb.linearDamping = 0.1f;
        _rb.angularDamping = 2.5f;
        _rb.centerOfMass = centerOfMass;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        bool fwd = driveMode == DriveMode.FWD || driveMode == DriveMode.AWD;
        bool rwd = driveMode == DriveMode.RWD || driveMode == DriveMode.AWD;

        _wheels = new[]
        {
            MakeWheel(wheelFL, meshFL, steer:true,  power:fwd),
            MakeWheel(wheelFR, meshFR, steer:true,  power:fwd),
            MakeWheel(wheelRL, meshRL, steer:false, power:rwd),
            MakeWheel(wheelRR, meshRR, steer:false, power:rwd),
        };

        _currentRearGrip = rearGrip;
        SetupSteeringWheel();
    }

    Wheel MakeWheel(WheelCollider wc, Transform mesh, bool steer, bool power)
    {
        if (wc == null) return null;
        wc.enabled = false;                 // stop WheelCollider physics — we own the forces now
        if (wheelRadius <= 0f) wheelRadius = wc.radius;
        return new Wheel { anchor = wc.transform, mesh = mesh, steer = steer, power = power };
    }

    void SetupSteeringWheel()
    {
        if (steeringWheel == null) return;
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
        else steeringPivot = steeringWheel;
    }

    public void SetPlayerControlled(bool on)
    {
        _playerControlled = on;
        if (!on) { _motorInput = 0f; _steerInput = 0f; _handBrake = false; _currentSteerAngle = 0f; }
    }

    // ── input ────────────────────────────────────────────────────────────────
    void Update()
    {
        if (!_playerControlled) return;
        _motorInput = Input.GetAxis("Vertical");
        _steerInput = Input.GetAxis("Horizontal");
        _handBrake  = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.Space);
    }

    // ── physics ────────────────────────────────────────────────────────────────
    void FixedUpdate()
    {
        if (_wheels == null) return;

        float forwardSpeed = Vector3.Dot(transform.forward, _rb.linearVelocity);
        float speed        = _rb.linearVelocity.magnitude;
        float dt           = Time.fixedDeltaTime;

        UpdateSteering(forwardSpeed, dt);
        UpdateRearGrip(speed, dt);

        // Steered wheel directions (front wheels rotate by the smoothed steer angle).
        Quaternion steerRot = Quaternion.AngleAxis(_currentSteerAngle, transform.up);
        Vector3 steerFwd   = steerRot * transform.forward;
        Vector3 steerRight = steerRot * transform.right;

        int poweredCount = 0;
        foreach (var w in _wheels) if (w != null && w.power) poweredCount++;
        poweredCount = Mathf.Max(1, poweredCount);

        float tireMass = _rb.mass / 4f;

        foreach (var w in _wheels)
        {
            if (w == null) continue;

            Vector3 up   = transform.up;
            Vector3 fwd  = w.steer ? steerFwd   : transform.forward;
            Vector3 side = w.steer ? steerRight : transform.right;
            Vector3 pos  = w.anchor.position;

            // ── Suspension (raycast spring/damper) ───────────────────────────
            float maxDist = restLength + springTravel + wheelRadius;
            // Cast down and pick the nearest hit that ISN'T part of this car, so the
            // body's own colliders never count as ground.
            int n = Physics.RaycastNonAlloc(pos, -up, _hitBuf, maxDist, ~0, QueryTriggerInteraction.Ignore);
            RaycastHit hit = default;
            float best = float.MaxValue;
            w.grounded = false;
            for (int i = 0; i < n; i++)
            {
                if (_hitBuf[i].collider.transform.IsChildOf(transform)) continue;
                if (_hitBuf[i].distance < best) { best = _hitBuf[i].distance; hit = _hitBuf[i]; w.grounded = true; }
            }

            if (w.grounded)
            {
                Vector3 tireVel = _rb.GetPointVelocity(pos);

                float currentLen = hit.distance - wheelRadius;     // current spring length
                float offset     = restLength - currentLen;        // + when compressed
                float springVel  = Vector3.Dot(up, tireVel);
                float suspension = offset * springStiffness - springVel * damperStiffness;
                if (suspension > 0f) _rb.AddForceAtPosition(up * suspension, pos);

                // ── Lateral grip (kills sideways slide -> cornering & drift) ──
                float grip = w.steer ? frontGrip : _currentRearGrip;
                float lateralVel = Vector3.Dot(side, tireVel);
                float desiredAccel = -lateralVel * grip / dt;
                _rb.AddForceAtPosition(side * (tireMass * desiredAccel), pos);

                // ── Drive force (engine) ─────────────────────────────────────
                if (w.power && Mathf.Abs(_motorInput) > 0.01f)
                {
                    bool accelerating = Mathf.Sign(_motorInput) == Mathf.Sign(forwardSpeed) || Mathf.Abs(forwardSpeed) < 0.5f;
                    if (accelerating)
                    {
                        float top = _motorInput > 0f ? maxSpeed : maxReverseSpeed;
                        float t   = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / Mathf.Max(1f, top));
                        float power = powerCurve.Evaluate(t) * enginePower * _motorInput;
                        _rb.AddForceAtPosition(fwd * (power / poweredCount), pos);
                    }
                }

                // ── Braking & rolling resistance (along travel) ──────────────
                float longVel = Vector3.Dot(fwd, tireVel);
                bool braking = Mathf.Abs(_motorInput) > 0.01f &&
                               Mathf.Sign(_motorInput) != Mathf.Sign(forwardSpeed) &&
                               Mathf.Abs(forwardSpeed) > 0.5f;
                float brakeF = 0f;
                if (braking) brakeF = brakePower * Mathf.Abs(_motorInput);
                else if (Mathf.Abs(_motorInput) < 0.01f) brakeF = brakePower * rollingResistance;
                if (_handBrake && !w.steer) brakeF = Mathf.Max(brakeF, brakePower * 0.6f);

                float brakeAccel = Mathf.Clamp(-longVel / dt, -brakeF / tireMass, brakeF / tireMass);
                _rb.AddForceAtPosition(fwd * (tireMass * brakeAccel) * (brakeF > 0f ? 1f : 0f), pos);

            }

            // Visual placement — cast straight DOWN in world from the anchor so the
            // wheel sits on the ground directly under its mount. Vertical-only, so it
            // never snaps sideways when the body leans and never digs into terrain.
            PlaceWheelMesh(w, pos);

            UpdateWheelMesh(w, forwardSpeed, dt);
        }

        // ── High-speed downforce ─────────────────────────────────────────────
        _rb.AddForce(-transform.up * downforce * speed);

        // ── Steering wheel visual ────────────────────────────────────────────
        if (steeringPivot != null)
        {
            float n = maxSteerAngle > 0.01f ? _currentSteerAngle / maxSteerAngle : 0f;
            steeringPivot.localRotation = initialSteeringWheelRot * Quaternion.Euler(0, 0, -n * steeringWheelMaxAngle);
        }
    }

    void UpdateSteering(float forwardSpeed, float dt)
    {
        // Sharp at low speed, gentler at high speed; gradual (not snapping).
        float speedT = Mathf.Clamp01(Mathf.Abs(forwardSpeed) / Mathf.Max(1f, maxSpeed));
        float effective = maxSteerAngle * Mathf.Lerp(1f, highSpeedSteer, speedT);
        float target = _steerInput * effective;
        float rate = (Mathf.Abs(target) < Mathf.Abs(_currentSteerAngle)) ? steerReturnSpeed : steerSpeed;
        _currentSteerAngle = Mathf.MoveTowards(_currentSteerAngle, target, rate * dt);
    }

    void UpdateRearGrip(float speed, float dt)
    {
        bool steering = Mathf.Abs(_steerInput) > 0.1f;
        bool braking  = _motorInput < -0.1f;

        float target = rearGrip;
        if (_handBrake)                              target = handbrakeGrip;
        else if (braking && steering && speed > 4f)  target = driftRearGrip;

        // Lose grip quickly, recover gradually (authentic drift feel).
        float rate = (target < _currentRearGrip ? gripRecoverySpeed * 2.5f : gripRecoverySpeed) * dt;
        _currentRearGrip = Mathf.Lerp(_currentRearGrip, target, rate);
    }

    void PlaceWheelMesh(Wheel w, Vector3 anchorPos)
    {
        float maxDrop = restLength + springTravel;
        int n = Physics.RaycastNonAlloc(anchorPos, Vector3.down, _hitBuf,
                                        maxDrop + wheelRadius + 0.5f, ~0, QueryTriggerInteraction.Ignore);
        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < n; i++)
        {
            if (_hitBuf[i].collider.transform.IsChildOf(transform)) continue;
            if (_hitBuf[i].distance < bestDist) { bestDist = _hitBuf[i].distance; found = true; }
        }
        float len = found ? Mathf.Clamp(bestDist - wheelRadius, 0.02f, maxDrop) : maxDrop;
        w.meshPos = anchorPos + Vector3.down * len;
    }

    void UpdateWheelMesh(Wheel w, float forwardSpeed, float dt)
    {
        if (w.mesh == null) return;

        w.mesh.position = w.meshPos;

        // Roll the wheel for travel; steer the fronts. Built the same way Unity's
        // WheelCollider.GetWorldPose does it (car rotation → steer about up → roll
        // about the axle), so the authored wheel mesh stays correctly oriented.
        w.spin = Mathf.Repeat(w.spin + (forwardSpeed / Mathf.Max(0.05f, wheelRadius)) * Mathf.Rad2Deg * dt, 360f);
        float steer = w.steer ? _currentSteerAngle : 0f;
        w.mesh.rotation = transform.rotation
                        * Quaternion.AngleAxis(steer, Vector3.up)
                        * Quaternion.AngleAxis(w.spin, Vector3.right);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(transform.TransformPoint(centerOfMass), 0.08f);

        Gizmos.color = Color.cyan;
        foreach (var wc in new[] { wheelFL, wheelFR, wheelRL, wheelRR })
        {
            if (wc == null) continue;
            Vector3 p = wc.transform.position;
            Gizmos.DrawLine(p, p - transform.up * (restLength + springTravel + wheelRadius));
        }
    }
#endif
}
