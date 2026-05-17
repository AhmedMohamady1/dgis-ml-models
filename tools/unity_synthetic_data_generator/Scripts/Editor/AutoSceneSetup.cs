using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

public class AutoSceneSetup : EditorWindow
{
    private YOLOClassConfig datasetConfig;
    private int numberOfLocations = 9;
    private float gridSpacing = 50f; 
    private Vector2 scrollPos;

    private string classListText = @"0 : Beaver
1 : BrichTree
2 : Lynx
3 : Marten
4 : Squirrel
5 : Warbler
6 : Woodpecker
7 : AbandonedCar
8 : Bicycle
9 : CalicoCat
10 : Coyote
11 : Crow
12 : FeralDog
13 : Pigeon
14 : Rat
15 : Skeleton
16 : Agave
17 : DesertBighornSheep
18 : Tortoise
19 : DesertWillow
20 : DorcasGazelle
21 : Pelican
22 : RattleSnake
23 : SeaBird
24 : AlpineMarmot
25 : Elk
26 : GoldenEagle
27 : GrizzlyBear
28 : Heather
29 : LionMountain
30 : Bison
31 : Black-footedFerret
32 : Hyena
33 : Lion
34 : TurtleOrnateBox
35 : Pipit
36 : Elephant
37 : Quail
38 : Zebra
39 : BigSnake
40 : CacaoTree
41 : Capybara
42 : Gorilla
43 : GreenAnaconda
44 : GreenLguana
45 : Leopard
46 : Okapi
47 : Pangolin
48 : PygmyChimpanzee
49 : Sloth
50 : AloeVeraPlant
51 : DesertScorpion
52 : DromedaryCamel
53 : FennecFox
54 : Gecko
55 : HornedLizard
56 : Jarboa
57 : SalviaPlant
58 : AmericanBlackBear
59 : Hickory
60 : Maple
61 : Raccoon
62 : RedFox
63 : White-tailedDear
64 : WoodFrog
65 : Cypress
66 : Cactus
67 : PricklyPearCactus
68 : FlowerGazania
69 : FlowerEmpodium
70 : Conifer
71 : PineTree";

    [MenuItem("Tools/YOLO/Auto Scene Setup")]
    public static void ShowWindow()
    {
        GetWindow<AutoSceneSetup>("Auto Scene Setup");
    }

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField("1. Dataset Configuration Setup", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("Generate a YOLO Dataset Config asset. This will parse the list below and attempt to auto-link prefabs matching the class names.", MessageType.Info);

        classListText = EditorGUILayout.TextArea(classListText, GUILayout.Height(150));

        if (GUILayout.Button("Generate Config & Auto-Link Prefabs", GUILayout.Height(30)))
        {
            GenerateDatasetConfig();
        }

        EditorGUILayout.Space(20);
        EditorGUILayout.LabelField("2. Scene Generation", EditorStyles.boldLabel);

        datasetConfig = (YOLOClassConfig)EditorGUILayout.ObjectField(
            "Dataset Config Asset",
            datasetConfig,
            typeof(YOLOClassConfig),
            false
        );

        numberOfLocations = EditorGUILayout.IntSlider("Number of Locations", numberOfLocations, 1, 25);
        gridSpacing = EditorGUILayout.FloatField("Grid Spacing", gridSpacing);

        EditorGUILayout.Space();

        GUI.enabled = datasetConfig != null;
        if (GUILayout.Button("Setup Complete Scene", GUILayout.Height(40)))
        {
            SetupCompleteScene();
        }
        GUI.enabled = true;

        if (datasetConfig == null)
        {
            EditorGUILayout.HelpBox("Please generate or assign a Dataset Config Asset first.", MessageType.Warning);
        }

        EditorGUILayout.EndScrollView();
    }

