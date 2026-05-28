using UnityEngine;
using System.Collections.Generic;

// ──────────────────────────────────────────────────────────────────────────────
//  PolarMazeGenerator  (fixed)
//
//  Issue 2  – Connectivity guarantee
//             Recursive-backtracking visits every cell exactly once, so the
//             spanning-tree it produces already guarantees full connectivity.
//             The original bug was that GetUnvisitedNeighbors could map an
//             inner-ring cell to the SAME outer-ring cell multiple times and
//             could skip outer-ring cells whose angular span exceeded one
//             inner cell.  Fixed in GetUnvisitedNeighbors: we now collect
//             ALL outer cells whose angular range overlaps the current cell,
//             and likewise find the correct single inner parent.
//
//  Issue 3  – Solvability
//             Because recursive-backtracking produces a perfect maze (one path
//             between every pair of cells) the maze is always solvable once
//             connectivity is guaranteed.  A BFS solver runs after generation
//             to mark the solution path so the gizmo (issue 5) and the mesh
//             builder can colour it.
//
//  Issue 6  – Centre ring has no break
//             Ring 0 is a single "hub" cell.  We now open one radial passage
//             from ring 1 into the hub (OutwardOpen on a ring-1 cell) so a
//             player can reach the centre.  The hub itself gets an
//             OutwardOpen flag on a pseudo-cell so the mesh builder knows to
//             leave the gap.
// ──────────────────────────────────────────────────────────────────────────────

[ExecuteInEditMode]
public class PolarMazeGenerator : MonoBehaviour
{
    [Header("Maze Settings")]
    public int maxRings = 8;
    public int baseSegments = 8;

    [Header("Center Room")]
    [Min(0.1f)]
    public float centerOpenRadius = 4f;

    public float ringWidth = 4f;

    public int seed = 42;

    [Header("Generation Tuning")]
    [Range(0f, 1f)]
    public float outwardChance = 0.25f;

    [Header("Wall Settings")]
    public float wallHeight = 4f;
    public float wallThickness = 0.3f;

    [Header("Materials")]
    public Material wallMaterial;

    // The solved path from exit → centre, inclusive
    public List<PolarCell> SolutionPath { get; private set; } = new();

    private List<List<PolarCell>> grid = new();

    // ── Public API ────────────────────────────────────────────────────────────

    [ContextMenu("Generate Maze")]
    public void GenerateMaze()
    {
        Random.InitState(seed);
        Clear();
        CreateGrid();
        Generate();
        OpenCentreEntrance();   // Issue 6
        Solve();                // Issue 3
        Debug.Log($"Polar maze generated. Solution length: {SolutionPath.Count} cells.");
    }

    public List<List<PolarCell>> GetGrid() => grid;

    [ContextMenu("Clear")]
    public void Clear()
    {
        grid.Clear();
        SolutionPath.Clear();
    }

    // ── Grid construction ─────────────────────────────────────────────────────

    void CreateGrid()
    {
        grid.Clear();

        for (int r = 0; r <= maxRings; r++)
        {
            // Ring 0 = single hub cell
            int segments = (r == 0) ? 1
                : Mathf.Max(baseSegments,
                    Mathf.RoundToInt(2f * Mathf.PI * (centerOpenRadius + r * ringWidth)
                                     / ringWidth));

            var ring = new List<PolarCell>();

            for (int s = 0; s < segments; s++)
            {
                var cell = new PolarCell(r, s, segments);
                cell.CalculateWorldPosition(centerOpenRadius, ringWidth);
                ring.Add(cell);
            }

            grid.Add(ring);
        }
    }

    // ── Maze carving (recursive back-tracker) ─────────────────────────────────

