using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ThetaMaze
{
    // ═══════════════════════════════════════════════════════════════════════════
    //  ThetaMazeMono  —  clean 3D mesh, proper corners, correct normals
    //
    //  APPROACH
    //  ────────
    //  Wall geometry is built as continuous RIBBONS rather than isolated prisms.
    //
    //  ARC walls  (ring boundaries):
    //    One ribbon per contiguous run of wall-present cells on the same ring.
    //    A ribbon is a quad-strip sampled at fine angular steps → smooth curve,
    //    no seams between adjacent cells.  At each end of the ribbon the arc is
    //    trimmed inward by halfThickness so the spoke wall's box cap fits flush.
    //
    //  SPOKE walls  (radial boundaries):
    //    One rectangular box per wall-present spoke.
    //    The box's angular extent = WallThickness so it fills exactly the gap
    //    left by the trimmed arc ribbons → perfect L-corner, no overlap, no gap.
    //
    //  NORMALS
    //    Every face is built with its own four verts (no sharing across hard
    //    edges) so RecalculateNormals() produces sharp, correct face normals.
    //    The curved outer/inner faces of arc ribbons get smooth normals because
    //    adjacent ring-face verts ARE shared along the ribbon's length.
    // ═══════════════════════════════════════════════════════════════════════════

    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class ThetaMazeMono : MonoBehaviour
    {
        // ── Inspector fields ──────────────────────────────────────────────────

        [Header("Generation")]
        public ThetaMazeParams Params = new ThetaMazeParams();

        [Header("Wall Mesh")]
        public Material WallMaterial;
        public float WallHeight = 1.0f;
        public float WallThickness = 0.12f;
        public int ArcSegments = 64;   // segments per full circle for arcs

        [Header("Solve Path")]
        public bool ShowSolvePath = true;
        public Color SolvePathColor = new Color(0.1f, 1f, 0.4f, 1f);
        public float SolvePathDotSize = 0.05f;
        public float SolvePathYOffset = 0.05f;

        // ── Runtime ───────────────────────────────────────────────────────────

        ThetaMazeGenerator _gen;
        List<Vector3> _solvePath = new List<Vector3>();
        MeshFilter _mf;
        MeshRenderer _mr;

        // Mesh accumulators (reused each build)
        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<int> _tris = new List<int>();
        readonly List<Vector3> _norms = new List<Vector3>();

        // ── Unity lifecycle ───────────────────────────────────────────────────

        void Awake() { _mf = GetComponent<MeshFilter>(); _mr = GetComponent<MeshRenderer>(); }
        void Start() => GenerateMaze();

        void Update()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying) return;
#endif
            if (Input.GetKeyDown(KeyCode.G)) GenerateMaze();
            if (Input.GetKeyDown(KeyCode.P)) TogglePath();
        }

        // ── Public ────────────────────────────────────────────────────────────

        [ContextMenu("Generate Maze")]
        public void GenerateMaze()
        {
            if (_mf == null) _mf = GetComponent<MeshFilter>();
            if (_mr == null) _mr = GetComponent<MeshRenderer>();

            _gen = new ThetaMazeGenerator();
            _gen.Generate(Params);

            BuildMesh();
            SolveMaze();
        }

        [ContextMenu("Toggle Solve Path")]
        public void TogglePath()
        {
            ShowSolvePath = !ShowSolvePath;
#if UNITY_EDITOR
            SceneView.RepaintAll();
#endif
        }

        // ═════════════════════════════════════════════════════════════════════
        //  MESH BUILDING
        // ═════════════════════════════════════════════════════════════════════

        void BuildMesh()
        {
            _verts.Clear();
            _tris.Clear();
            _norms.Clear();

            float halfT = WallThickness * 0.5f;
            float halfH = WallHeight * 0.5f;

            // ── ARC walls ─────────────────────────────────────────────────────
            //
            //  Walk each ring and collect contiguous runs of wall-present arcs.
            //  A "run" is a maximal sequence of adjacent cells on the same ring
            //  where the shared arc wall (outer or inner) is present.
            //
            //  We do this separately for outer walls and inner walls.
            //  Outer walls of ring i  = ring boundary between ring i and ring i+1.
            //  Inner wall  of ring 0  = innermost circle.

            for (int i = 0; i < _gen.Cells.Length; i++)
            {
                int bins = _gen.BinsPerRing[i];
                float rIn = _gen.Cells[i][0].RadiusInner;
                float rOut = _gen.Cells[i][0].RadiusOuter;

                // Outer arc runs for ring i  (boundary at rOut)
                BuildArcRuns(i, bins, rOut, halfT, halfH, isOuterBoundary: true);

                // Inner arc of ring 0 only (boundary at rIn)
                if (i == 0)
                    BuildArcRuns(i, bins, rIn, halfT, halfH, isOuterBoundary: false);
            }

            // ── SPOKE walls ───────────────────────────────────────────────────
            //
            //  For each cell, if WallCW is present build one spoke box.
            //  The spoke is drawn at angle c.ThetaEnd, from rIn to rOut.
            //  Angular half-extent = halfT / radius (arc-length ≈ thickness).

            for (int i = 0; i < _gen.Cells.Length; i++)
            {
                int bins = _gen.BinsPerRing[i];
                float rIn = _gen.Cells[i][0].RadiusInner;
                float rOut = _gen.Cells[i][0].RadiusOuter;

                for (int j = 0; j < bins; j++)
                {
                    if (_gen.Cells[i][j].WallCW)
                        BuildSpokeWall(rIn, rOut, _gen.Cells[i][j].ThetaEnd, halfT, halfH);
                }
            }

            // ── Upload ────────────────────────────────────────────────────────

            var mesh = new Mesh { name = "ThetaMaze" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_norms);
            mesh.SetTriangles(_tris, 0);
            mesh.RecalculateBounds();

            _mf.sharedMesh = mesh;
            _mr.sharedMaterial = WallMaterial != null ? WallMaterial : DefaultMat();

            var col = GetComponent<MeshCollider>();
            if (col != null) col.sharedMesh = mesh;
        }

        // ── Arc run builder ───────────────────────────────────────────────────
        //
        //  Scans ring i for contiguous runs of wall-present arc segments, then
        //  calls BuildArcRibbon for each run.

        void BuildArcRuns(int ring, int bins, float radius, float halfT, float halfH,
                          bool isOuterBoundary)
        {
            // Collect which sectors have this arc wall present
            // For outer boundary: WallOuter of cell (ring, j)
            // For inner boundary: WallInner of cell (ring, j)  — only ring 0
            bool WallPresent(int j) =>
                isOuterBoundary ? _gen.Cells[ring][j].WallOuter
                                : _gen.Cells[ring][j].WallInner;

            // Find contiguous runs (circular array)
            // We break the ring at the first absent wall to avoid wrap complexity
            int startSearch = 0;
            for (int j = 0; j < bins; j++)
            {
                if (!WallPresent(j)) { startSearch = j + 1; break; }
            }

            int j0 = startSearch;
            int count = 0;

            for (int step = 0; step <= bins; step++)
            {
                int j = (j0 + step) % bins;
                bool present = WallPresent(j);

                if (present)
                {
                    count++;
                }
                else
                {
                    if (count > 0)
                    {
                        // Run from (j0 + step - count) to (j0 + step - 1)
                        int runStart = (j0 + step - count + bins * 2) % bins;
                        float tStart = _gen.Cells[ring][runStart].ThetaStart;
                        int runEnd = (j0 + step - 1 + bins * 2) % bins;
                        float tEnd = _gen.Cells[ring][runEnd].ThetaEnd;

                        // Handle wrap-around
                        if (tEnd <= tStart) tEnd += 2f * Mathf.PI;

                        BuildArcRibbon(radius, tStart, tEnd, halfT, halfH);
                        count = 0;
                    }
                }
            }
            // Flush final run
            if (count > 0)
            {
                int runStart = (j0 + bins + 1 - count) % bins;
                float tStart = _gen.Cells[ring][runStart].ThetaStart;
                int runEnd = (j0 + bins) % bins;
                float tEnd = _gen.Cells[ring][runEnd].ThetaEnd;
                if (tEnd <= tStart) tEnd += 2f * Mathf.PI;
                BuildArcRibbon(radius, tStart, tEnd, halfT, halfH);
            }
        }

        // ── Arc ribbon ────────────────────────────────────────────────────────
        //
        //  Builds a smooth curved wall ribbon at `radius` from tStart to tEnd.
        //  Radial thickness = WallThickness (halfT inward and outward).
        //  The arc endpoints are trimmed angularly by halfT/radius radians so
        //  spoke wall boxes cap the corners with no gap and no overlap.
        //
        //  Faces generated:
        //    Outer curved face  (furthest from centre)
        //    Inner curved face  (closest to centre)
        //    Top flat cap
        //    Bottom flat cap
        //    Two end caps (trimmed ends — the spoke fills these corners)

        void BuildArcRibbon(float radius, float tStart, float tEnd, float halfT, float halfH)
        {
            // Angular trim at each end so spoke boxes cap perfectly
            float trimAngle = halfT / radius;
            float ta = tStart + trimAngle;
            float tb = tEnd - trimAngle;

            if (tb <= ta) return; // arc too short (fully covered by spoke corners)

            float rI = radius - halfT;
            float rO = radius + halfT;

            int segs = Mathf.Max(1, Mathf.RoundToInt(ArcSegments * (tb - ta) / (2f * Mathf.PI)));

            // Sample ring of points along the arc
            // We need segs+1 column positions
            int cols = segs + 1;

            // Precompute the angle at each column
            var angles = new float[cols];
            for (int k = 0; k < cols; k++)
                angles[k] = Mathf.Lerp(ta, tb, (float)k / segs);

            // ── Outer curved face (normal points away from centre) ──────────
            // Vertices shared between outer face and top/bottom caps
            // Layout: for each column k, two verts: bottom(k) and top(k)
            // We build the outer face as a quad strip.
            {
                int baseIdx = _verts.Count;
                for (int k = 0; k < cols; k++)
                {
                    float cos = Mathf.Cos(angles[k]);
                    float sin = Mathf.Sin(angles[k]);
                    Vector3 norm = new Vector3(cos, 0f, sin); // outward radial

                    _verts.Add(new Vector3(rO * cos, -halfH, rO * sin)); // bottom
                    _norms.Add(norm);
                    _verts.Add(new Vector3(rO * cos, halfH, rO * sin)); // top
                    _norms.Add(norm);
                }
                // Quads: col k → col k+1
                for (int k = 0; k < segs; k++)
                {
                    int b0 = baseIdx + k * 2;     // bottom-left
                    int t0 = b0 + 1;              // top-left
                    int b1 = b0 + 2;              // bottom-right
                    int t1 = b0 + 3;              // top-right
                    AddQuad(b0, t0, t1, b1);      // outward-facing
                }
            }

            // ── Inner curved face (normal points toward centre) ─────────────
            {
                int baseIdx = _verts.Count;
                for (int k = 0; k < cols; k++)
                {
                    float cos = Mathf.Cos(angles[k]);
                    float sin = Mathf.Sin(angles[k]);
                    Vector3 norm = new Vector3(-cos, 0f, -sin); // inward radial

                    _verts.Add(new Vector3(rI * cos, -halfH, rI * sin));
                    _norms.Add(norm);
                    _verts.Add(new Vector3(rI * cos, halfH, rI * sin));
                    _norms.Add(norm);
                }
                for (int k = 0; k < segs; k++)
                {
                    int b0 = baseIdx + k * 2;
                    int t0 = b0 + 1;
                    int b1 = b0 + 2;
                    int t1 = b0 + 3;
                    AddQuad(b1, t1, t0, b0);      // inward-facing (reversed winding)
                }
            }

            // ── Top cap (normal = +Y) ───────────────────────────────────────
            {
                int baseIdx = _verts.Count;
                Vector3 up = Vector3.up;
                for (int k = 0; k < cols; k++)
                {
                    float cos = Mathf.Cos(angles[k]);
                    float sin = Mathf.Sin(angles[k]);
                    _verts.Add(new Vector3(rI * cos, halfH, rI * sin)); _norms.Add(up); // inner
                    _verts.Add(new Vector3(rO * cos, halfH, rO * sin)); _norms.Add(up); // outer
                }
                for (int k = 0; k < segs; k++)
                {
                    int iL = baseIdx + k * 2;
                    int oL = iL + 1;
                    int iR = iL + 2;
                    int oR = iL + 3;
                    AddQuad(iL, oL, oR, iR);
                }
            }

            // ── Bottom cap (normal = -Y) ────────────────────────────────────
            {
                int baseIdx = _verts.Count;
                Vector3 dn = Vector3.down;
                for (int k = 0; k < cols; k++)
                {
                    float cos = Mathf.Cos(angles[k]);
                    float sin = Mathf.Sin(angles[k]);
                    _verts.Add(new Vector3(rI * cos, -halfH, rI * sin)); _norms.Add(dn);
                    _verts.Add(new Vector3(rO * cos, -halfH, rO * sin)); _norms.Add(dn);
                }
                for (int k = 0; k < segs; k++)
                {
                    int iL = baseIdx + k * 2;
                    int oL = iL + 1;
                    int iR = iL + 2;
                    int oR = iL + 3;
                    AddQuad(iR, oR, oL, iL);  // reversed for down-face
                }
            }

            // ── End caps (the trimmed ends, spoke fills the corners) ────────
            BuildArcEndCap(rI, rO, halfH, angles[0], normal: -1f); // CCW end
            BuildArcEndCap(rI, rO, halfH, angles[cols - 1], normal: +1f); // CW  end
        }

        // Flat quad end cap at a single angle — outward tangent is the normal
        void BuildArcEndCap(float rI, float rO, float halfH, float theta, float normal)
        {
            // Tangent direction (perpendicular to radial, in XZ plane)
            // normal = +1 means CW end, -1 means CCW end
            float cos = Mathf.Cos(theta);
            float sin = Mathf.Sin(theta);
            // Tangent (90° rotation of radial) × normal gives outward face direction
            Vector3 n = new Vector3(-sin * normal, 0f, cos * normal);

            int b = _verts.Count;
            _verts.Add(new Vector3(rI * cos, -halfH, rI * sin)); _norms.Add(n);
            _verts.Add(new Vector3(rO * cos, -halfH, rO * sin)); _norms.Add(n);
            _verts.Add(new Vector3(rO * cos, halfH, rO * sin)); _norms.Add(n);
            _verts.Add(new Vector3(rI * cos, halfH, rI * sin)); _norms.Add(n);

            if (normal > 0) AddQuad(b, b + 1, b + 2, b + 3);
            else AddQuad(b + 3, b + 2, b + 1, b);
        }

        // ── Spoke (radial) wall ───────────────────────────────────────────────
        //
        //  Box at angle `theta`, spanning rIn → rOut radially.
        //  Angular half-width = halfT.
        //  This fills the corner gap left by the trimmed arc ribbons.
        //  6 faces, each with its own 4 verts and explicit normals.

        void BuildSpokeWall(float rIn, float rOut, float theta, float halfT, float halfH)
        {
            float cos = Mathf.Cos(theta);
            float sin = Mathf.Sin(theta);

            // Radial direction and perpendicular tangent
            Vector3 radial = new Vector3(cos, 0f, sin);   // outward from centre
            Vector3 tangent = new Vector3(-sin, 0f, cos);   // CCW tangent

            // Four base corners at y=0, then ±halfH
            // Layout in box-face terms:
            //   "front" = CW  face  (+tangent)
            //   "back"  = CCW face  (-tangent)
            //   "outer" = far radial face
            //   "inner" = near radial face (toward centre)

            Vector3 c0 = new Vector3(rIn * cos, 0, rIn * sin);  // inner centre
            Vector3 c1 = new Vector3(rOut * cos, 0, rOut * sin);  // outer centre

            // Corners: iCCW/iCW = inner-ring CCW/CW, oCCW/oCW = outer-ring CCW/CW
            Vector3 iCCW = c0 - tangent * halfT;
            Vector3 iCW = c0 + tangent * halfT;
            Vector3 oCCW = c1 - tangent * halfT;
            Vector3 oCW = c1 + tangent * halfT;

            // Helper: emit one flat rectangular face with a given normal
            void Face(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n)
            {
                int idx = _verts.Count;
                _verts.Add(new Vector3(a.x, -halfH, a.z)); _norms.Add(n);
                _verts.Add(new Vector3(b.x, -halfH, b.z)); _norms.Add(n);
                _verts.Add(new Vector3(c.x, halfH, c.z)); _norms.Add(n);
                _verts.Add(new Vector3(d.x, halfH, d.z)); _norms.Add(n);
                AddQuad(idx, idx + 1, idx + 2, idx + 3);
            }

            // CW face  (+tangent normal)
            Face(iCW, oCW, oCW, iCW, tangent);
            // but we actually need distinct bottom/top rows:
            // Let me redo with explicit bottom/top separation ──────────────────

            // Remove the botched Face calls — rebuild cleanly
            // (remove last 8 verts and 6 tris added)
            _verts.RemoveRange(_verts.Count - 4, 4);
            _norms.RemoveRange(_norms.Count - 4, 4);
            _tris.RemoveRange(_tris.Count - 6, 6);

            // ── 6 clean faces ─────────────────────────────────────────────────

            // CW tangent face  (faces +tangent)
            EmitFace(
                new Vector3(iCW.x, -halfH, iCW.z),
                new Vector3(oCW.x, -halfH, oCW.z),
                new Vector3(oCW.x, halfH, oCW.z),
                new Vector3(iCW.x, halfH, iCW.z),
                tangent);

            // CCW tangent face  (faces -tangent)
            EmitFace(
                new Vector3(oCCW.x, -halfH, oCCW.z),
                new Vector3(iCCW.x, -halfH, iCCW.z),
                new Vector3(iCCW.x, halfH, iCCW.z),
                new Vector3(oCCW.x, halfH, oCCW.z),
                -tangent);

            // Outer radial face  (faces +radial)
            EmitFace(
                new Vector3(oCW.x, -halfH, oCW.z),
                new Vector3(oCCW.x, -halfH, oCCW.z),
                new Vector3(oCCW.x, halfH, oCCW.z),
                new Vector3(oCW.x, halfH, oCW.z),
                radial);

            // Inner radial face  (faces -radial)
            EmitFace(
                new Vector3(iCCW.x, -halfH, iCCW.z),
                new Vector3(iCW.x, -halfH, iCW.z),
                new Vector3(iCW.x, halfH, iCW.z),
                new Vector3(iCCW.x, halfH, iCCW.z),
                -radial);

            // Top cap  (+Y)
            EmitFace(
                new Vector3(iCCW.x, halfH, iCCW.z),
                new Vector3(iCW.x, halfH, iCW.z),
                new Vector3(oCW.x, halfH, oCW.z),
                new Vector3(oCCW.x, halfH, oCCW.z),
                Vector3.up);

            // Bottom cap  (-Y)
            EmitFace(
                new Vector3(iCW.x, -halfH, iCW.z),
                new Vector3(iCCW.x, -halfH, iCCW.z),
                new Vector3(oCCW.x, -halfH, oCCW.z),
                new Vector3(oCW.x, -halfH, oCW.z),
                Vector3.down);
        }

        // Emit one rectangular face: verts in order BL, BR, TR, TL (bottom-left etc.)
        // Winding: clockwise from front = correct for Unity's left-hand coord system
        void EmitFace(Vector3 bl, Vector3 br, Vector3 tr, Vector3 tl, Vector3 normal)
        {
            int i = _verts.Count;
            _verts.Add(bl); _norms.Add(normal);
            _verts.Add(br); _norms.Add(normal);
            _verts.Add(tr); _norms.Add(normal);
            _verts.Add(tl); _norms.Add(normal);
            AddQuad(i, i + 1, i + 2, i + 3);
        }

        // Emit two triangles for a quad (verts must be in CCW order when viewed from outside)
        void AddQuad(int a, int b, int c, int d)
        {
            _tris.Add(a); _tris.Add(b); _tris.Add(c);
            _tris.Add(a); _tris.Add(c); _tris.Add(d);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  BFS SOLVER
        // ═════════════════════════════════════════════════════════════════════

        void SolveMaze()
        {
            _solvePath.Clear();
            if (_gen?.EntranceCell == null || _gen?.ExitCell == null) return;

            var prev = new Dictionary<PolarCell, PolarCell> { [_gen.EntranceCell] = null };
            var queue = new Queue<PolarCell>();
            queue.Enqueue(_gen.EntranceCell);

            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (cur == _gen.ExitCell) break;
                foreach (var nb in _gen.GetPassableNeighbours(cur))
                    if (!prev.ContainsKey(nb)) { prev[nb] = cur; queue.Enqueue(nb); }
            }

            if (!prev.ContainsKey(_gen.ExitCell)) return;

            var path = new List<PolarCell>();
            for (var c = _gen.ExitCell; c != null; c = prev[c]) path.Add(c);
            path.Reverse();

            float pathY = WallHeight * 0.5f + SolvePathYOffset;
            foreach (var c in path)
            {
                var xz = c.CenterXZ();
                _solvePath.Add(transform.TransformPoint(new Vector3(xz.x, pathY, xz.y)));
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  GIZMOS
        // ═════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
        void OnDrawGizmos()
        {
            if (!ShowSolvePath || _solvePath == null || _solvePath.Count < 2) return;

            // Draw path line
            Handles.color = SolvePathColor;
            for (int k = 0; k < _solvePath.Count - 1; k++)
                Handles.DrawLine(_solvePath[k], _solvePath[k + 1]);

            // Dots
            Gizmos.color = SolvePathColor;
            foreach (var pt in _solvePath)
                Gizmos.DrawSphere(pt, SolvePathDotSize * 0.5f);

            if (_gen == null) return;
            float pathY = WallHeight * 0.5f + SolvePathYOffset;

            if (_gen.EntranceCell != null)
            { Gizmos.color = Color.cyan; Gizmos.DrawSphere(CellWorld(_gen.EntranceCell, pathY), SolvePathDotSize); }
            if (_gen.ExitCell != null)
            { Gizmos.color = Color.red; Gizmos.DrawSphere(CellWorld(_gen.ExitCell, pathY), SolvePathDotSize); }
        }

        Vector3 CellWorld(PolarCell c, float y)
        {
            var xz = c.CenterXZ();
            return transform.TransformPoint(new Vector3(xz.x, y, xz.y));
        }
#endif

        // ── Helpers ───────────────────────────────────────────────────────────

        Material DefaultMat()
        {
            var m = new Material(Shader.Find("Standard"));
            m.color = Color.white;
            return m;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    //  CUSTOM INSPECTOR
    // ═══════════════════════════════════════════════════════════════════════════

#if UNITY_EDITOR
    [CustomEditor(typeof(ThetaMazeMono))]
    public class ThetaMazeMonoEditor : Editor
    {
        bool _showGen = true;
        bool _showMesh = true;
        bool _showSolve = true;

        public override void OnInspectorGUI()
        {
            var mono = (ThetaMazeMono)target;
            serializedObject.Update();

            // ── Generation ─────────────────────────────────────────────────
            _showGen = EditorGUILayout.BeginFoldoutHeaderGroup(_showGen, "⚙  Generation");
            if (_showGen)
            {
                EditorGUI.indentLevel++;
                var p = mono.Params;

                p.Seed = EditorGUILayout.IntField(
                    new GUIContent("Seed", "0 = random. Any other value = deterministic."), p.Seed);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Topology", EditorStyles.boldLabel);
                p.Rings = Mathf.Max(2, EditorGUILayout.IntField(
                    new GUIContent("Rings", "Concentric ring count. Min 2."), p.Rings));
                p.BaseAngularBins = Mathf.Max(4, EditorGUILayout.IntField(
                    new GUIContent("Base Angular Bins", "Sectors in innermost ring. Min 4."), p.BaseAngularBins));
                p.AdaptiveAngularResolution = EditorGUILayout.Toggle(
                    new GUIContent("Adaptive Resolution", "Double sector count per ring to keep cells near-square."),
                    p.AdaptiveAngularResolution);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Radii", EditorStyles.boldLabel);
                p.InnerRadius = Mathf.Max(0.01f, EditorGUILayout.FloatField("Inner Radius", p.InnerRadius));
                p.RingSpacing = Mathf.Max(0.01f, EditorGUILayout.FloatField("Ring Spacing", p.RingSpacing));

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Theta (Θ) Pre-structure", EditorStyles.boldLabel);
                p.ThetaLoopRing = Mathf.Clamp(EditorGUILayout.IntField(
                    new GUIContent("Loop Ring", "Fully opened ring (Θ crossbar). -1 = off."), p.ThetaLoopRing), -1, p.Rings - 1);
                if (p.ThetaLoopRing >= 0)
                {
                    p.ThetaInnerSpokes = Mathf.Max(1, EditorGUILayout.IntField("Inner Spokes", p.ThetaInnerSpokes));
                    p.ThetaOuterSpokes = Mathf.Max(1, EditorGUILayout.IntField("Outer Spokes", p.ThetaOuterSpokes));
                }

                EditorGUILayout.Space(4);
                p.PrecarvedSpokes = Mathf.Max(0, EditorGUILayout.IntField(
                    new GUIContent("Pre-carved Spokes", "Evenly-spaced radial corridors pre-opened before algorithm runs."), p.PrecarvedSpokes));

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Algorithm", EditorStyles.boldLabel);
                p.Algorithm = (MazeAlgorithm)EditorGUILayout.EnumPopup("Algorithm", p.Algorithm);
                if (p.Algorithm == MazeAlgorithm.DepthFirstSearch)
                    p.RadialBias = Mathf.Clamp01(EditorGUILayout.Slider(
                        new GUIContent("Radial Bias", "0 = angular corridors, 1 = radial spokes."), p.RadialBias, 0f, 1f));
                p.ExtraPassageRatio = Mathf.Clamp(EditorGUILayout.FloatField(
                    new GUIContent("Extra Passages", "Extra wall removals after gen. 0 = perfect maze."), p.ExtraPassageRatio), 0f, 1f);

                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Entrance / Exit", EditorStyles.boldLabel);
                p.EntranceAngleDeg = EditorGUILayout.Slider("Entrance Angle", p.EntranceAngleDeg, 0f, 360f);
                p.ExitAngleDeg = EditorGUILayout.Slider("Exit Angle", p.ExitAngleDeg, 0f, 360f);

                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // ── Wall Mesh ──────────────────────────────────────────────────
            _showMesh = EditorGUILayout.BeginFoldoutHeaderGroup(_showMesh, "🧱  Wall Mesh");
            if (_showMesh)
            {
                EditorGUI.indentLevel++;
                mono.WallMaterial = (Material)EditorGUILayout.ObjectField("Wall Material", mono.WallMaterial, typeof(Material), false);
                mono.WallHeight = Mathf.Max(0.01f, EditorGUILayout.FloatField("Wall Height", mono.WallHeight));
                mono.WallThickness = Mathf.Max(0.001f, EditorGUILayout.FloatField("Wall Thickness", mono.WallThickness));
                mono.ArcSegments = Mathf.Max(4, EditorGUILayout.IntField("Arc Segments (per circle)", mono.ArcSegments));
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // ── Solve Path ─────────────────────────────────────────────────
            _showSolve = EditorGUILayout.BeginFoldoutHeaderGroup(_showSolve, "🧭  Solve Path");
            if (_showSolve)
            {
                EditorGUI.indentLevel++;
                mono.ShowSolvePath = EditorGUILayout.Toggle("Show Path", mono.ShowSolvePath);
                mono.SolvePathColor = EditorGUILayout.ColorField("Path Color", mono.SolvePathColor);
                mono.SolvePathDotSize = Mathf.Max(0.001f, EditorGUILayout.FloatField("Dot Size", mono.SolvePathDotSize));
                mono.SolvePathYOffset = EditorGUILayout.FloatField("Y Offset", mono.SolvePathYOffset);
                EditorGUI.indentLevel--;
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
            if (GUI.changed && !Application.isPlaying)
                EditorUtility.SetDirty(mono);

            EditorGUILayout.Space(10);

            GUI.backgroundColor = new Color(0.35f, 0.85f, 0.45f);
            if (GUILayout.Button("⟳   Generate Maze", GUILayout.Height(40)))
            {
                mono.GenerateMaze();
                EditorUtility.SetDirty(mono);
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
            if (GUILayout.Button("👁   Toggle Solve Path", GUILayout.Height(28)))
            {
                mono.TogglePath();
                SceneView.RepaintAll();
            }

            GUI.backgroundColor = Color.white;
        }
    }
#endif
}
