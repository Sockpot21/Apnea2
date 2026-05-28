using System.Collections.Generic;

public class PolarCell2
{
    public int RingIndex;
    public int SectorIndex;

    public float InnerRadius;
    public float OuterRadius;

    public float StartAngle;
    public float EndAngle;

    public PolarCell2 Clockwise;
    public PolarCell2 CounterClockwise;

    public List<PolarCell2> InwardNeighbors = new List<PolarCell2>();
    public List<PolarCell2> OutwardNeighbors = new List<PolarCell2>();

    public List<PolarCell2> Connections = new List<PolarCell2>();

    public bool Visited = false;

    public bool HasConnection(PolarCell2 other)
    {
        return other != null && Connections.Contains(other);
    }

    public bool HasOutwardConnection()
    {
        for (int i = 0; i < OutwardNeighbors.Count; i++)
            if (Connections.Contains(OutwardNeighbors[i]))
                return true;

        return false;
    }

    public bool HasInwardConnection()
    {
        for (int i = 0; i < InwardNeighbors.Count; i++)
            if (Connections.Contains(InwardNeighbors[i]))
                return true;

        return false;
    }
}