    void GenerateDatasetConfig()
    {
        YOLOClassConfig config = ScriptableObject.CreateInstance<YOLOClassConfig>();
        string[] lines = classListText.Split('\n');
        
        int linkedPrefabsCount = 0;

        foreach (string line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            
            string[] parts = line.Split(':');
            if (parts.Length == 2)
            {
                if (int.TryParse(parts[0].Trim(), out int id))
                {
                    string className = parts[1].Trim();
                    YOLOClass newClass = new YOLOClass { classId = id, className = className, prefabs = new List<GameObject>() };

                    string[] searchResults = AssetDatabase.FindAssets($"{className} t:Prefab");
                    foreach (string guid in searchResults)
                    {
                        string path = AssetDatabase.GUIDToAssetPath(guid);
                        GameObject foundPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        
                        if (foundPrefab != null && foundPrefab.name.ToLower().Contains(className.ToLower()))
                        {
                            newClass.prefabs.Add(foundPrefab);
                            linkedPrefabsCount++;
                        }
                    }

                    config.classes.Add(newClass);
                }
            }
        }

        string assetPath = "Assets/YOLODatasetConfig.asset";
        AssetDatabase.CreateAsset(config, assetPath);
        AssetDatabase.SaveAssets();

        datasetConfig = config;
        
        Debug.Log($"Generated YOLO Config with {config.classes.Count} classes. Auto-linked {linkedPrefabsCount} prefabs. Saved to: {assetPath}");
        Selection.activeObject = config; 
    }

    void SetupCompleteScene()
    {
        GameObject yoloScene = new GameObject("YOLO_Training_Scene");
        AddYOLOGenerator(yoloScene.transform);
        CreateLightingSetup(yoloScene.transform);
        AddDefaultSkybox();
        Debug.Log("YOLO training scene setup complete!");
    }

    YOLODataGenerator AddYOLOGenerator(Transform parent)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            GameObject cameraObj = new GameObject("Main Camera");
            mainCamera = cameraObj.AddComponent<Camera>();
            cameraObj.AddComponent<AudioListener>();
            cameraObj.tag = "MainCamera";
        }

        mainCamera.transform.SetParent(parent);

        YOLODataGenerator generator = mainCamera.gameObject.GetComponent<YOLODataGenerator>();
        if (generator == null)
        {
            generator = mainCamera.gameObject.AddComponent<YOLODataGenerator>();
        }
        
        generator.renderCamera = mainCamera;
        generator.datasetConfig = datasetConfig;

        GenerateSpawnLocations(generator);
        ConfigureCamera(mainCamera);

        return generator;
    }

    void GenerateSpawnLocations(YOLODataGenerator generator)
    {
        generator.spawnLocations.Clear();
        int gridSize = Mathf.CeilToInt(Mathf.Sqrt(numberOfLocations));

        for (int i = 0; i < numberOfLocations; i++)
        {
            int x = i % gridSize;
            int z = i / gridSize;

            ObjectSpawnLocation location = new ObjectSpawnLocation();
            location.locationName = $"Auto_Location_{i}";
            
            location.manualPosition = new Vector3(
                (x - gridSize / 2f) * gridSpacing,
                0,
                (z - gridSize / 2f) * gridSpacing
            );
            
            location.rotation = new Vector3(0, Random.Range(0, 360), 0);
            // REMOVED: Scale assignment is gone

            generator.spawnLocations.Add(location);
        }
    }

    void ConfigureCamera(Camera camera)
    {
        camera.clearFlags = CameraClearFlags.Skybox;
        camera.fieldOfView = 45f; // Set default FOV to 45
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 200f; 
        camera.transform.position = new Vector3(0, 20, -20);
        camera.transform.rotation = Quaternion.Euler(45, 0, 0);
    }

    void CreateLightingSetup(Transform parent)
    {
        GameObject lightObj = new GameObject("Directional Light");
        lightObj.transform.SetParent(parent);
        Light light = lightObj.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1f;
        light.color = Color.white;
        light.transform.rotation = Quaternion.Euler(50, -30, 0);

        YOLODataGenerator generator = Object.FindFirstObjectByType<YOLODataGenerator>(); // Fixed Deprecation
        if (generator != null)
        {
            generator.sceneLights.Clear();
            generator.sceneLights.Add(light);
        }
    }

    void AddDefaultSkybox()
    {
        Material[] allMaterials = Resources.FindObjectsOfTypeAll<Material>();
        Material skyboxMaterial = null;

        foreach (Material mat in allMaterials)
        {
            if (mat.shader != null && mat.shader.name.Contains("Skybox"))
            {
                skyboxMaterial = mat;
                break;
            }
        }

        if (skyboxMaterial != null)
        {
            RenderSettings.skybox = skyboxMaterial;
            YOLODataGenerator generator = Object.FindFirstObjectByType<YOLODataGenerator>(); // Fixed Deprecation
            if (generator != null)
            {
                generator.skyboxMaterials.Clear();
                generator.skyboxMaterials.Add(skyboxMaterial);
            }
        }
    }
}