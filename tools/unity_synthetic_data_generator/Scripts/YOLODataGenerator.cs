using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

[System.Serializable]
public class ObjectSpawnLocation
{
    public string locationName = "Location";
    [Tooltip("Drag your Ground Plane / Environment GameObject here!")]
    public Transform linkedEnvironmentPlane; 
    public Vector3 manualPosition; 
    public Vector3 rotation;
    public Vector3 scale = Vector3.one;
    public Vector3 positionOffset = Vector3.zero;
    public Vector3 rotationOffset = Vector3.zero;
    [Range(0.5f, 3f)] public float scaleMultiplier = 1f;

    public Vector3 GetCenterPosition() 
    {
        if (linkedEnvironmentPlane != null)
        {
            Renderer r = linkedEnvironmentPlane.GetComponent<Renderer>();
            if (r != null) return r.bounds.center; 
            return linkedEnvironmentPlane.position; 
        }
        return manualPosition;
    }

    public Quaternion GetRotation() 
    {
        if (linkedEnvironmentPlane != null) return linkedEnvironmentPlane.rotation;
        return Quaternion.Euler(rotation);
    }
}

public class YOLODataGenerator : MonoBehaviour
{
    [Header("Dataset Configuration")]
    public YOLOClassConfig datasetConfig;
    public int targetTotalImages = 60000; // Updated for your large dataset goal
    
    [Header("Camera Settings")]
    public Camera renderCamera;
    public int imageWidth = 640;
    public int imageHeight = 480;
    [Range(20, 90)] public float fieldOfView = 45f; 

    [Header("Drone Camera Setup")]
    public float droneAltitudeMin = 5f;   
    public float droneAltitudeMax = 30f;  
    public float droneRadiusMin = 0f;     
    public float droneRadiusMax = 90f;    
    public float cameraFramingOffset = 15f; 
    
    [Header("Spawn & Environment Settings")]
    public int minObjectsPerImage = 3;  
    public int maxObjectsPerImage = 12; 
    public float spawnSpreadRadius = 15f; 
    public float minDistanceBetweenObjects = 3.0f; 
    
    [Tooltip("If an object touches the edge of the screen, destroy it? (Prevents cut-off animals)")]
    public bool destroyObjectsTouchingEdge = true;
    [Range(0.1f, 1f)] public float minVisibilityThreshold = 0.85f; 
    
    [Header("Scaling Controls")]
    public float globalObjectScaleMultiplier = 1.0f; 
    public Vector2 randomScaleRange = new Vector2(0.9f, 1.1f); 
    
    public List<ObjectSpawnLocation> spawnLocations = new List<ObjectSpawnLocation>();
    public List<Material> skyboxMaterials = new List<Material>();
    public List<Light> sceneLights = new List<Light>();

    [Header("Output Settings")]
    public string datasetPath = "Dataset";
    [Range(10, 100)] public int jpgQuality = 95; 

    private string imagesFolder;
    private string labelsFolder;
    private RenderTexture realTexture;
    private RenderTexture idTexture;
    private Texture2D screenshot;
    private Texture2D idScreenshot;
    private bool isCapturing = false;
    private int[] classAppearanceCounts;
    private Material unlitColorMaterial;
    
    private int imagesCaptured = 0;
    private int startIndex = 0; 
    private Coroutine captureCoroutine;

    private static readonly Color32[] ID_COLORS = new Color32[] {
        new Color32(250, 12, 250, 255), new Color32(12, 250, 250, 255), new Color32(250, 250, 12, 255),
        new Color32(250, 100, 12, 255), new Color32(100, 12, 250, 255), new Color32(12, 250, 100, 255),
        new Color32(250, 50, 150, 255), new Color32(50, 150, 250, 255), new Color32(150, 250, 50, 255),
        new Color32(250, 150, 50, 255), new Color32(150, 50, 250, 255), new Color32(50, 250, 150, 255),
        new Color32(200, 10, 100, 255), new Color32(10, 100, 200, 255), new Color32(100, 200, 10, 255),
        new Color32(200, 100, 10, 255), new Color32(100, 10, 200, 255), new Color32(10, 200, 100, 255)
    };

