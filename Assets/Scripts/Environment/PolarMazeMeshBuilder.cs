using System.Collections.Generic;
using UnityEngine;

// ──────────────────────────────────────────────────────────────────────────────
//  PolarMazeMeshBuilder  (v3 – conditional shortening)
//
//  ROOT CAUSE OF REMAINING GAPS
//  ─────────────────────────────
//  A radial wall spans from innerR to outerR at some angle A.  At each end it
//  must shorten by t (half wall-thickness) IF an arc wall is present there, so
//  the cap lands flush on the arc face.  If NO arc wall is present (an open
//  passage), the radial wall must NOT shorten — it reaches the full ring-
//  boundary radius.  The ring-(r-1) or ring-(r+1) radial wall on the other side
//  of that passage also reaches the same radius, so the two ends touch with
//  zero gap.
//
//  Previous versions always shortened by t regardless of whether an arc was
//  present, leaving a gap of exactly 2t (= wallThickness) at every open-passage
//  ring boundary.  This produced the "L corners that don't close" and "walls
//  that seem to almost align but don't" artefacts visible in the screenshots.
//
//  FIX: for each end of a radial wall, check whether any arc wall exists at
//  that ring boundary and at that angular position.  If yes → shorten by t.
//  If no arc (open passage) → no shortening; endpoint stays at the raw radius.
//
//  CHECKING WHICH ARC EXISTS AT A RADIAL WALL TIP
//  ────────────────────────────────────────────────
//  A radial wall lives at the CLOCKWISE edge of cell (r, s), i.e. between cell s
//  and cell s+1 in the same ring.  At its outer tip (radius outerR):
//    • Cell (r, s) may have an arc wall spanning [startAngle_s .. endAngle]
//    • Cell (r, s+1) may have an arc wall spanning [endAngle .. startAngle_s+1]
//    An arc wall is present on the CCW side iff  !cell_s.OutwardOpen
//    An arc wall is present on the CW  side iff  !cell_sNext.OutwardOpen
//    → outerArcPresent = !cell_s.OutwardOpen || !cell_sNext.OutwardOpen
//  At its inner tip (radius innerR = outerR of ring r-1):
//    Find the ring-(r-1) cells on each side of endAngle and apply the same test.
//    (If r==1, innerR is the hub wall whose arcs are tracked by ring-1 cells.)
//
//  SOLUTION PATH GIZMO
//  ─────────────────────
//  Draws the BFS solution through the CENTRE of each corridor cell.
//  WorldPosition in PolarCell is the radial+angular midpoint of the cell's
//  passable area (set in PolarCell.CalculateWorldPosition).
// ──────────────────────────────────────────────────────────────────────────────

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PolarMazeMeshBuilder : MonoBehaviour
{
    [Header("Source")]
    public PolarMazeGenerator generator;

    [Header("Arc Quality")]
    [Tooltip("Minimum quad strips per arc wall cell.")]
    public int minArcQuads = 8;

    [Header("Solution Path Gizmo")]
    public Color solutionPathColor = Color.yellow;
    [Tooltip("Height above floor at which the solution line is drawn.")]
    public float solutionGizmoHeight = 1f;

    Mesh          mesh;
    List<Vector3> verts;
    List<int>     tris;

    // ── Entry points ──────────────────────────────────────────────────────────

    [ContextMenu("Generate Maze Mesh")]
    public void GenerateMazeMesh()
    {
        if (generator == null) { Debug.LogError("No generator assigned."); return; }
        generator.GenerateMaze();
        BuildMesh();
    }

    [ContextMenu("Clear Mesh")]
    public void ClearMesh()
    {
        var mf = GetComponent<MeshFilter>();
        if (mf.sharedMesh != null) DestroyImmediate(mf.sharedMesh);
        mf.sharedMesh = null;
    }

    // ── Gizmo: solution path ──────────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (generator == null) return;
        var path = generator.SolutionPath;
        if (path == null || path.Count < 2) return;

        Gizmos.color = solutionPathColor;
        float y = solutionGizmoHeight;

        for (int i = 0; i < path.Count - 1; i++)
        {
            Vector3 a = path[i].WorldPosition     + Vector3.up * y;
            Vector3 b = path[i + 1].WorldPosition + Vector3.up * y;
            Gizmos.DrawLine(a, b);
            Gizmos.DrawSphere(a, 0.18f);
        }
        Gizmos.DrawSphere(path[path.Count - 1].WorldPosition + Vector3.up * y, 0.18f);
    }

    // ── Mesh construction ─────────────────────────────────────────────────────

    void BuildMesh()
    {
        mesh = new Mesh { name = "PolarMazeMesh" };
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        verts = new List<Vector3>();
        tris  = new List<int>();

        BuildFromGrid();

        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals(0f);
        mesh.RecalculateBounds();

        GetComponent<MeshFilter>().sharedMesh = mesh;
        if (generator.wallMaterial != null)
            GetComponent<MeshRenderer>().sharedMaterial = generator.wallMaterial;
    }

    // ── Main grid traversal ───────────────────────────────────────────────────

    void BuildFromGrid()
    {
        var   grid = generator.GetGrid();
        float t    = generator.wallThickness * 0.5f;

        for (int r = 0; r < grid.Count; r++)
        {
            var ring = grid[r];
            for (int s = 0; s < ring.Count; s++)
            {
                var cell = ring[s];
                if (cell.Ring == 0) continue;

                float angleStep  = Mathf.PI * 2f / cell.SegmentsInThisRing;
                float startAngle = cell.Segment       * angleStep;
                float endAngle   = (cell.Segment + 1) * angleStep;

                float innerR = generator.centerOpenRadius + (cell.Ring - 1) * generator.ringWidth;
                float outerR = generator.centerOpenRadius +  cell.Ring      * generator.ringWidth;

                // ── RADIAL wall (at the clockwise edge of cell s) ──────────
                if (!cell.ClockwiseOpen)
                {
                    Vector3 innerPt = PolarToWorld(innerR, endAngle);
                    Vector3 outerPt = PolarToWorld(outerR, endAngle);
                    Vector3 dir     = (outerPt - innerPt).normalized;

                    int sNext = (cell.Segment + 1) % cell.SegmentsInThisRing;
                    PolarCell cellNext = grid[r][sNext];

                    // ── Outer offset ──────────────────────────────────────
                    // Shorten toward centre by t if an arc wall exists at outerR
                    // adjacent to angle endAngle.  An arc exists on the CCW side
                    // (cell s) or CW side (cell s+1).
                    bool outerArc = !cell.OutwardOpen || !cellNext.OutwardOpen;
                    float outerOff = outerArc ? t : 0f;

                    // ── Inner offset ──────────────────────────────────────
                    // Shorten away from centre by t if an arc wall exists at
                    // innerR adjacent to angle endAngle.
                    //
                    // Special case r==1: innerR is the hub boundary.  The hub
                    // arcs are built from ring-1 cells' OutwardOpen flags (not
                    // from grid[0][0].OutwardOpen, which is always true and
                    // encodes nothing about which angular sector is open).
                    // So at r==1 we reuse cell and cellNext (ring-1 cells).
                    //
                    // General case r>1: innerR == outerR of ring r-1.  Find the
                    // ring-(r-1) cells that straddle endAngle and check their
                    // OutwardOpen flags.
                    float innerOff = 0f;
                    if (r == 1)
                    {
                        // Hub arc present at endAngle iff either adjacent ring-1
                        // cell has its outward (hub) wall intact.
                        bool innerArc = !cell.OutwardOpen || !cellNext.OutwardOpen;
                        innerOff = innerArc ? t : 0f;
                    }
                    else if (r > 1)
                    {
                        var   innerRing = grid[r - 1];
                        int   innerSegs = innerRing.Count;
                        float TAU       = Mathf.PI * 2f;

                        // Cell on the CCW side of endAngle in the inner ring
                        float aCcw = ((endAngle - 1e-5f) % TAU + TAU) % TAU;
                        int   idxL = Mathf.Clamp((int)(aCcw / TAU * innerSegs), 0, innerSegs - 1);

                        // Cell on the CW side of endAngle in the inner ring
                        float aCw  = ((endAngle + 1e-5f) % TAU + TAU) % TAU;
                        int   idxR = Mathf.Clamp((int)(aCw  / TAU * innerSegs), 0, innerSegs - 1);

                        bool innerArc = !innerRing[idxL].OutwardOpen || !innerRing[idxR].OutwardOpen;
                        innerOff = innerArc ? t : 0f;
                    }

                    BuildRadialRibbon(
                        innerPt + dir * innerOff,
                        outerPt - dir * outerOff,
                        endAngle
                    );
                }

                // ── ARC wall (outer edge of this cell) ────────────────────
                if (!cell.OutwardOpen)
                {
                    bool isOuterRing = (cell.Ring == generator.maxRings);
                    if (!(isOuterRing && cell.IsExit))
                        BuildArcRibbon(outerR, startAngle, endAngle);
                }
            }
        }

        BuildInnerHub();
    }

    // ── Inner hub wall (centre room boundary) ─────────────────────────────────

    void BuildInnerHub()
    {
        var grid = generator.GetGrid();
        if (grid.Count < 2) return;

        int   segs      = grid[1].Count;
        float radius    = generator.centerOpenRadius;
        float angleStep = Mathf.PI * 2f / segs;

        for (int s = 0; s < segs; s++)
        {
            if (grid[1][s].OutwardOpen) continue;     // entrance gap
            BuildArcRibbon(radius, s * angleStep, (s + 1) * angleStep);
        }
    }

    // ═════════════════════════════════════════════════════════════════════════
    //  RIBBON PRIMITIVES
    // ═════════════════════════════════════════════════════════════════════════

    void BuildArcRibbon(float radius, float startAngle, float endAngle)
    {
        float span = Mathf.Abs(endAngle - startAngle);
        int   n    = Mathf.Max(minArcQuads, Mathf.CeilToInt(span / (Mathf.PI / 32f)));

        float h = generator.wallHeight;
        float t = generator.wallThickness * 0.5f;

        var outerBot = new Vector3[n + 1];
        var innerBot = new Vector3[n + 1];

        for (int i = 0; i <= n; i++)
        {
            float angle = Mathf.Lerp(startAngle, endAngle, i / (float)n);
            float cos   = Mathf.Cos(angle);
            float sin   = Mathf.Sin(angle);
            outerBot[i] = new Vector3(cos * (radius + t), 0f, sin * (radius + t));
            innerBot[i] = new Vector3(cos * (radius - t), 0f, sin * (radius - t));
        }

        Vector3 up = Vector3.up * h;
        for (int i = 0; i < n; i++)
        {
            Vector3 oA = outerBot[i], oB = outerBot[i + 1];
            Vector3 iA = innerBot[i], iB = innerBot[i + 1];
            AddQuad(oA,      oB,      oB + up, oA + up);
            AddQuad(iB,      iA,      iA + up, iB + up);
            AddQuad(iA,      iB,      oB,      oA);
            AddQuad(oA + up, oB + up, iB + up, iA + up);
        }
        // Start cap
        AddQuad(innerBot[0], outerBot[0], outerBot[0] + up, innerBot[0] + up);
        // End cap
        AddQuad(outerBot[n], innerBot[n], innerBot[n] + up, outerBot[n] + up);
    }

    void BuildRadialRibbon(Vector3 a, Vector3 b, float angle)
    {
        float h = generator.wallHeight;
        float t = generator.wallThickness * 0.5f;

        Vector3 tang  = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));
        Vector3 right = tang * t;
        Vector3 up    = Vector3.up * h;

        Vector3 a0 = a - right, a1 = a + right;
        Vector3 b0 = b - right, b1 = b + right;

        AddQuad(a0,      b0,      b0 + up, a0 + up);  // front
        AddQuad(b1,      a1,      a1 + up, b1 + up);  // back
        AddQuad(a1,      a0,      b0,      b1);        // bottom
        AddQuad(a0 + up, a1 + up, b1 + up, b0 + up);  // top
        AddQuad(a0,      a1,      a1 + up, a0 + up);  // inner cap
        AddQuad(b1,      b0,      b0 + up, b1 + up);  // outer cap
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void AddQuad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        int i = verts.Count;
        verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
        tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
        tris.Add(i); tris.Add(i + 2); tris.Add(i + 3);
    }

    Vector3 PolarToWorld(float radius, float angle) =>
        new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
}
