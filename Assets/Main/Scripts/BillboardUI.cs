using UnityEngine;

// Attach to the root of a World Space Canvas prompt.
// Keeps it perfectly parallel to the screen plane every frame — no tilt, always readable.
public class BillboardUI : MonoBehaviour
{
    Camera _cam;
    Canvas _canvas;

    void Awake()
    {
        _canvas = GetComponent<Canvas>();
        // Force World Space so rotation actually takes effect
        if (_canvas != null)
            _canvas.renderMode = RenderMode.WorldSpace;
    }

    void LateUpdate()
    {
        if (_cam == null) _cam = GameManager.Instance.MainCamera;
        if (_cam == null) return;

        // Mirror the camera's own rotation — UI stays perfectly screen-parallel
        transform.rotation = _cam.transform.rotation;
    }
}