    private class SpawnedInstance
    {
        public GameObject gameObject;
        public YOLOClass yoloClass;
        public Color32 idColor;
        public int expectedTotalPixels;
        public Dictionary<Renderer, Material[]> originalMaterials = new Dictionary<Renderer, Material[]>();
    }

    void Start()
    {
        InitializeSystem();
    }

    void InitializeSystem()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string basePath = Path.Combine(projectRoot, datasetPath);
        imagesFolder = Path.Combine(basePath, "Images") + Path.DirectorySeparatorChar;
        labelsFolder = Path.Combine(basePath, "Labels") + Path.DirectorySeparatorChar;

        Directory.CreateDirectory(imagesFolder);
        Directory.CreateDirectory(labelsFolder);

        startIndex = GetHighestImageIndex(imagesFolder);
        if (startIndex > 0) Debug.Log($"Found existing dataset. Resuming numbering from: img_{startIndex + 1:0000}");

        if (renderCamera == null) renderCamera = Camera.main;
        
        renderCamera.allowMSAA = false;
        renderCamera.fieldOfView = fieldOfView; 

        realTexture = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        idTexture = new RenderTexture(imageWidth, imageHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        idTexture.filterMode = FilterMode.Point; 

        screenshot = new Texture2D(imageWidth, imageHeight, TextureFormat.RGB24, false, true);
        idScreenshot = new Texture2D(imageWidth, imageHeight, TextureFormat.RGBA32, false, true);

        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader == null) unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (unlitShader == null) Debug.LogError("CRITICAL: Could not find an Unlit shader for the ID pass!");
        
        unlitColorMaterial = new Material(unlitShader);

