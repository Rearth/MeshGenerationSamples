using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

public class ReloadTrigger : EditorWindow {

    [MenuItem("Tools/Orbis/Enable")]
    public static void Enable() {
        SceneView.duringSceneGui += OnScene;
    }

    [MenuItem("Tools/Orbis/Disable")]
    public static void Disable() {
        SceneView.duringSceneGui -= OnScene;
    }

    private static void OnScene(SceneView sceneview) {
        Handles.BeginGUI();
        if (GUILayout.Button("Orbis Reload", GUILayout.Height(20), GUILayout.Width(82))) {
            Debug.Log("Reloading Orbis Live preview");
            DoReload();
        }

        Handles.EndGUI();
    }

    private static void DoReload() {
        CreateStack();
        AssetDatabase.Refresh();

        var targetWorld = World.DefaultGameObjectInjectionWorld;
        var em = targetWorld.EntityManager;
        var schedulerSystem = targetWorld.GetExistingSystem<OrbisSchedulerSystem>();

        schedulerSystem.ResetData();

        foreach (var mesh in schedulerSystem.renderMeshes) {
            if (!em.Exists(mesh.Key)) continue;
            var lodLevel = em.GetComponentData<TerrainLODLevel>(mesh.Key).Value;
            if (lodLevel != 0) {
                em.AddComponent<DestroyTerrainTag>(mesh.Key);
            }
            else {
                em.RemoveComponent<QuadtreeChildren>(mesh.Key);
                em.AddComponent<RenderTerrainTag>(mesh.Key);
            }
        }
    }


#if UNITY_EDITOR
    public static void CreateStack() {
        var parent = GameObject.Find("Stamps");

        if (!parent || parent.transform.childCount == 0) {
            Debug.Log("unable to find stamp parent");
            return;
        }

        var maps = new Dictionary<ushort, NativeArray<ushort>>();
        var totalCount = 0;

        foreach (Transform stamp in parent.transform) {
            if (!stamp.TryGetComponent<StampTextureAuthoring>(out var stampData)) continue;

            var tex = stampData.stamp;
            var texID = (ushort) ((float) ushort.MaxValue / int.MaxValue * tex.name.GetHashCode());
            if (maps.ContainsKey(texID)) continue;

            var native = tex.GetRawTextureData<ushort>();
            maps.Add(texID, native);
            totalCount += native.Length;
        }

        var beginIndices = new Dictionary<ushort, uint>();
        var heightsLinear = new NativeArray<ushort>(totalCount, Allocator.Temp);

        var currentStart = 0;
        foreach (var map in maps) {
            var activeSlice = heightsLinear.Slice(currentStart, map.Value.Length);
            activeSlice.CopyFrom(map.Value);

            beginIndices.Add(map.Key, (uint) currentStart);

            currentStart += map.Value.Length;
        }

        var data = new OrbisData {
            indices = beginIndices,
            heights = heightsLinear.ToArray()
        };

        var bf = new BinaryFormatter();
        var orbisFile = File.Create(Application.dataPath + "/Orbis/Resources/OrbisData/main.bytes");

        bf.Serialize(orbisFile, data);
        orbisFile.Close();

        Debug.Log("successfully saved orbis data");
    }

#endif
    [Serializable]
    public class OrbisData {
        public Dictionary<ushort, uint> indices;
        public ushort[] heights;
    }
}