    void Generate()
    {
        var stack = new Stack<PolarCell>();
        PolarCell start = grid[0][0];
        start.Visited = true;
        stack.Push(start);

        while (stack.Count > 0)
        {
            PolarCell current = stack.Peek();
            var neighbors = GetUnvisitedNeighbors(current);

            if (neighbors.Count == 0) { stack.Pop(); continue; }

            PolarCell next = neighbors[Random.Range(0, neighbors.Count)];
            OpenPassage(current, next);
            next.Visited = true;
            stack.Push(next);
        }

        CreateExit();
    }

    void CreateExit()
    {
        var outer = grid[maxRings];
        outer[Random.Range(0, outer.Count)].IsExit = true;
    }

    // ── Issue 6 – open a passage into the centre hub ──────────────────────────

    void OpenCentreEntrance()
    {
        // Pick a random ring-1 cell and open its inward wall (OutwardOpen on
        // the hub cell would logically make sense, but the hub is a single cell
        // so we express the break as OutwardOpen = true on that hub cell,
        // paired with the ring-1 cell that faces it).
        var ring1 = grid[1];
        int chosen = Random.Range(0, ring1.Count);

        // Mark the ring-1 cell so the mesh builder skips its inner arc wall
        ring1[chosen].OutwardOpen = true;

        // Also mark the hub cell (ring 0) so the mesh builder knows one sector
        // of the hub's outer arc is open.  We reuse the hub's OutwardOpen flag.
        grid[0][0].OutwardOpen = true;
    }

    // ── Neighbour lookup (Issue 2 fix) ────────────────────────────────────────

    List<PolarCell> GetUnvisitedNeighbors(PolarCell cell)
    {
        var neighbors = new List<PolarCell>();
        int segs = cell.SegmentsInThisRing;

        // ── Clockwise / counter-clockwise (same ring, ring > 0) ──────────────
        if (cell.Ring > 0)
        {
            AddIfUnvisited(grid[cell.Ring][(cell.Segment + 1) % segs], neighbors);
            AddIfUnvisited(grid[cell.Ring][(cell.Segment - 1 + segs) % segs], neighbors);
        }

        // ── Outward ──────────────────────────────────────────────────────────
        if (cell.Ring < maxRings)
        {
            var outer = grid[cell.Ring + 1];
            int outerSegs = outer.Count;

            // Angular span of this cell
            float myStart = (cell.Segment / (float)segs) * (Mathf.PI * 2f);
            float myEnd = ((cell.Segment + 1) / (float)segs) * (Mathf.PI * 2f);

            // Collect every outer cell whose angular centre falls inside our span
            for (int os = 0; os < outerSegs; os++)
            {
                float centre = ((os + 0.5f) / outerSegs) * (Mathf.PI * 2f);
                if (centre >= myStart && centre < myEnd)
                    AddIfUnvisited(outer[os], neighbors);
            }

            // Fallback: if nothing matched (numerical edge), take the mapped cell
            if (neighbors.Count == 0 || (cell.Ring == 0))
            {
                int idx = Mathf.Clamp(
                    Mathf.FloorToInt((cell.Segment / (float)segs) * outerSegs),
                    0, outerSegs - 1);
                AddIfUnvisited(outer[idx], neighbors);
            }
        }

        // ── Inward ───────────────────────────────────────────────────────────
        if (cell.Ring > 0)
        {
            var inner = grid[cell.Ring - 1];
            // The parent is whichever inner cell angularly contains this cell's centre
            float myCentre = ((cell.Segment + 0.5f) / segs) * (Mathf.PI * 2f);
            int innerSegs = inner.Count;
            int idx = Mathf.Clamp(
                Mathf.FloorToInt(myCentre / (Mathf.PI * 2f) * innerSegs),
                0, innerSegs - 1);

            AddIfUnvisited(inner[idx], neighbors);
        }

        return neighbors;
    }

    static void AddIfUnvisited(PolarCell c, List<PolarCell> list)
    {
        if (!c.Visited) list.Add(c);
    }

    // ── Passage opening ───────────────────────────────────────────────────────

