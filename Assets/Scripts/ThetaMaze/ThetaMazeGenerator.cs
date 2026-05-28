using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThetaMaze
{
    // ─────────────────────────────────────────────────────────────────────────
    //  PolarCell
    // ─────────────────────────────────────────────────────────────────────────

    public class PolarCell
    {
        public int Ring;
        public int Sector;

        public bool WallInner;
        public bool WallOuter;
        public bool WallCCW;
        public bool WallCW;

        public bool Visited;

        public float ThetaStart;
        public float ThetaEnd;
        public float RadiusInner;
        public float RadiusOuter;

        public PolarCell(int ring, int sector,
                         float tStart, float tEnd,
                         float rIn, float rOut)
        {
            Ring = ring; Sector = sector;
            ThetaStart = tStart; ThetaEnd = tEnd;
            RadiusInner = rIn; RadiusOuter = rOut;
            WallInner = WallOuter = WallCCW = WallCW = true;
        }

        public Vector2 CenterXZ()
        {
            float r = (RadiusInner + RadiusOuter) * 0.5f;
            float t = (ThetaStart + ThetaEnd) * 0.5f;
            return new Vector2(r * Mathf.Cos(t), r * Mathf.Sin(t));
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  Parameters  —  no [Range] clamps, you own your perf
    // ─────────────────────────────────────────────────────────────────────────

    [Serializable]
    public class ThetaMazeParams
    {
        [Header("Seed")]
        [Tooltip("0 = random every run. Any other value = deterministic.")]
        public int Seed = 42;

        [Header("Topology")]
        [Tooltip("Number of concentric rings. Minimum 2.")]
        public int Rings = 6;

        [Tooltip("Sectors in the innermost ring. Minimum 4. " +
                 "Outer rings double automatically when cells get too wide.")]
        public int BaseAngularBins = 8;

        [Tooltip("Allow outer rings to double their sector count for near-square cells. " +
                 "Disable to keep the same count on every ring.")]
        public bool AdaptiveAngularResolution = true;

        [Header("Radii")]
        [Tooltip("Radius of the innermost ring wall.")]
        public float InnerRadius = 0.4f;

        [Tooltip("Radial distance between ring walls (controls cell depth).")]
        public float RingSpacing = 0.6f;

        [Header("Theta (Θ) Pre-structure")]
        [Tooltip("Ring index that gets its full angular loop pre-carved (the Θ crossbar). -1 = disabled.")]
        public int ThetaLoopRing = 2;

        [Tooltip("Radial passages connecting the loop ring inward.")]
        public int ThetaInnerSpokes = 2;

        [Tooltip("Radial passages connecting the loop ring outward.")]
        public int ThetaOuterSpokes = 2;

        [Header("Extra Pre-carved Spokes")]
        [Tooltip("Evenly-spaced radial corridors punched through all rings before the algorithm runs.")]
        public int PrecarvedSpokes = 0;

        [Header("Algorithm")]
        [Tooltip("DFS = long winding corridors. Kruskal = balanced. Prim = dense branching.")]
        public MazeAlgorithm Algorithm = MazeAlgorithm.DepthFirstSearch;

        [Tooltip("DFS only: 0 = prefer angular corridors, 1 = prefer radial spokes.")]
        public float RadialBias = 0.3f;

        [Tooltip("Fraction of remaining walls removed after generation to add loops / shortcuts. 0 = perfect maze.")]
        public float ExtraPassageRatio = 0.05f;

        [Header("Entrance / Exit")]
        [Tooltip("Angle in degrees of the entrance gap on the outermost ring.")]
        public float EntranceAngleDeg = 0f;

        [Tooltip("Angle in degrees of the exit gap on the innermost ring.")]
        public float ExitAngleDeg = 180f;
    }

    public enum MazeAlgorithm { DepthFirstSearch, Kruskal, Prim }

    // ─────────────────────────────────────────────────────────────────────────
    //  Generator
    // ─────────────────────────────────────────────────────────────────────────

    public class ThetaMazeGenerator
    {
        public PolarCell[][] Cells { get; private set; }
        public ThetaMazeParams P { get; private set; }
        public int[] BinsPerRing { get; private set; }
        public PolarCell EntranceCell { get; private set; }
        public PolarCell ExitCell { get; private set; }

        System.Random _rng;

        // ── Entry point ───────────────────────────────────────────────────

        public void Generate(ThetaMazeParams p)
        {
            P = p;
            // Structural minimums — silently enforced
            P.Rings = Mathf.Max(2, P.Rings);
            P.BaseAngularBins = Mathf.Max(4, P.BaseAngularBins);
            P.InnerRadius = Mathf.Max(0.01f, P.InnerRadius);
            P.RingSpacing = Mathf.Max(0.01f, P.RingSpacing);

            _rng = new System.Random(p.Seed == 0 ? Environment.TickCount : p.Seed);

            BuildGrid();
            CarvePrestructure();
            RunAlgorithm();
            CarveExtraPassages();
            PlaceEntranceExit();
        }

        // ── Grid ──────────────────────────────────────────────────────────
        //  Alignment invariant: each ring's bin count is an integer multiple
        //  of all inner rings' counts (achieved via doubling only).
        //  This means every sector boundary angle on ring i also exists on ring i+1,
        //  so radial spoke walls always meet at a shared arc — no gaps.

        void BuildGrid()
        {
            BinsPerRing = new int[P.Rings];
            Cells = new PolarCell[P.Rings][];

            int bins = P.BaseAngularBins;

            for (int i = 0; i < P.Rings; i++)
            {
                if (i > 0 && P.AdaptiveAngularResolution)
                {
                    float rMid = P.InnerRadius + (i + 0.5f) * P.RingSpacing;
                    float circ = 2f * Mathf.PI * rMid;
                    float idealBins = circ / P.RingSpacing;
                    if (idealBins > bins * 1.5f)
                        bins *= 2;
                }

                BinsPerRing[i] = bins;
                Cells[i] = new PolarCell[bins];

                float rIn = P.InnerRadius + i * P.RingSpacing;
                float rOut = P.InnerRadius + (i + 1) * P.RingSpacing;

                for (int j = 0; j < bins; j++)
                {
                    float t0 = j * (2f * Mathf.PI / bins);
                    float t1 = (j + 1) * (2f * Mathf.PI / bins);
                    Cells[i][j] = new PolarCell(i, j, t0, t1, rIn, rOut);
                }
            }
        }

        // ── Pre-structure ─────────────────────────────────────────────────

        void CarvePrestructure()
        {
            int lr = P.ThetaLoopRing;
            if (lr >= 0 && lr < P.Rings)
            {
                for (int j = 0; j < BinsPerRing[lr]; j++) RemoveAngularWall(lr, j);

                if (lr > 0)
                    for (int s = 0; s < P.ThetaInnerSpokes; s++)
                        CarveRadialPassage(lr, s * BinsPerRing[lr] / P.ThetaInnerSpokes, inward: true);

                if (lr < P.Rings - 1)
                    for (int s = 0; s < P.ThetaOuterSpokes; s++)
                        CarveRadialPassage(lr, s * BinsPerRing[lr] / P.ThetaOuterSpokes, inward: false);
            }

            for (int s = 0; s < P.PrecarvedSpokes; s++)
                for (int i = 0; i < P.Rings - 1; i++)
                    CarveRadialPassage(i, s * BinsPerRing[i] / P.PrecarvedSpokes, inward: false);
        }

        // ── Algorithms ───────────────────────────────────────────────────

        void RunAlgorithm()
        {
            switch (P.Algorithm)
            {
                case MazeAlgorithm.DepthFirstSearch: RunDFS(); break;
                case MazeAlgorithm.Kruskal: RunKruskal(); break;
                case MazeAlgorithm.Prim: RunPrim(); break;
            }
        }

        void RunDFS()
        {
            var stack = new Stack<PolarCell>();
            Cells[0][0].Visited = true;
            stack.Push(Cells[0][0]);

            while (stack.Count > 0)
            {
                var cur = stack.Peek();
                var nbrs = GetUnvisitedNeighbours(cur);
                if (nbrs.Count == 0) { stack.Pop(); continue; }

                nbrs.Sort((a, b) =>
                {
                    bool ar = a.Ring != cur.Ring, br = b.Ring != cur.Ring;
                    if (ar == br) return _rng.Next(-1, 2);
                    float r = (float)_rng.NextDouble();
                    return ar ? (r < P.RadialBias ? -1 : 1) : (r < P.RadialBias ? 1 : -1);
                });

                var next = nbrs[0];
                CarvePassage(cur, next);
                next.Visited = true;
                stack.Push(next);
            }
        }

        void RunKruskal()
        {
            int FlatId(int ring, int sector)
            {
                int id = 0;
                for (int k = 0; k < ring; k++) id += BinsPerRing[k];
                return id + sector;
            }
            int total = 0;
            for (int i = 0; i < P.Rings; i++) total += BinsPerRing[i];

            var parent = new int[total];
            for (int k = 0; k < total; k++) parent[k] = k;

            int FindRoot(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
            void Union(int a, int b) => parent[FindRoot(a)] = FindRoot(b);

            var walls = new List<(PolarCell a, PolarCell b)>();
            for (int i = 0; i < P.Rings; i++)
                for (int j = 0; j < BinsPerRing[i]; j++)
                {
                    var c = Cells[i][j];
                    if (i < P.Rings - 1) { var nb = GetOuterNeighbour(c); if (nb != null) walls.Add((c, nb)); }
                    walls.Add((c, GetCWNeighbour(c)));
                }

            Shuffle(walls);
            foreach (var (a, b) in walls)
            {
                int ra = FindRoot(FlatId(a.Ring, a.Sector));
                int rb = FindRoot(FlatId(b.Ring, b.Sector));
                if (ra != rb) { CarvePassage(a, b); Union(ra, rb); }
            }
        }

        void RunPrim()
        {
            var start = Cells[0][0];
            start.Visited = true;
            var frontier = new List<(PolarCell from, PolarCell to)>();
            AddFrontier(start, frontier);

            while (frontier.Count > 0)
            {
                int idx = _rng.Next(frontier.Count);
                var (fr, to) = frontier[idx];
                frontier.RemoveAt(idx);
                if (to.Visited) continue;
                CarvePassage(fr, to);
                to.Visited = true;
                AddFrontier(to, frontier);
            }
        }

        void AddFrontier(PolarCell c, List<(PolarCell, PolarCell)> f)
        {
            foreach (var nb in GetAllNeighbours(c))
                if (!nb.Visited) f.Add((c, nb));
        }

        void CarveExtraPassages()
        {
            if (P.ExtraPassageRatio <= 0f) return;
            var walls = new List<(PolarCell, PolarCell)>();
            for (int i = 0; i < P.Rings; i++)
                for (int j = 0; j < BinsPerRing[i]; j++)
                {
                    var c = Cells[i][j];
                    if (i < P.Rings - 1 && c.WallOuter) { var nb = GetOuterNeighbour(c); if (nb != null) walls.Add((c, nb)); }
                    if (c.WallCW) walls.Add((c, GetCWNeighbour(c)));
                }
            Shuffle(walls);
            int extra = Mathf.RoundToInt(walls.Count * P.ExtraPassageRatio);
            for (int k = 0; k < Mathf.Min(extra, walls.Count); k++)
                CarvePassage(walls[k].Item1, walls[k].Item2);
        }

        void PlaceEntranceExit()
        {
            EntranceCell = AngleToCell(P.Rings - 1, P.EntranceAngleDeg * Mathf.Deg2Rad);
            EntranceCell.WallOuter = false;
            ExitCell = AngleToCell(0, P.ExitAngleDeg * Mathf.Deg2Rad);
            ExitCell.WallInner = false;
        }

        PolarCell AngleToCell(int ring, float theta)
        {
            theta = ((theta % (2f * Mathf.PI)) + 2f * Mathf.PI) % (2f * Mathf.PI);
            int j = Mathf.Clamp(Mathf.FloorToInt(theta / (2f * Mathf.PI) * BinsPerRing[ring]), 0, BinsPerRing[ring] - 1);
            return Cells[ring][j];
        }

        // ── Passage carving ───────────────────────────────────────────────

        void CarvePassage(PolarCell a, PolarCell b)
        {
            if (a.Ring == b.Ring)
            {
                int bins = BinsPerRing[a.Ring];
                int diff = ((b.Sector - a.Sector) % bins + bins) % bins;
                if (diff == 1) { a.WallCW = false; b.WallCCW = false; }
                else if (diff == bins - 1) { a.WallCCW = false; b.WallCW = false; }
            }
            else
            {
                if (b.Ring > a.Ring) { a.WallOuter = false; b.WallInner = false; }
                else { a.WallInner = false; b.WallOuter = false; }
            }
        }

        void RemoveAngularWall(int ring, int j)
        {
            int bins = BinsPerRing[ring];
            Cells[ring][j].WallCW = false;
            Cells[ring][(j + 1) % bins].WallCCW = false;
        }

        void CarveRadialPassage(int ring, int sector, bool inward)
        {
            sector = Mathf.Clamp(sector, 0, BinsPerRing[ring] - 1);
            if (inward)
            {
                if (ring == 0) return;
                var a = Cells[ring][sector]; var b = GetInnerNeighbour(a);
                if (b != null) { a.WallInner = false; b.WallOuter = false; }
            }
            else
            {
                if (ring >= P.Rings - 1) return;
                var a = Cells[ring][sector]; var b = GetOuterNeighbour(a);
                if (b != null) { a.WallOuter = false; b.WallInner = false; }
            }
        }

        // ── Neighbours ───────────────────────────────────────────────────

        public PolarCell GetOuterNeighbour(PolarCell c)
        {
            if (c.Ring >= P.Rings - 1) return null;
            float mid = (c.ThetaStart + c.ThetaEnd) * 0.5f;
            int bins = BinsPerRing[c.Ring + 1];
            int j = Mathf.FloorToInt(mid / (2f * Mathf.PI) * bins) % bins;
            return Cells[c.Ring + 1][j];
        }

        public PolarCell GetInnerNeighbour(PolarCell c)
        {
            if (c.Ring == 0) return null;
            float mid = (c.ThetaStart + c.ThetaEnd) * 0.5f;
            int bins = BinsPerRing[c.Ring - 1];
            int j = Mathf.FloorToInt(mid / (2f * Mathf.PI) * bins) % bins;
            return Cells[c.Ring - 1][j];
        }

        PolarCell GetCWNeighbour(PolarCell c) => Cells[c.Ring][(c.Sector + 1) % BinsPerRing[c.Ring]];
        PolarCell GetCCWNeighbour(PolarCell c) => Cells[c.Ring][(c.Sector - 1 + BinsPerRing[c.Ring]) % BinsPerRing[c.Ring]];

        List<PolarCell> GetAllNeighbours(PolarCell c)
        {
            var list = new List<PolarCell>(4);
            var inn = GetInnerNeighbour(c); if (inn != null) list.Add(inn);
            var out_ = GetOuterNeighbour(c); if (out_ != null) list.Add(out_);
            list.Add(GetCCWNeighbour(c));
            list.Add(GetCWNeighbour(c));
            return list;
        }

        List<PolarCell> GetUnvisitedNeighbours(PolarCell c)
        {
            var list = new List<PolarCell>(4);
            foreach (var nb in GetAllNeighbours(c)) if (!nb.Visited) list.Add(nb);
            return list;
        }

        public List<PolarCell> GetPassableNeighbours(PolarCell c)
        {
            var list = new List<PolarCell>(4);
            int bins = BinsPerRing[c.Ring];
            if (!c.WallCW) list.Add(Cells[c.Ring][(c.Sector + 1) % bins]);
            if (!c.WallCCW) list.Add(Cells[c.Ring][(c.Sector - 1 + bins) % bins]);
            if (!c.WallOuter) { var nb = GetOuterNeighbour(c); if (nb != null) list.Add(nb); }
            if (!c.WallInner) { var nb = GetInnerNeighbour(c); if (nb != null) list.Add(nb); }
            return list;
        }

        void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            { int j = _rng.Next(i + 1); (list[i], list[j]) = (list[j], list[i]); }
        }
    }
}
