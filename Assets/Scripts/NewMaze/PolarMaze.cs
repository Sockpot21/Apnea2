using UnityEngine;

public class PolarMaze : MonoBehaviour
{
    [Header("Maze Settings")]
    public int ringCount = 10;
    public int initialSectorCount = 6;
    public float ringThickness = 2f;

    [Header("Generation")]
    public bool useRandomSeed = true;
    public int seed = 1234;

    private PolarGrid grid;

    private void Start()
    {
        Generate();
    }

    [ContextMenu("Generate")]
    public void Generate()
    {
        System.Random rng = new System.Random(useRandomSeed ? Random.Range(int.MinValue, int.MaxValue) : seed);

        grid = new PolarGrid(ringCount, initialSectorCount, ringThickness, rng);
        grid.GenerateMazeDFS(rng);

        Debug.Log("Maze generated.");
    }
}