        if (datasetConfig != null) classAppearanceCounts = new int[datasetConfig.classes.Count];
    }

    int GetHighestImageIndex(string path)
    {
        if (!Directory.Exists(path)) return 0;
        
        string[] files = Directory.GetFiles(path, "img_*.jpg");
        int maxIndex = 0;
        Regex regex = new Regex(@"img_(\d+)");

        foreach (string file in files)
        {
            Match match = regex.Match(Path.GetFileName(file));
            if (match.Success && int.TryParse(match.Groups[1].Value, out int index))
            {
                if (index > maxIndex) maxIndex = index;
            }
        }
        return maxIndex;
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R) && !isCapturing) captureCoroutine = StartCoroutine(GenerateBalancedDataset());
        if (Input.GetKeyDown(KeyCode.S) && isCapturing)
        {
            isCapturing = false;
            if (captureCoroutine != null) StopCoroutine(captureCoroutine);
            Debug.Log("Dataset generation stopped by user.");
        }
    }

    IEnumerator GenerateBalancedDataset()
    {
        isCapturing = true;
        imagesCaptured = 0; 
        
        Debug.Log("Starting drone perspective dataset generation...");
        
        List<ObjectSpawnLocation> validLocations = spawnLocations.Where(x => x.linkedEnvironmentPlane != null || x.manualPosition != Vector3.zero).ToList();
        
        if (validLocations.Count == 0)
        {
            Debug.LogError("CRITICAL: No valid spawn locations found!");
            isCapturing = false;
            yield break;
        }

        while (imagesCaptured < targetTotalImages)
        {
            if (!isCapturing) yield break; 

            bool isSoloShot = Random.value < 0.20f;
            int objectsToSpawn = isSoloShot ? 1 : Mathf.Min(Random.Range(minObjectsPerImage, maxObjectsPerImage + 1), ID_COLORS.Length);
            
            List<SpawnedInstance> activeInstances = new List<SpawnedInstance>();
            List<Vector3> usedOffsets = new List<Vector3>(); 
            bool layoutSuccessful = false;
            int layoutAttempts = 0;

            while (!layoutSuccessful && layoutAttempts < 10)
            {
                layoutAttempts++;
                CleanupInstances(activeInstances);
                activeInstances.Clear();
                usedOffsets.Clear();

                ObjectSpawnLocation activeEnvironmentLocation = validLocations[Random.Range(0, validLocations.Count)];
                
                // ISOLATION: Turn off ALL other environments
                foreach (var loc in validLocations)
                {
                    if (loc.linkedEnvironmentPlane != null)
                        loc.linkedEnvironmentPlane.gameObject.SetActive(loc == activeEnvironmentLocation);
                }

                Vector3 environmentCenter = activeEnvironmentLocation.GetCenterPosition(); 
                Quaternion environmentRotation = activeEnvironmentLocation.GetRotation();

                List<YOLOClass> selectedClasses = GetClassesForBalancing(objectsToSpawn);

                for (int i = 0; i < selectedClasses.Count; i++)
                {
                    YOLOClass cls = selectedClasses[i];
                    if (cls.prefabs == null || cls.prefabs.Count == 0) continue;

                    GameObject prefab = cls.prefabs[Random.Range(0, cls.prefabs.Count)];
                    
                    Vector3 spawnPosition = Vector3.zero;
                    bool foundValidPosition = false;
                    int positionAttempts = 0;

                    if (isSoloShot)
                    {
                        spawnPosition = environmentCenter;
                        foundValidPosition = CheckGround(spawnPosition, out float groundHeight);
                        if (foundValidPosition) spawnPosition.y = groundHeight;
                    }
                    else
                    {
                        while (!foundValidPosition && positionAttempts < 30)
                        {
                            positionAttempts++;
                            Vector2 randomCircle = Random.insideUnitCircle * spawnSpreadRadius;
                            Vector3 candidateOffset = new Vector3(randomCircle.x, 0, randomCircle.y);
                            Vector3 candidatePos = environmentCenter + candidateOffset;
                            
                            bool tooClose = false;
                            foreach (Vector3 usedOffset in usedOffsets)
                            {
                                if (Vector3.Distance(candidateOffset, usedOffset) < minDistanceBetweenObjects) { tooClose = true; break; }
                            }
                            if (tooClose) continue;

                            if (CheckGround(candidatePos, out float groundH))
                            {
                                candidatePos.y = groundH;
                                spawnPosition = candidatePos;
                                usedOffsets.Add(candidateOffset);
                                foundValidPosition = true;
                            }
                        }
                    }

                    if (!foundValidPosition) continue; 

                    GameObject obj = Instantiate(prefab, spawnPosition, environmentRotation);
                    obj.transform.Rotate(Vector3.up, Random.Range(0f, 360f));
                    
                    Vector3 prefabDefaultScale = obj.transform.localScale;
                    float randomVariance = Random.Range(randomScaleRange.x, randomScaleRange.y);
                    float finalScale = globalObjectScaleMultiplier * cls.modelScale * randomVariance;
                    
                    obj.transform.localScale = prefabDefaultScale * finalScale;
                    
                    Renderer rend = obj.GetComponentInChildren<Renderer>();
                    if (rend != null)
                    {
                        float pivotToBottom = obj.transform.position.y - rend.bounds.min.y;
                        obj.transform.position += Vector3.up * pivotToBottom;
                    }

                    Color32 idColor = ID_COLORS[i];
                    activeInstances.Add(new SpawnedInstance { gameObject = obj, yoloClass = cls, idColor = idColor });
                }

                if (activeInstances.Count == 0) continue; 

                RandomizeEnvironment();
                PositionDroneCamera(environmentCenter, isSoloShot, activeInstances);
                
                yield return new WaitForEndOfFrame();

                layoutSuccessful = CheckVisibilityAndGetBounds(activeInstances, out Dictionary<Color32, Rect> validBounds);

                if (layoutSuccessful)
                {
                    string uuid = System.Guid.NewGuid().ToString("N").Substring(0, 8);
                    int currentNumber = startIndex + imagesCaptured + 1;
                    string imageName = $"img_{currentNumber:0000}_{uuid}";

                    CaptureRealImage(imageName);
                    GenerateLabelsFromBounds(imageName, activeInstances, validBounds);

                    foreach(var inst in activeInstances) classAppearanceCounts[inst.yoloClass.classId]++;
                    imagesCaptured++;
                    
                    if (imagesCaptured % 20 == 0) Debug.Log($"Captured image {currentNumber}");
                }
            }
            
            CleanupInstances(activeInstances);
            yield return null; 
        }

        foreach (var loc in validLocations)
            if (loc.linkedEnvironmentPlane != null) loc.linkedEnvironmentPlane.gameObject.SetActive(true);

        Debug.Log("Dataset generation complete!");
        isCapturing = false;
    }

    bool CheckGround(Vector3 targetPos, out float hitHeight)
    {
        hitHeight = 0;
        Vector3 rayStart = new Vector3(targetPos.x, targetPos.y + 100f, targetPos.z);
        if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 200f))
        {
            hitHeight = hit.point.y;
            return true;
        }
        return false;
    }

    void OnGUI()
    {
        if (!Application.isPlaying) return;

        GUIStyle boxStyle = new GUIStyle(GUI.skin.box) { fontSize = 14, alignment = TextAnchor.UpperCenter, fontStyle = FontStyle.Bold };
        GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        GUIStyle miniStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Normal };

        float progress = targetTotalImages > 0 ? (float)imagesCaptured / targetTotalImages : 0;

        GUI.Box(new Rect(10, 10, 320, 200), "\nYOLO Data Generator (Drone)", boxStyle);
        GUI.color = isCapturing ? Color.green : Color.yellow;
        GUI.Label(new Rect(20, 45, 300, 20), $"Status: {(isCapturing ? "Capturing..." : "Ready")}", labelStyle);
        GUI.color = Color.white;
        
        string countText = startIndex > 0 ? $"Resuming from {startIndex}" : "Starting Fresh";
        GUI.Label(new Rect(20, 65, 300, 20), countText, miniStyle);

        GUI.Label(new Rect(20, 85, 300, 20), $"Session Progress: {imagesCaptured} / {targetTotalImages}", labelStyle);
        DrawProgressBar(new Rect(20, 105, 280, 20), progress);
        GUI.Label(new Rect(20, 125, 300, 20), "Controls:", labelStyle);
        GUI.Label(new Rect(30, 145, 300, 20), "[ R ] - Start Generation");
        GUI.Label(new Rect(30, 165, 300, 20), "[ S ] - Stop Generation");
        if (GUI.Button(new Rect(20, 220, 300, 35), "Open Dataset Folder")) OpenDatasetFolder();
    }

    void DrawProgressBar(Rect rect, float progress)
    {
        Texture2D bgTex = Texture2D.whiteTexture;
        Color oldColor = GUI.color;
        GUI.color = new Color(0.2f, 0.2f, 0.2f);
        GUI.DrawTexture(rect, bgTex);
        GUI.color = new Color(0.2f, 0.8f, 0.2f);
        GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width * progress, rect.height), bgTex);
        GUI.color = oldColor;
        GUIStyle centerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        centerStyle.normal.textColor = Color.white;
        GUI.Label(rect, $"{(progress * 100):F1}%", centerStyle);
    }

    void OpenDatasetFolder()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string datasetFullPath = Path.Combine(projectRoot, datasetPath);
        if (Directory.Exists(datasetFullPath)) System.Diagnostics.Process.Start(datasetFullPath);
    }

    List<YOLOClass> GetClassesForBalancing(int count)
    {
        return datasetConfig.classes.OrderBy(c => classAppearanceCounts[c.classId]).ThenBy(c => Random.value).Take(count).ToList();
    }

    bool CheckVisibilityAndGetBounds(List<SpawnedInstance> instances, out Dictionary<Color32, Rect> boundsDict)
    {
        boundsDict = new Dictionary<Color32, Rect>();
        
        foreach (var inst in instances)
        {
            HideAllExcept(instances, inst);
            ApplyIdMaterial(inst);
            RenderCameraToTexture(idTexture, idScreenshot);
            RestoreMaterials(inst);
            inst.expectedTotalPixels = CountPixelsOfColor(idScreenshot, inst.idColor);
        }

        foreach (var inst in instances)
        {
            inst.gameObject.SetActive(true);
            ApplyIdMaterial(inst);
        }

        RenderCameraToTexture(idTexture, idScreenshot);
        Color32[] allPixels = idScreenshot.GetPixels32();

        Dictionary<Color32, int> visibleCounts = new Dictionary<Color32, int>();
        Dictionary<Color32, Vector4> minMax = new Dictionary<Color32, Vector4>(); 

        for (int y = 0; y < imageHeight; y++)
        {
            for (int x = 0; x < imageWidth; x++)
            {
                Color32 c = allPixels[y * imageWidth + x];
                if (c.r < 10 && c.g < 10 && c.b < 10) continue; 

                foreach (var inst in instances)
                {
                    if (ColorsMatch(c, inst.idColor))
                    {
                        if (!visibleCounts.ContainsKey(inst.idColor))
                        {
                            visibleCounts[inst.idColor] = 0;
                            minMax[inst.idColor] = new Vector4(float.MaxValue, float.MinValue, float.MaxValue, float.MinValue);
                        }

                        visibleCounts[inst.idColor]++;
                        Vector4 mm = minMax[inst.idColor];
                        mm.x = Mathf.Min(mm.x, x);
                        mm.y = Mathf.Max(mm.y, x);
                        mm.z = Mathf.Min(mm.z, y);
                        mm.w = Mathf.Max(mm.w, y);
                        minMax[inst.idColor] = mm;
                        break; 
                    }
                }
            }
        }

        foreach (var inst in instances) RestoreMaterials(inst);

        List<SpawnedInstance> survivors = new List<SpawnedInstance>();

        foreach (var inst in instances)
        {
            int visiblePx = visibleCounts.ContainsKey(inst.idColor) ? visibleCounts[inst.idColor] : 0;
            float visibilityRatio = inst.expectedTotalPixels > 0 ? (float)visiblePx / inst.expectedTotalPixels : 0;

            // FIX: Edge Guard Check
            // If the bounding box touches the pixel borders (0 or max), we consider it cut-off
            Vector4 mm = visibleCounts.ContainsKey(inst.idColor) ? minMax[inst.idColor] : Vector4.zero;
            bool touchesEdge = false;
            
            if (destroyObjectsTouchingEdge)
            {
                // Give a 2-pixel buffer
                if (mm.x <= 2 || mm.y >= imageWidth - 2 || mm.z <= 2 || mm.w >= imageHeight - 2)
                    touchesEdge = true;
            }

            if (visibilityRatio >= minVisibilityThreshold && !touchesEdge)
            {
                boundsDict[inst.idColor] = new Rect(mm.x, mm.z, mm.y - mm.x, mm.w - mm.z);
                survivors.Add(inst);
            }
            else
            {
                inst.gameObject.SetActive(false); 
                Destroy(inst.gameObject); 
            }
        }

        instances.Clear();
        instances.AddRange(survivors);

        return instances.Count > 0; 
    }

    bool ColorsMatch(Color32 c1, Color32 target)
    {
        return Mathf.Abs(c1.r - target.r) < 15 && 
               Mathf.Abs(c1.g - target.g) < 15 && 
               Mathf.Abs(c1.b - target.b) < 15;
    }

    void CaptureRealImage(string imageName)
    {
        RenderCameraToTexture(realTexture, screenshot);
        File.WriteAllBytes(Path.Combine(imagesFolder, imageName + ".jpg"), screenshot.EncodeToJPG(jpgQuality));
    }

    void GenerateLabelsFromBounds(string imageName, List<SpawnedInstance> instances, Dictionary<Color32, Rect> boundsDict)
    {
        string labelContent = "";
        foreach (var inst in instances)
        {
            Rect box = boundsDict[inst.idColor];
            float x_center = (box.x + (box.width / 2f)) / imageWidth;
            float y_center = (box.y + (box.height / 2f)) / imageHeight;
            float width = box.width / imageWidth;
            float height = box.height / imageHeight;
            
            labelContent += $"{inst.yoloClass.classId} {x_center:F6} {1f - y_center:F6} {width:F6} {height:F6}\n";
        }
        File.WriteAllText(Path.Combine(labelsFolder, imageName + ".txt"), labelContent);
    }

    void ApplyIdMaterial(SpawnedInstance inst)
    {
        inst.originalMaterials.Clear();
        
        Material idMat = new Material(unlitColorMaterial);
        if (idMat.HasProperty("_Color")) idMat.SetColor("_Color", inst.idColor);
        if (idMat.HasProperty("_BaseColor")) idMat.SetColor("_BaseColor", inst.idColor);

        foreach (Renderer r in inst.gameObject.GetComponentsInChildren<Renderer>())
        {
            inst.originalMaterials[r] = r.sharedMaterials;
            Material[] idMats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < idMats.Length; i++) idMats[i] = idMat;
            r.sharedMaterials = idMats;
        }
    }

    void RestoreMaterials(SpawnedInstance inst)
    {
        foreach (var kvp in inst.originalMaterials) if (kvp.Key != null) kvp.Key.sharedMaterials = kvp.Value;
    }

    void HideAllExcept(List<SpawnedInstance> all, SpawnedInstance exception)
    {
        foreach (var inst in all) inst.gameObject.SetActive(inst == exception);
    }

    void CleanupInstances(List<SpawnedInstance> instances)
    {
        foreach (var inst in instances) if (inst.gameObject != null) Destroy(inst.gameObject);
    }

    void RenderCameraToTexture(RenderTexture rt, Texture2D tex)
    {
        renderCamera.targetTexture = rt;
        RenderTexture.active = rt;
        
        if (rt == idTexture)
        {
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = Color.black;
        }
        else
        {
            renderCamera.clearFlags = CameraClearFlags.Skybox;
        }

        renderCamera.Render();
        tex.ReadPixels(new Rect(0, 0, imageWidth, imageHeight), 0, 0);
        tex.Apply();
    }

    int CountPixelsOfColor(Texture2D tex, Color32 colorToFind)
    {
        int count = 0;
        foreach (Color32 c in tex.GetPixels32()) if (ColorsMatch(c, colorToFind)) count++;
        return count;
    }

    void PositionDroneCamera(Vector3 environmentCenter, bool isSoloShot, List<SpawnedInstance> instances)
    {
        float altitude;
        float radius;

        if (isSoloShot)
        {
            // FIX: Close-up Variety
            // 1. Calculate rough height of the object to prevent the camera from clipping inside it
            float maxObjHeight = 1f;
            if (instances.Count > 0 && instances[0].gameObject != null)
            {
                Renderer r = instances[0].gameObject.GetComponentInChildren<Renderer>();
                if (r != null) maxObjHeight = r.bounds.size.y;
            }

            // 2. Allow lower altitudes (relative to object size) but widen the radius
            // This creates "Low Angle / Side Profile" shots
            float safeMinAlt = Mathf.Max(droneAltitudeMin, maxObjHeight * 1.5f);
            altitude = Random.Range(safeMinAlt, safeMinAlt + 8f);
            
            // Allow wider radius (up to 20m) for oblique side views
            radius = Random.Range(0f, 20f); 
        }
        else
        {
            float altitudeRange = droneAltitudeMax - droneAltitudeMin;
            float shotCategory = Random.value;
            
            if (shotCategory < 0.6f) 
            {
                altitude = droneAltitudeMin + Random.Range(altitudeRange * 0.2f, altitudeRange * 0.6f);
                radius = Random.Range(5f, droneRadiusMax * 0.7f);
            } 
            else 
            {
                altitude = droneAltitudeMin + Random.Range(altitudeRange * 0.6f, altitudeRange);
                radius = Random.Range(10f, droneRadiusMax);
            }
        }

        float angle = Random.Range(0f, Mathf.PI * 2f); 

        Vector3 dronePos = environmentCenter + new Vector3(
            Mathf.Cos(angle) * radius,
            altitude,
            Mathf.Sin(angle) * radius
        );

        renderCamera.transform.position = dronePos;
        renderCamera.transform.LookAt(environmentCenter);
        
        if (!isSoloShot)
        {
            float pan = Random.Range(-5f, 5f); 
            float tilt = Random.Range(-5f, 5f);
            renderCamera.transform.Rotate(Vector3.up, pan, Space.World);
            renderCamera.transform.Rotate(Vector3.right, tilt, Space.Self);
        }
        
        renderCamera.transform.Rotate(Vector3.forward, Random.Range(-3f, 3f), Space.Self); 
    }

    void RandomizeEnvironment()
    {
        if (skyboxMaterials.Count > 0) RenderSettings.skybox = skyboxMaterials[Random.Range(0, skyboxMaterials.Count)];
        foreach (Light light in sceneLights)
        {
            if (light != null && light.type == LightType.Directional)
            {
                light.intensity = Random.Range(0.5f, 1.5f);
                light.transform.rotation = Quaternion.Euler(Random.Range(30f, 80f), Random.Range(0f, 360f), 0);
            }
        }
    }
}