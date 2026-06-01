using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public class PatrolPath : MonoBehaviour
{
    [Header("Gizmos")]
    public Color waypointColor  = new Color(0.0f, 0.9f, 0.8f, 1f);
    public Color lineColor      = new Color(1.0f, 0.85f, 0.0f, 1f);
    public float waypointRadius = 0.45f;

    public int Count => transform.childCount;

    public Transform GetWaypoint(int index)
    {
        if (transform.childCount == 0) return transform;
        return transform.GetChild(index % transform.childCount);
    }

    void OnDrawGizmos()
    {
        int n = transform.childCount;
        if (n == 0) return;

        for (int i = 0; i < n; i++)
        {
            Transform wp = transform.GetChild(i);

            // Waypoint sphere
            Gizmos.color = waypointColor;
            Gizmos.DrawSphere(wp.position, waypointRadius);
            Gizmos.color = new Color(waypointColor.r, waypointColor.g, waypointColor.b, 0.35f);
            Gizmos.DrawWireSphere(wp.position, waypointRadius + 0.15f);

            // Connecting line to next — skip last waypoint (ping-pong, no loop back)
            if (i < n - 1)
            {
                Transform next = transform.GetChild(i + 1);
                Gizmos.color = lineColor;
                Gizmos.DrawLine(wp.position, next.position);
                DrawArrowHead(wp.position, next.position);
            }

#if UNITY_EDITOR
            // Index label
            GUIStyle style = new GUIStyle();
            style.normal.textColor = Color.white;
            style.fontStyle = FontStyle.Bold;
            style.fontSize = 13;
            Handles.Label(wp.position + Vector3.up * (waypointRadius + 0.35f), $"  {i}", style);
#endif
        }
    }

    void DrawArrowHead(Vector3 from, Vector3 to)
    {
        Vector3 dir = (to - from).normalized;
        if (dir == Vector3.zero) return;
        Vector3 mid   = Vector3.Lerp(from, to, 0.55f);
        Vector3 right = Vector3.Cross(dir, Vector3.up).normalized;
        float   size  = 0.4f;
        Gizmos.DrawLine(mid, mid - dir * size + right * size * 0.45f);
        Gizmos.DrawLine(mid, mid - dir * size - right * size * 0.45f);
    }
}
