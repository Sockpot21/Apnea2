using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SpiderBotPrototypeBuilder
{
    private const string Root = "Assets/Enemy/SpiderBot";
    private const string DefinitionPath = Root + "/Definitions/PrototypeSpiderBot.asset";
    private const string ShellMaterialPath = Root + "/Materials/SpiderShell.mat";
    private const string JointMaterialPath = Root + "/Materials/SpiderJoints.mat";
    private const string EyeMaterialPath = Root + "/Materials/SpiderEyes.mat";
    private const string LimbPhysicsMaterialPath = Root + "/Materials/SpiderLimb.physicsMaterial";
    private const string FootPhysicsMaterialPath = Root + "/Materials/SpiderFoot.physicsMaterial";
    private const string PrefabPath = Root + "/Prefabs/PrototypeSpiderBot.prefab";
    private const string ScenePath = "Assets/Scenes/SampleScene.unity";

    [InitializeOnLoadMethod]
    private static void UpgradePhysicsPrototypeOnce()
    {
        if (AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(FootPhysicsMaterialPath) != null) return;
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.playModeStateChanged -= BuildAfterPlayMode;
                EditorApplication.playModeStateChanged += BuildAfterPlayMode;
                return;
            }
            BuildPhysicsUpgradeAssets();
        };
    }

    private static void BuildAfterPlayMode(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode) return;
        EditorApplication.playModeStateChanged -= BuildAfterPlayMode;
        if (AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(FootPhysicsMaterialPath) == null)
            BuildPhysicsUpgradeAssets();
    }

    private static void BuildPhysicsUpgradeAssets()
    {
        BuildAssets();
        Debug.Log("[SpiderBotBuilder] Upgraded the prototype to the active-physics leg rig.");
    }

    [MenuItem("Tools/Enemies/Build and Place Prototype Spider Bot")]
    public static void BuildAndPlace()
    {
        GameObject prefab = BuildAssets();
        PlaceInSampleScene(prefab);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SpiderBotBuilder] Created {PrefabPath} and placed it in {ScenePath}.");
    }

    private static GameObject BuildAssets()
    {
        SpiderBotDefinition definition = GetOrCreateDefinition();
        Material shell = GetOrCreateMaterial(ShellMaterialPath, definition.shellColor, false);
        Material joints = GetOrCreateMaterial(JointMaterialPath, definition.jointColor, false);
        Material eyes = GetOrCreateMaterial(EyeMaterialPath, definition.eyeColor, true);
        PhysicsMaterial limbs = GetOrCreatePhysicsMaterial(LimbPhysicsMaterialPath, .08f, .05f);
        PhysicsMaterial feet = GetOrCreatePhysicsMaterial(FootPhysicsMaterialPath, 1.25f, 1.15f);
        EditorUtility.SetDirty(definition);
        GameObject prefab = BuildPrefab(definition, shell, joints, eyes, limbs, feet);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        return prefab;
    }

    // Entry point for -executeMethod.
    public static void BuildBatch() => BuildAndPlace();

    private static SpiderBotDefinition GetOrCreateDefinition()
    {
        SpiderBotDefinition definition = AssetDatabase.LoadAssetAtPath<SpiderBotDefinition>(DefinitionPath);
        if (definition != null) return definition;
        definition = ScriptableObject.CreateInstance<SpiderBotDefinition>();
        AssetDatabase.CreateAsset(definition, DefinitionPath);
        return definition;
    }

    private static Material GetOrCreateMaterial(string path, Color color, bool emissive)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                ?? Shader.Find("Standard");
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
        }
        material.color = color;
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (emissive)
        {
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", color * 3f);
        }
        EditorUtility.SetDirty(material);
        return material;
    }

    private static PhysicsMaterial GetOrCreatePhysicsMaterial(string path,
        float staticFriction, float dynamicFriction)
    {
        PhysicsMaterial material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(path);
        if (material == null)
        {
            material = new PhysicsMaterial(System.IO.Path.GetFileNameWithoutExtension(path));
            AssetDatabase.CreateAsset(material, path);
        }
        material.staticFriction = staticFriction;
        material.dynamicFriction = dynamicFriction;
        material.bounciness = 0f;
        EditorUtility.SetDirty(material);
        return material;
    }

    private static GameObject BuildPrefab(SpiderBotDefinition definition, Material shell,
        Material joints, Material eyes, PhysicsMaterial limbPhysics, PhysicsMaterial footPhysics)
    {
        GameObject root = new("Prototype Spider Bot");
        Rigidbody body = root.AddComponent<Rigidbody>();
        body.mass = definition.bodyMass;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearDamping = .12f;
        body.angularDamping = .8f;
        body.centerOfMass = definition.centreOfMassOffset;
        BoxCollider collider = root.AddComponent<BoxCollider>();
        collider.center = new Vector3(0f, .15f, 0f);
        collider.size = new Vector3(2.2f, 2.6f, 1.9f);

        ProceduralSpiderLegs legs = root.AddComponent<ProceduralSpiderLegs>();
        SpiderBotController controller = root.AddComponent<SpiderBotController>();
        SpiderBotHealth health = root.AddComponent<SpiderBotHealth>();

        CreatePrimitive("Tall Industrial Chassis", PrimitiveType.Cube, root.transform,
            new Vector3(0f, .15f, 0f), new Vector3(2.2f, 2.5f, 1.75f), shell);
        CreatePrimitive("Lower Leg Mount", PrimitiveType.Cube, root.transform,
            new Vector3(0f, -.98f, 0f), new Vector3(2.5f, .35f, 2.05f), joints);
        CreatePrimitive("Top Armour Cap", PrimitiveType.Cube, root.transform,
            new Vector3(0f, 1.46f, -.03f), new Vector3(2.05f, .28f, 1.65f), shell);
        CreatePrimitive("Front Sensor Plate", PrimitiveType.Cube, root.transform,
            new Vector3(0f, .22f, .94f), new Vector3(1.7f, 1.45f, .16f), joints);
        CreatePrimitive("Optical Visor", PrimitiveType.Cube, root.transform,
            new Vector3(0f, .58f, 1.05f), new Vector3(1.08f, .34f, .12f), eyes);

        for (int side = -1; side <= 1; side += 2)
        {
            CreatePrimitive($"Side Power Cell {side}", PrimitiveType.Cylinder, root.transform,
                new Vector3(side * 1.23f, .2f, -.18f), new Vector3(.42f, .72f, .42f), joints);
            CreatePrimitive($"Hip Housing Front {side}", PrimitiveType.Cube, root.transform,
                new Vector3(side * 1.2f, -.62f, .72f), new Vector3(.42f, .5f, .58f), shell);
            CreatePrimitive($"Hip Housing Rear {side}", PrimitiveType.Cube, root.transform,
                new Vector3(side * 1.2f, -.62f, -.72f), new Vector3(.42f, .5f, .58f), shell);
            CreatePrimitive($"Antenna {side}", PrimitiveType.Cylinder, root.transform,
                new Vector3(side * .67f, 2.15f, -.25f), new Vector3(.055f, .72f, .055f), joints);
            CreatePrimitive($"Antenna Tip {side}", PrimitiveType.Sphere, root.transform,
                new Vector3(side * .67f, 2.88f, -.25f), Vector3.one * .13f, eyes);
        }

        Transform muzzle = new GameObject("Burn Muzzle").transform;
        muzzle.SetParent(root.transform, false);
        muzzle.localPosition = new Vector3(0f, -.35f, 1.22f);

        legs.Configure(definition, shell, joints, limbPhysics, footPhysics, eyes);
        controller.Configure(definition, body, legs, health, muzzle);
        health.Configure(definition, controller, legs, body);

        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        return prefab;
    }

    private static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent,
        Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localScale = localScale;
        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null) Object.DestroyImmediate(collider);
        Renderer renderer = primitive.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;
        return primitive;
    }

    private static void PlaceInSampleScene(GameObject prefab)
    {
        Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.GetComponent<SpiderBotController>() == null) continue;
            root.transform.position = new Vector3(-16.8f, 11.65f, 2.35f);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            return;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        instance.name = "Prototype Spider Bot";
        // The existing capsule enemy stands on the arena around y=9.1. The
        // spider's root is its body centre, approximately 2.55m above that floor.
        instance.transform.position = new Vector3(-16.8f, 11.65f, 2.35f);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }
}
