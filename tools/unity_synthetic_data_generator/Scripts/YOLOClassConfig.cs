using UnityEngine;
using System.Collections.Generic;

[System.Serializable]
public class YOLOClass
{
    public string className;
    public int classId;
    public List<GameObject> prefabs = new List<GameObject>();
    
    [Tooltip("Individual scale for this specific animal. E.g. Set Elephant to 2.0 and Frog to 0.2")]
    public float modelScale = 1.0f; 
}

[CreateAssetMenu(fileName = "YOLODatasetConfig", menuName = "YOLO/Dataset Config")]
public class YOLOClassConfig : ScriptableObject
{
    public List<YOLOClass> classes = new List<YOLOClass>();
}