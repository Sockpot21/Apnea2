using UnityEngine;
using System.Collections.Generic;

public class PolarMesh
{
    public Mesh Build(PolarGrid grid)
    {
        Mesh mesh = new Mesh();
        mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

        List<Vector3> vertices = new List<Vector3>();
        List<int> triangles = new List<int>();

        // -------------------------------------------------
        // DEBUG OUTPUT: ONE TRIANGLE PER CELL
        // -------------------------------------------------
        foreach (var cell in grid.Cells)
        {
            Vector3 center = Vector3.zero;

            Vector3 a = PolarToWorld(cell.InnerRadius, cell.StartAngle);
            Vector3 b = PolarToWorld(cell.InnerRadius, cell.EndAngle);
            Vector3 c = PolarToWorld(cell.OuterRadius, cell.StartAngle);

            int i0 = vertices.Count;
            vertices.Add(center);
            vertices.Add(a);
            vertices.Add(b);

            triangles.Add(i0);
            triangles.Add(i0 + 1);
            triangles.Add(i0 + 2);
        }

        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();

        return mesh;
    }

    private Vector3 PolarToWorld(float radius, float angle)
    {
        float x = Mathf.Cos(angle) * radius;
        float z = Mathf.Sin(angle) * radius;
        return new Vector3(x, 0, z);
    }
}