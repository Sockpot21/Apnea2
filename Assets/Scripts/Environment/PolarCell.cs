using UnityEngine;

[System.Serializable]
public class PolarCell
{
    public int Ring;
    public int Segment;
    public int SegmentsInThisRing;

    // WorldPosition = geometric centre of this cell's passable corridor area.
    //
    // Previously this was placed at the outer ring boundary and the segment's
    // start angle, which put it on/near a wall.  It is now computed as:
    //   radius = centerRadius + (Ring - 0.5) * ringWidth   ← radial midpoint of corridor
    //   angle  = (Segment + 0.5) * angleStep               ← angular centre of cell
    //
    // This makes the solution path gizmo run through the centre of each passage
    // rather than along the wall faces.
    public Vector3 WorldPosition { get; private set; }

    public bool Visited = false;

    public bool ClockwiseOpen  = false;
    public bool OutwardOpen    = false;
    public bool IsExit         = false;
    public bool OnSolutionPath = false;

    public PolarCell(int ring, int segment, int segmentsInRing)
    {
        Ring               = ring;
        Segment            = segment;
        SegmentsInThisRing = segmentsInRing;
    }

    public void CalculateWorldPosition(float centerRadius, float ringWidth)
    {
        if (Ring == 0)
        {
            // Hub sits at the origin (centre of the maze)
            WorldPosition = Vector3.zero;
            return;
        }

        // Radial midpoint of the corridor band for this ring
        float radius = centerRadius + (Ring - 0.5f) * ringWidth;

        // Angular centre of this segment
        float angleStep = Mathf.PI * 2f / SegmentsInThisRing;
        float angle     = (Segment + 0.5f) * angleStep;

        WorldPosition = new Vector3(
            Mathf.Cos(angle) * radius,
            0f,
            Mathf.Sin(angle) * radius
        );
    }
}
