using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshRenderer))]
[RequireComponent(typeof(MeshCollider))]
public class RoadSpline : MonoBehaviour
{
    [System.Serializable]
    public class BezierPoint
    {
        public Vector3 position;
        public Vector3 tangent = Vector3.forward * 5f; // only used when autoSmooth is OFF

        public BezierPoint(Vector3 pos, Vector3 tan) { position = pos; tangent = tan; }
    }

    [Header("Spline Points")]
    public List<BezierPoint> points = new List<BezierPoint>();

    [Header("Road Shape")]
    public float roadWidth = 8f;
    [Range(4, 30)] public int samplesPerSegment = 12;
    public float uvTileLength = 10f;
    public bool closedLoop = false;

    [Header("Auto-Smooth (Catmull-Rom)")]
    [Tooltip("Automatically compute smooth tangents. Just move points — curve stays smooth. Disable for sharp corners.")]
    public bool autoSmooth = true;
    [Range(0.5f, 2.5f), Tooltip("How loose the automatic curve is. 1 = natural Catmull-Rom.")]
    public float smoothTension = 1f;

    [Header("Height")]
    [Tooltip("Lifts road mesh above terrain so it never clips through.")]
    public float roadHeightOffset = 0.06f;

    [Header("Auto Update")]
    public bool autoUpdate = true;

    // ── Public API ─────────────────────────────────────────────────────────

    public int SegmentCount => Mathf.Max(0, closedLoop ? points.Count : points.Count - 1);

    public Vector3 GetPoint(float t)
    {
        if (SegmentCount == 0) return transform.position;
        int seg = Mathf.Min(Mathf.FloorToInt(t), SegmentCount - 1);
        float lt = t - seg;
        int next = (seg + 1) % points.Count;
        var p0 = points[seg];
        var p1 = points[next];
        Vector3 t0 = autoSmooth ? AutoTangentAt(seg)  : p0.tangent;
        Vector3 t1 = autoSmooth ? AutoTangentAt(next) : p1.tangent;
        return CubicBezier(p0.position, p0.position + t0, p1.position - t1, p1.position, lt);
    }

    public Vector3 GetTangentDir(float t)
    {
        if (SegmentCount == 0) return transform.forward;
        int seg = Mathf.Min(Mathf.FloorToInt(t), SegmentCount - 1);
        float lt = t - seg;
        int next = (seg + 1) % points.Count;
        var p0 = points[seg];
        var p1 = points[next];
        Vector3 t0 = autoSmooth ? AutoTangentAt(seg)  : p0.tangent;
        Vector3 t1 = autoSmooth ? AutoTangentAt(next) : p1.tangent;
        return CubicBezierDeriv(p0.position, p0.position + t0, p1.position - t1, p1.position, lt).normalized;
    }

    // Computes Catmull-Rom tangent at index i without touching stored data
    Vector3 AutoTangentAt(int i)
    {
        int n = points.Count;
        if (n < 2) return Vector3.forward * 5f;
        Vector3 prev, next;
        if (closedLoop)
        {
            prev = points[(i - 1 + n) % n].position;
            next = points[(i + 1) % n].position;
        }
        else
        {
            prev = i > 0     ? points[i - 1].position : points[0].position - (points[1].position - points[0].position);
            next = i < n - 1 ? points[i + 1].position : points[n - 1].position + (points[n - 1].position - points[n - 2].position);
        }
        return (next - prev) * (smoothTension / 6f);
    }

    // ── Mesh generation ────────────────────────────────────────────────────

    public void GenerateMesh()
    {
        if (SegmentCount == 0) return;

        int total = SegmentCount * samplesPerSegment + 1;
        var verts = new Vector3[total * 2];
        var tris  = new int[(total - 1) * 6];
        var uvs   = new Vector2[total * 2];

        float dist = 0f;
        Vector3 prev = GetPoint(0f);

        for (int i = 0; i < total; i++)
        {
            float t   = (float)i / (total - 1) * SegmentCount;
            Vector3 p = GetPoint(t) + Vector3.up * roadHeightOffset;
            Vector3 f = GetTangentDir(t);

            if (i > 0) dist += Vector3.Distance(p, prev);
            prev = p;

            Vector3 right = Vector3.Cross(Vector3.up, f).normalized;
            if (right == Vector3.zero) right = Vector3.right;

            int vi = i * 2;
            verts[vi]     = transform.InverseTransformPoint(p - right * roadWidth * 0.5f);
            verts[vi + 1] = transform.InverseTransformPoint(p + right * roadWidth * 0.5f);

            float v = dist / uvTileLength;
            uvs[vi]     = new Vector2(0f, v);
            uvs[vi + 1] = new Vector2(1f, v);

            if (i < total - 1)
            {
                int ti = i * 6;
                tris[ti]     = vi;     tris[ti + 1] = vi + 2; tris[ti + 2] = vi + 1;
                tris[ti + 3] = vi + 1; tris[ti + 4] = vi + 2; tris[ti + 5] = vi + 3;
            }
        }

        var mesh       = new Mesh { name = "RoadMesh" };
        mesh.vertices  = verts;
        mesh.triangles = tris;
        mesh.uv        = uvs;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        var mf = GetComponent<MeshFilter>();
        var mc = GetComponent<MeshCollider>();
        mf.sharedMesh = mesh;
        mc.sharedMesh = mesh;
    }

    // ── Math helpers ───────────────────────────────────────────────────────

    static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t, tt = t * t, uu = u * u;
        return uu * u * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + tt * t * p3;
    }

    static Vector3 CubicBezierDeriv(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return 3f * u * u * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * t * t * (p3 - p2);
    }

    // ── Default setup ──────────────────────────────────────────────────────

    void Reset()
    {
        points.Clear();
        Vector3 fwd = Vector3.forward * 5f;
        points.Add(new BezierPoint(transform.position, fwd));
        points.Add(new BezierPoint(transform.position + Vector3.forward * 10f, fwd));
        GenerateMesh();
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        if (SegmentCount == 0) return;
        UnityEditor.Handles.color = new Color(1f, 0.8f, 0f, 0.35f);
        int steps = SegmentCount * 20;
        Vector3 prev2 = GetPoint(0f);
        for (int i = 1; i <= steps; i++)
        {
            Vector3 next = GetPoint((float)i / steps * SegmentCount);
            UnityEditor.Handles.DrawLine(prev2, next);
            prev2 = next;
        }
    }
#endif
}
