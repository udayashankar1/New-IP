using UnityEngine;

[RequireComponent(typeof(PlayerController))]
public class EnemyInteractionPrompt : MonoBehaviour
{
    [Header("Detection")]
    public float detectionRange = 5f;
    [Tooltip("Max degrees from camera centre — enemy must be inside this cone")]
    public float viewAngle = 60f;

    Camera      _camera;
    EnemyPatrol _currentTarget;
    EnemyPatrol _prevTarget;

    void Awake() => _camera = Camera.main;

    void Update()
    {
        if (_camera == null) _camera = Camera.main;

        _currentTarget = FindBestTarget();

        if (_prevTarget != _currentTarget)
        {
            if (_prevTarget != null) _prevTarget.ShowQPrompt(false);
            _prevTarget = _currentTarget;
        }

        if (_currentTarget != null)
        {
            _currentTarget.ShowQPrompt(true);

            if (Input.GetKeyDown(KeyCode.Q))
            {
                float dist  = Vector3.Distance(transform.position, _currentTarget.transform.position);
                float angle = CameraAngleTo(_currentTarget);
                Debug.Log($"[Q Interaction] Target: {_currentTarget.name}  |  Distance: {dist:F2} m  |  Camera angle: {angle:F1}°");
            }
        }
    }

    EnemyPatrol FindBestTarget()
    {
        var cols = Physics.OverlapSphere(transform.position, detectionRange);
        EnemyPatrol best      = null;
        float        bestAngle = viewAngle;

        foreach (var col in cols)
        {
            var e = col.GetComponent<EnemyPatrol>()
                 ?? col.GetComponentInParent<EnemyPatrol>();

            if (e == null || !e.CanBeTakenDown || e.IsFPromptActive) continue;

            float a = CameraAngleTo(e);
            if (a < bestAngle) { bestAngle = a; best = e; }
        }
        return best;
    }

    float CameraAngleTo(EnemyPatrol e)
    {
        Vector3 toEnemy = (e.transform.position + Vector3.up * 1.0f) - _camera.transform.position;
        return Vector3.Angle(_camera.transform.forward, toEnemy);
    }

    void OnDisable()
    {
        if (_prevTarget != null) { _prevTarget.ShowQPrompt(false); _prevTarget = null; }
    }
}
