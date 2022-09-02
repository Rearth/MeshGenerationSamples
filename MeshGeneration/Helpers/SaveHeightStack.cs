using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Formatters.Binary;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;

public class SaveHeightStack : MonoBehaviour {
#if UNITY_EDITOR
    [MenuItem("Tools/ReloadOrbis")]
    static void CreateStack() {
        var parent = GameObject.Find("Stamps");

        if (!parent || parent.transform.childCount == 0) {
            print("unable to find stamp parent");
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