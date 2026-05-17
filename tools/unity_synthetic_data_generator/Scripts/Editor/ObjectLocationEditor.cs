using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

public class ObjectLocationEditor : EditorWindow
{
    private YOLODataGenerator generator;
    private Vector2 scrollPos;

    [MenuItem("Tools/YOLO/Object Location Editor")]
    public static void ShowWindow()
    {
        GetWindow<ObjectLocationEditor>("Object Location Editor");
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Object Location Editor", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        generator = (YOLODataGenerator)EditorGUILayout.ObjectField(
            "Data Generator",
            generator,
            typeof(YOLODataGenerator),
            true
        );

        if (generator == null)
        {
            EditorGUILayout.HelpBox("Assign a YOLO Data Generator component", MessageType.Info);
            return;
        }

        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField($"Spawn Locations: {generator.spawnLocations.Count}", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        for (int i = 0; i < generator.spawnLocations.Count; i++)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            ObjectSpawnLocation location = generator.spawnLocations[i];

            EditorGUILayout.BeginHorizontal();
            location.locationName = EditorGUILayout.TextField("Name", location.locationName);

            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                generator.spawnLocations.RemoveAt(i);
                EditorUtility.SetDirty(generator);
                break;
            }
            EditorGUILayout.EndHorizontal();

            // Drag and Drop Slot for Environment
            location.linkedEnvironmentPlane = (Transform)EditorGUILayout.ObjectField(
                "Linked Environment", 
                location.linkedEnvironmentPlane, 
                typeof(Transform), 
                true
            );

            // Only show manual position if no environment is linked
            if (location.linkedEnvironmentPlane == null)
            {
                location.manualPosition = EditorGUILayout.Vector3Field("Manual Position", location.manualPosition);
                EditorGUILayout.HelpBox("Link an Environment GameObject above for auto-centering.", MessageType.Warning);
            }
            else
            {
                EditorGUILayout.LabelField("Using center of: " + location.linkedEnvironmentPlane.name, EditorStyles.miniLabel);
            }

            location.rotation = EditorGUILayout.Vector3Field("Base Rotation", location.rotation);
            // REMOVED: Scale fields are gone now

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space();
        }

        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space();

        if (GUILayout.Button("Add New Location", GUILayout.Height(30)))
        {
            generator.spawnLocations.Add(new ObjectSpawnLocation { locationName = "New Location" });
            EditorUtility.SetDirty(generator);
        }

        if (GUILayout.Button("Clear All Locations", GUILayout.Height(30)))
        {
            if (EditorUtility.DisplayDialog("Clear Locations",
                "Are you sure you want to clear all locations?", "Yes", "No"))
            {
                generator.spawnLocations.Clear();
                EditorUtility.SetDirty(generator);
            }
        }
    }
}