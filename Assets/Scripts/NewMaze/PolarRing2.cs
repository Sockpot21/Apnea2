using System.Collections.Generic;

public class PolarRing2
{
    public int RingIndex;
    public float InnerRadius;
    public float OuterRadius;

    public List<PolarCell2> Cells = new List<PolarCell2>();

    public PolarRing2(int ringIndex, float innerRadius, float outerRadius)
    {
        RingIndex = ringIndex;
        InnerRadius = innerRadius;
        OuterRadius = outerRadius;
    }
}