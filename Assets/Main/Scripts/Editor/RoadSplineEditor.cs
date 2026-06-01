using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(RoadSpline))]
public class RoadSplineEditor : Editor
{
    RoadSpline _road;
    int _selectedIndex = -1;

    float _blendMargin    = 5f;
    float _terrainSink    = 0.1f;   // push terrain below road to stop overlap
    int   _texLayerIndex  = 0;
    float _treeRadius     = 7f;

    static readonly Color ColCurve    = new Color(1.00f, 0.80f, 0.00f);
    static readonly Color ColPoint    = new Color(1.00f, 0.55f, 0.10f);
    static readonly Color ColSelected = Color.white;
    static readonly Color ColTangent  = new Color(0.25f, 0.75f, 1.00f);

    void OnEnable() { _road = (RoadSpline)target; }

    // ── Inspector ──────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Auto-smooth toggle up top — most important setting
        var autoSmoothProp = serializedObject.FindProperty("autoSmooth");
        EditorGUILayout.PropertyField(autoSmoothProp,
            new GUIContent("Auto-Smooth", "Catmull-Rom: just move points, curve stays smooth automatically."));
        if (autoSmoothProp.boolValue)
            EditorGUILayout.PropertyField(serializedObject.FindProperty("smoothTension"),
                new GUIContent("  Curve Tension", "1 = natural Catmull-Rom. Higher = looser curves."));

        EditorGUILayout.Space(4);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("roadWidth"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("samplesPerSegment"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("uvTileLength"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("closedLoop"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("roadHeightOffset"),
            new GUIContent("Road Height Offset", "Lifts road mesh above terrain. Prevents z-fighting/overlap."));
        EditorGUILayout.PropertyField(serializedObject.FindProperty("autoUpdate"));

        bool changed = serializedObject.ApplyModifiedProperties();

        // ── Spline Controls ───────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Spline Controls", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("＋ Add Point at End"))
            AddPointAtEnd();
        using (new EditorGUI.DisabledScope(_selectedIndex < 0))
            if (GUILayout.Button("✕ Remove Selected"))
                RemoveSelected();
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("⟳ Generate Mesh Now"))
        {
            Undo.RecordObject(_road.GetComponent<MeshFilter>(), "Generate Road Mesh");
            _road.GenerateMesh();
        }

        string hint = autoSmoothProp.boolValue
            ? "Move orange handles → curve auto-smooths.\nAlt + Click terrain → place point at cursor."
            : "Move orange handles to reposition.\nDrag cyan handles to adjust curve shape.\nAlt + Click terrain → place point at cursor.";
        EditorGUILayout.HelpBox(hint, MessageType.Info);

        // ── Terrain Tools ─────────────────────────────────────────────────
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Terrain Tools", EditorStyles.boldLabel);

        Terrain terrain = FindFirstObjectByType<Terrain>();
        if (terrain == null)
        {
            EditorGUILayout.HelpBox("No Terrain found in scene.", MessageType.Warning);
        }
        else
        {
            EditorGUILayout.LabelField("Terrain: " + terrain.name, EditorStyles.miniLabel);

            _blendMargin   = EditorGUILayout.Slider("Blend Margin", _blendMargin, 0.5f, 30f);
            _terrainSink   = EditorGUILayout.Slider("Terrain Sink Below Road", _terrainSink, 0f, 0.5f);
            _texLayerIndex = EditorGUILayout.IntField("Texture Layer Index", _texLayerIndex);
            _treeRadius    = EditorGUILayout.FloatField("Tree Clear Radius", _treeRadius);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Flatten Terrain"))  FlattenTerrain(terrain);
            if (GUILayout.Button("Paint Texture"))    PaintRoadTexture(terrain);
            EditorGUILayout.EndHorizontal();
            if (GUILayout.Button("Clear Trees Along Road")) ClearTrees(terrain);
        }

        if (changed && _road.autoUpdate)
            _road.GenerateMesh();
    }

    // ── Scene GUI ─────────────────────────────────────────────────────────

    void OnSceneGUI()
    {
        Event e = Event.current;
        DrawCurve();
        DrawHandles();

        // Alt + Left Click → add point at terrain hit
        if (e.alt && e.type == EventType.MouseDown && e.button == 0)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            if (Physics.Raycast(ray, out RaycastHit hit, 2000f))
            {
                Undo.RecordObject(_road, "Add Road Point");
                AddPointAt(hit.point);
                e.Use();
            }
        }
    }

    // ── Curve drawing ──────────────────────────────────────────────────────

