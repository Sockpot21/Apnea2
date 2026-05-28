using System.Collections.Generic;
using UnityEngine;

public class PolarGrid
{
    private readonly int ringCount;
    private readonly int initialSectorCount;
    private readonly float ringThickness;
    private readonly System.Random rng;

    private readonly List<PolarRing2> rings = new List<PolarRing2>();
    private readonly List<PolarCell2> allCells = new List<PolarCell2>();

    public IReadOnlyList<PolarCell2> Cells => allCells;

    public PolarGrid(int ringCount, int initialSectorCount, float ringThickness, System.Random rng)
    {
        this.ringCount = ringCount;
        this.initialSectorCount = initialSectorCount;
        this.ringThickness = ringThickness;
        this.rng = rng;

        BuildGrid();
        LinkNeighbors();
    }

    private void BuildGrid()
    {
        rings.Clear();
        allCells.Clear();

        int sectorCount = initialSectorCount;

        for (int r = 0; r < ringCount; r++)
        {
            float innerRadius = r * ringThickness;
            float outerRadius = (r + 1) * ringThickness;

            if (r > 0 && r % 2 == 0)
                sectorCount *= 2;

            PolarRing2 ring = new PolarRing2(r, innerRadius, outerRadius);
            rings.Add(ring);

            float step = (Mathf.PI * 2f) / sectorCount;

            for (int i = 0; i < sectorCount; i++)
            {
                PolarCell2 cell = new PolarCell2
                {
                    RingIndex = r,
                    SectorIndex = i,

                    InnerRadius = innerRadius,
                    OuterRadius = outerRadius,

                    StartAngle = i * step,
                    EndAngle = (i + 1) * step
                };

                ring.Cells.Add(cell);
                allCells.Add(cell);
            }
        }
    }

    private void LinkNeighbors()
    {
        for (int r = 0; r < rings.Count; r++)
        {
            var ring = rings[r];
            int count = ring.Cells.Count;

            for (int i = 0; i < count; i++)
            {
                PolarCell2 cell = ring.Cells[i];

                cell.Clockwise = ring.Cells[(i + 1) % count];
                cell.CounterClockwise = ring.Cells[(i - 1 + count) % count];

                if (r > 0)
                {
                    var inner = rings[r - 1];
                    float ratio = (float)inner.Cells.Count / count;

                    int start = Mathf.FloorToInt(i * ratio);
                    int end = Mathf.FloorToInt((i + 1) * ratio);

                    for (int j = start; j <= end; j++)
                        cell.InwardNeighbors.Add(inner.Cells[j % inner.Cells.Count]);
                }

                if (r < rings.Count - 1)
                {
                    var outer = rings[r + 1];
                    float ratio = (float)outer.Cells.Count / count;

                    int start = Mathf.FloorToInt(i * ratio);
                    int end = Mathf.FloorToInt((i + 1) * ratio);

                    for (int j = start; j <= end; j++)
                        cell.OutwardNeighbors.Add(outer.Cells[j % outer.Cells.Count]);
                }
            }
        }
    }

    public void GenerateMazeDFS(System.Random rng)
    {
        Stack<PolarCell2> stack = new Stack<PolarCell2>();

        PolarCell2 start = allCells[0];
        start.Visited = true;

        stack.Push(start);

        while (stack.Count > 0)
        {
            PolarCell2 current = stack.Peek();
            PolarCell2 next = GetNext(current, rng);

            if (next != null)
            {
                current.Connections.Add(next);
                next.Connections.Add(current);

                next.Visited = true;
                stack.Push(next);
            }
            else
            {
                stack.Pop();
            }
        }
    }

    private PolarCell2 GetNext(PolarCell2 cell, System.Random rng)
    {
        List<PolarCell2> options = new List<PolarCell2>();

        Add(cell.Clockwise, options);
        Add(cell.CounterClockwise, options);

        for (int i = 0; i < cell.InwardNeighbors.Count; i++)
            Add(cell.InwardNeighbors[i], options);

        for (int i = 0; i < cell.OutwardNeighbors.Count; i++)
            Add(cell.OutwardNeighbors[i], options);

        if (options.Count == 0)
            return null;

        return options[rng.Next(options.Count)];
    }

    private void Add(PolarCell2 c, List<PolarCell2> list)
    {
        if (c != null && !c.Visited)
            list.Add(c);
    }
}