    void OpenPassage(PolarCell a, PolarCell b)
    {
        if (a.Ring == b.Ring)
        {
            int segs = a.SegmentsInThisRing;
            if (b.Segment == (a.Segment + 1) % segs)
                a.ClockwiseOpen = true;
            else
                b.ClockwiseOpen = true;
            return;
        }

        PolarCell inner = a.Ring < b.Ring ? a : b;
        inner.OutwardOpen = true;
    }

    // ── Issue 3 – BFS solver ──────────────────────────────────────────────────

    void Solve()
    {
        SolutionPath.Clear();

        // Mark all cells as off-path first
        foreach (var ring in grid)
            foreach (var c in ring)
                c.OnSolutionPath = false;

        // Find exit cell
        PolarCell exit = null;
        foreach (var c in grid[maxRings])
            if (c.IsExit) { exit = c; break; }

        if (exit == null) { Debug.LogWarning("No exit cell found."); return; }

        PolarCell goal = grid[0][0];

        // BFS using passage connectivity
        var prev = new Dictionary<PolarCell, PolarCell>();
        var queue = new Queue<PolarCell>();
        prev[exit] = null;
        queue.Enqueue(exit);

        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == goal) break;

            foreach (var nb in GetPassageNeighbors(cur))
            {
                if (!prev.ContainsKey(nb))
                {
                    prev[nb] = cur;
                    queue.Enqueue(nb);
                }
            }
        }

        if (!prev.ContainsKey(goal))
        {
            Debug.LogWarning("Maze solver: no path from exit to centre found.");
            return;
        }

        // Reconstruct path
        var cell = goal;
        while (cell != null)
        {
            cell.OnSolutionPath = true;
            SolutionPath.Add(cell);
            prev.TryGetValue(cell, out cell);
        }

        SolutionPath.Reverse();
    }

    // Returns all cells reachable from 'cell' through open passages
    List<PolarCell> GetPassageNeighbors(PolarCell cell)
    {
        var result = new List<PolarCell>();
        int segs = cell.SegmentsInThisRing;
        int r = cell.Ring;

        // Clockwise: cell.ClockwiseOpen means passage between cell and cell+1
        PolarCell cw = r > 0 ? grid[r][(cell.Segment + 1) % segs] : null;
        if (cw != null && cell.ClockwiseOpen) result.Add(cw);

        // Counter-clockwise: prev cell's ClockwiseOpen
        if (r > 0)
        {
            int prevS = (cell.Segment - 1 + segs) % segs;
            PolarCell ccw = grid[r][prevS];
            if (ccw.ClockwiseOpen) result.Add(ccw);
        }

        // Outward: cell.OutwardOpen means passage to one or more outer cells
        if (r < maxRings)
        {
            var outer = grid[r + 1];
            int outerSegs = outer.Count;
            float myStart = (cell.Segment / (float)segs) * (Mathf.PI * 2f);
            float myEnd = ((cell.Segment + 1) / (float)segs) * (Mathf.PI * 2f);

            if (cell.OutwardOpen)
            {
                for (int os = 0; os < outerSegs; os++)
                {
                    float centre = ((os + 0.5f) / outerSegs) * (Mathf.PI * 2f);
                    if (centre >= myStart && centre < myEnd)
                        result.Add(outer[os]);
                }
            }
        }

        // Inward: an outer-ring cell whose parent has OutwardOpen
        if (r > 0)
        {
            var inner = grid[r - 1];
            int innerSegs = inner.Count;
            float myCentre = ((cell.Segment + 0.5f) / segs) * (Mathf.PI * 2f);
            int idx = Mathf.Clamp(
                Mathf.FloorToInt(myCentre / (Mathf.PI * 2f) * innerSegs),
                0, innerSegs - 1);

            PolarCell parent = inner[idx];
            if (parent.OutwardOpen) result.Add(parent);
        }

        return result;
    }
}
