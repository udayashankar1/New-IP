using UnityEngine;
using UnityEngine.UI;

public class SuspicionMeterUI : MonoBehaviour
{
    [Header("Enemy Reference")]
    public EnemyPatrol enemy;

    [Header("UI References")]
    public Image fillImage;
    public Image bgImage;

    [Header("Colors")]
    public Color suspicionColor   = Color.yellow;
    public Color detectedColor    = Color.red;
    public Color bgSuspicionColor = new Color(1f, 1f, 0f, 0.28f);
    public Color bgDetectedColor  = new Color(1f, 0f, 0f, 0.28f);

    [Header("Detected Flash")]
    public float detectedHoldTime = 0.8f;

    Canvas _canvas;
    Camera _cam;
    bool   _wasDetected;
    float  _detectedTimer;

    void Awake()
    {
        if (enemy == null)
            enemy = GetComponentInParent<EnemyPatrol>();
        _canvas = GetComponent<Canvas>();
        if (_canvas != null) _canvas.enabled = false;
    }

    void Start() => _cam = Camera.main;

    void LateUpdate()
    {
        if (enemy == null) return;
        if (_cam == null) _cam = Camera.main;

        // Detected flash timer
        bool detected = enemy.IsDetected;
        if (detected && !_wasDetected) _detectedTimer = detectedHoldTime;
        _wasDetected = detected;
        if (_detectedTimer > 0f) _detectedTimer -= Time.deltaTime;

        bool suspicious    = enemy.IsSuspicious;
        bool investigating = enemy.IsInvestigating;
        bool showDetected  = _detectedTimer > 0f;

        bool show = suspicious || investigating || showDetected;
        if (_canvas != null) _canvas.enabled = show;
        if (!show) return;

        // Billboard — always face the camera
        Vector3 dir = transform.position - _cam.transform.position;
        if (dir.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(dir);

        // Fill amount: decay/fill during suspicion, full while investigating, full red when detected
        float fill = showDetected ? 1f : (investigating ? 1f : enemy.SuspicionProgress);
        Color fc   = showDetected ? detectedColor   : suspicionColor;
        Color bc   = showDetected ? bgDetectedColor : bgSuspicionColor;

        if (fillImage != null) { fillImage.color = fc; fillImage.fillAmount = fill; }
        if (bgImage   != null)   bgImage.color   = bc;
    }
}