    void DrawCurve()
    {
        if (_road.SegmentCount == 0) return;
        Handles.color = ColCurve;
        int steps = _road.SegmentCount * 24;
        Vector3 prev = _road.GetPoint(0f);
        for (int i = 1; i <= steps; i++)
        {
            Vector3 next = _road.GetPoint((float)i / steps * _road.SegmentCount);
            Handles.DrawLine(prev, next, 2.5f);
            prev = next;
        }
    }

    // ── Control handles ────────────────────────────────────────────────────

    void DrawHandles()
    {
        for (int i = 0; i < _road.points.Count; i++)
        {
            var pt = _road.points[i];
            bool selected = i == _selectedIndex;
            float sz = HandleUtility.GetHandleSize(pt.position) * 0.13f;

            Handles.color = selected ? ColSelected : ColPoint;
            if (Handles.Button(pt.position, Quaternion.identity, sz, sz * 1.4f, Handles.SphereHandleCap))
                _selectedIndex = i;

            if (!selected) continue;

            // Move handle
            EditorGUI.BeginChangeCheck();
            Vector3 newPos = Handles.PositionHandle(pt.position, Quaternion.identity);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_road, "Move Road Point");
                pt.position = newPos;
                if (_road.autoUpdate) _road.GenerateMesh();
            }

            // Only show tangent handles when auto-smooth is OFF
            if (!_road.autoSmooth)
            {
                DrawTangentHandle(pt, true);
                DrawTangentHandle(pt, false);
            }
        }
    }

    void DrawTangentHandle(RoadSpline.BezierPoint pt, bool outHandle)
    {
        Vector3 hp = outHandle ? pt.position + pt.tangent : pt.position - pt.tangent;
        float sz = HandleUtility.GetHandleSize(hp) * 0.09f;

        Handles.color = ColTangent;
        Handles.DrawLine(pt.position, hp, 1.5f);

        EditorGUI.BeginChangeCheck();
        Vector3 moved = Handles.FreeMoveHandle(hp, sz, Vector3.zero, Handles.CircleHandleCap);
        if (EditorGUI.EndChangeCheck())
        {
            Undo.RecordObject(_road, "Adjust Tangent");
            pt.tangent = outHandle ? moved - pt.position : pt.position - moved;
            if (_road.autoUpdate) _road.GenerateMesh();
        }
    }

    // ── Point management ──────────────────────────────────────────────────

    void AddPointAtEnd()
    {
        Undo.RecordObject(_road, "Add Road Point");
        var pts = _road.points;
        if (pts.Count == 0)
        {
            pts.Add(new RoadSpline.BezierPoint(Vector3.zero, Vector3.forward * 5f));
        }
        else
        {
            var last = pts[pts.Count - 1];
            Vector3 dir = last.tangent.sqrMagnitude > 0.001f ? last.tangent.normalized : Vector3.forward;
            pts.Add(new RoadSpline.BezierPoint(last.position + dir * 10f, last.tangent));
        }
        _selectedIndex = _road.points.Count - 1;
        if (_road.autoUpdate) _road.GenerateMesh();
    }

    void AddPointAt(Vector3 worldPos)
    {
        var pts = _road.points;
        Vector3 tan = pts.Count > 0 ? pts[pts.Count - 1].tangent : Vector3.forward * 5f;
        pts.Add(new RoadSpline.BezierPoint(worldPos, tan));
        _selectedIndex = pts.Count - 1;
        if (_road.autoUpdate) _road.GenerateMesh();
    }

    void RemoveSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _road.points.Count) return;
        Undo.RecordObject(_road, "Remove Road Point");
        _road.points.RemoveAt(_selectedIndex);
        _selectedIndex = Mathf.Clamp(_selectedIndex - 1, -1, _road.points.Count - 1);
        if (_road.autoUpdate) _road.GenerateMesh();
    }

    // ── Terrain: Flatten ──────────────────────────────────────────────────

    void FlattenTerrain(Terrain terrain)
    {
        if (_road.SegmentCount == 0) { Debug.LogWarning("[RoadSpline] No spline segments."); return; }

        TerrainData td   = terrain.terrainData;
        int res          = td.heightmapResolution;
        float halfW      = _road.roadWidth * 0.5f;
        float totalBlend = halfW + _blendMargin;

        List<Vector3> baked = BakeSpline(25);
        Bounds roadBounds   = RoadBoundsXZ(baked, totalBlend + 1f);

        // Convert world AABB to heightmap pixel range
        int xMin, xMax, zMin, zMax;
        WorldToHeightmapRange(terrain, td, res, roadBounds, out xMin, out xMax, out zMin, out zMax);
        int w = xMax - xMin + 1, h = zMax - zMin + 1;

        float[,] heights = td.GetHeights(xMin, zMin, w, h);
        bool cancelled   = false;

        for (int zi = 0; zi < h; zi++)
        {
            if (zi % 32 == 0)
            {
                if (EditorUtility.DisplayCancelableProgressBar("Flattening terrain",
                    $"Row {zi}/{h}", (float)zi / h))
                { cancelled = true; break; }
            }

            for (int xi = 0; xi < w; xi++)
            {
                Vector3 wp = new Vector3(
                    terrain.transform.position.x + (float)(xi + xMin) / (res - 1) * td.size.x,
                    0f,
                    terrain.transform.position.z + (float)(zi + zMin) / (res - 1) * td.size.z);

                Vector3 closest; float dist;
                FindClosestBaked(baked, wp, out closest, out dist);
                if (dist >= totalBlend) continue;

                // Sink terrain slightly below road to prevent z-fighting
                float targetH = Mathf.Clamp01(
                    (closest.y - terrain.transform.position.y - _terrainSink) / td.size.y);
                float blend   = dist <= halfW ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (dist - halfW) / _blendMargin);
                heights[zi, xi] = Mathf.Lerp(heights[zi, xi], targetH, blend);
            }
        }

        EditorUtility.ClearProgressBar();

        if (!cancelled)
        {
            // Register undo AFTER computation, just before writing — safe pattern
            Undo.RegisterCompleteObjectUndo(td, "Flatten Terrain Along Road");
            td.SetHeights(xMin, zMin, heights);
            EditorUtility.SetDirty(td);
            Debug.Log("[RoadSpline] Terrain flattened.");
        }
        else
        {
            Debug.Log("[RoadSpline] Flatten cancelled — no changes written.");
        }
    }

    // ── Terrain: Paint texture ────────────────────────────────────────────

    void PaintRoadTexture(Terrain terrain)
    {
        if (_road.SegmentCount == 0) return;

        TerrainData td  = terrain.terrainData;
        int layers      = td.alphamapLayers;
        if (_texLayerIndex >= layers)
        {
            Debug.LogWarning($"[RoadSpline] Layer {_texLayerIndex} out of range ({layers} exist).");
            return;
        }

        float halfW      = _road.roadWidth * 0.5f;
        float totalBlend = halfW + _blendMargin;
        int aw = td.alphamapWidth, ah = td.alphamapHeight;

        List<Vector3> baked = BakeSpline(25);
        Bounds roadBounds   = RoadBoundsXZ(baked, totalBlend + 1f);

        int xMin, xMax, zMin, zMax;
        WorldToAlphamapRange(terrain, td, aw, ah, roadBounds, out xMin, out xMax, out zMin, out zMax);
        int w = xMax - xMin + 1, h = zMax - zMin + 1;

        float[,,] alphas = td.GetAlphamaps(xMin, zMin, w, h);
        bool cancelled   = false;

        for (int zi = 0; zi < h; zi++)
        {
            if (zi % 32 == 0)
                if (EditorUtility.DisplayCancelableProgressBar("Painting texture",
                    $"Row {zi}/{h}", (float)zi / h))
                { cancelled = true; break; }

            for (int xi = 0; xi < w; xi++)
            {
                Vector3 wp = new Vector3(
                    terrain.transform.position.x + (float)(xi + xMin) / (aw - 1) * td.size.x,
                    0f,
                    terrain.transform.position.z + (float)(zi + zMin) / (ah - 1) * td.size.z);

                Vector3 closest; float dist;
                FindClosestBaked(baked, wp, out closest, out dist);
                if (dist >= totalBlend) continue;

                float blend = dist <= halfW ? 1f : 1f - Mathf.SmoothStep(0f, 1f, (dist - halfW) / _blendMargin);
                float old   = alphas[zi, xi, _texLayerIndex];
                float neo   = Mathf.Lerp(old, 1f, blend);
                float delta = neo - old;
                alphas[zi, xi, _texLayerIndex] = neo;

                float otherSum = 0f;
                for (int l = 0; l < layers; l++) if (l != _texLayerIndex) otherSum += alphas[zi, xi, l];
                if (otherSum > 0f)
                    for (int l = 0; l < layers; l++)
                        if (l != _texLayerIndex) alphas[zi, xi, l] -= delta * (alphas[zi, xi, l] / otherSum);
            }
        }

        EditorUtility.ClearProgressBar();

        if (!cancelled)
        {
            Undo.RegisterCompleteObjectUndo(td, "Paint Road Texture");
            td.SetAlphamaps(xMin, zMin, alphas);
            EditorUtility.SetDirty(td);
            Debug.Log("[RoadSpline] Texture painted.");
        }
        else
        {
            Debug.Log("[RoadSpline] Paint cancelled — no changes written.");
        }
    }

    // ── Terrain: Clear trees ──────────────────────────────────────────────

    void ClearTrees(Terrain terrain)
    {
        if (_road.SegmentCount == 0) return;

        TerrainData td    = terrain.terrainData;
        List<Vector3> baked = BakeSpline(25);
        float rSq         = _treeRadius * _treeRadius;
        var trees         = new List<TreeInstance>(td.treeInstances);

        trees.RemoveAll(tree =>
        {
            Vector3 wp = new Vector3(
                terrain.transform.position.x + tree.position.x * td.size.x,
                terrain.transform.position.y + tree.position.y * td.size.y,
                terrain.transform.position.z + tree.position.z * td.size.z);
            Vector3 closest; float dist;
            FindClosestBaked(baked, wp, out closest, out dist);
            return dist * dist < rSq;
        });

        Undo.RegisterCompleteObjectUndo(td, "Clear Trees Along Road");
        td.treeInstances = trees.ToArray();
        EditorUtility.SetDirty(td);
        Debug.Log($"[RoadSpline] Trees cleared. {td.treeInstances.Length} remain.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    List<Vector3> BakeSpline(int stepsPerSeg)
    {
        var list  = new List<Vector3>();
        int total = _road.SegmentCount * stepsPerSeg;
        for (int i = 0; i <= total; i++)
            list.Add(_road.GetPoint((float)i / total * _road.SegmentCount));
        return list;
    }

    static void FindClosestBaked(List<Vector3> baked, Vector3 wp, out Vector3 closest, out float dist)
    {
        closest = baked[0];
        float bestSq = float.MaxValue;
        foreach (var pt in baked)
        {
            float dx = pt.x - wp.x, dz = pt.z - wp.z;
            float sq = dx * dx + dz * dz;
            if (sq < bestSq) { bestSq = sq; closest = pt; }
        }
        dist = Mathf.Sqrt(bestSq);
    }

    static Bounds RoadBoundsXZ(List<Vector3> baked, float expand)
    {
        Vector3 min = baked[0], max = baked[0];
        foreach (var p in baked) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
        min -= new Vector3(expand, 0, expand);
        max += new Vector3(expand, 0, expand);
        return new Bounds((min + max) * 0.5f, max - min);
    }

    static void WorldToHeightmapRange(Terrain t, TerrainData td, int res, Bounds b,
        out int xMin, out int xMax, out int zMin, out int zMax)
    {
        Vector3 tPos = t.transform.position;
        xMin = Mathf.Clamp(Mathf.FloorToInt((b.min.x - tPos.x) / td.size.x * (res - 1)), 0, res - 1);
        xMax = Mathf.Clamp(Mathf.CeilToInt ((b.max.x - tPos.x) / td.size.x * (res - 1)), 0, res - 1);
        zMin = Mathf.Clamp(Mathf.FloorToInt((b.min.z - tPos.z) / td.size.z * (res - 1)), 0, res - 1);
        zMax = Mathf.Clamp(Mathf.CeilToInt ((b.max.z - tPos.z) / td.size.z * (res - 1)), 0, res - 1);
    }

    static void WorldToAlphamapRange(Terrain t, TerrainData td, int aw, int ah, Bounds b,
        out int xMin, out int xMax, out int zMin, out int zMax)
    {
        Vector3 tPos = t.transform.position;
        xMin = Mathf.Clamp(Mathf.FloorToInt((b.min.x - tPos.x) / td.size.x * (aw - 1)), 0, aw - 1);
        xMax = Mathf.Clamp(Mathf.CeilToInt ((b.max.x - tPos.x) / td.size.x * (aw - 1)), 0, aw - 1);
        zMin = Mathf.Clamp(Mathf.FloorToInt((b.min.z - tPos.z) / td.size.z * (ah - 1)), 0, ah - 1);
        zMax = Mathf.Clamp(Mathf.CeilToInt ((b.max.z - tPos.z) / td.size.z * (ah - 1)), 0, ah - 1);
    }
}
