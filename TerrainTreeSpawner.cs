using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Scenes;
using Unity.Transforms;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Object = System.Object;

[ExecuteInEditMode]
public class TerrainTreeSpawner : MonoBehaviour {
    public SpawnConfiguration config;
    public GameObject tree;
    public bool spawn;
    public bool clear;

    private void OnValidate() {
        var subScene = SceneManager.GetSceneByName("VegetationScene");
        var treeParent = subScene.GetRootGameObjects()[0].transform;
        if (!treeParent) return;

        if (clear || spawn) {
            clear = false;
            foreach (Transform child in treeParent) {
                UnityEditor.EditorApplication.delayCall += () => { GameObject.DestroyImmediate(child.gameObject); };
            }
            EditorSceneManager.MarkSceneDirty(subScene);
        }

        if (!spawn) return;

        spawn = false;
        print("spawning " + config + " trees");

        var world = World.DefaultGameObjectInjectionWorld;
        var em = world.EntityManager;

        var terrainQuery = em.CreateEntityQuery(ComponentType.ReadOnly<LocalToWorld>(), typeof(TerrainStaticData), typeof(TerrainLODLevel));
        var entities = terrainQuery.ToEntityArray(Allocator.Temp);
        var ltws = terrainQuery.ToComponentDataArray<LocalToWorld>(Allocator.Temp);
        var datas = terrainQuery.ToComponentDataArray<TerrainStaticData>(Allocator.Temp);
        var lods = terrainQuery.ToComponentDataArray<TerrainLODLevel>(Allocator.Temp);

        var schedulerSystem = world.GetExistingSystem<OrbisSchedulerSystem>();
        if (schedulerSystem == null) {
            print("unable to find system");
            return;
        }

        var timer = new Stopwatch();
        timer.Start();

        var sampledPoints = new NativeList<float3x2>(config.Count / 4, Allocator.TempJob);

        var handle = new JobHandle();

        for (var i = 0; i < entities.Length; i++) {
            var entity = entities[i];
            var ltw = ltws[i];
            var data = datas[i];
            var lod = lods[i].Value;

            if (lod != 0) continue;

            var job = new TreeSpawnerJob() {
                spawnConfig = config,
                beginIndices = schedulerSystem.beginIndices,
                heightmapLinear = schedulerSystem.heightsLinear,
                stamps = schedulerSystem.stampDatas,
                settings = data,
                terrainLTW = ltw.Value,
                positions = sampledPoints
            };
            handle = job.Schedule(handle);
        }

        handle.Complete();

        timer.Stop();
        print(timer.ElapsedMilliseconds + "ms for " + sampledPoints.Length + " valid points");

        foreach (var sampledPoint in sampledPoints) {
            var right = TreeSpawnerJob.getPerpendicularVector(sampledPoint.c1);
            var orientation = quaternion.LookRotation(right, sampledPoint.c1);
            UnityEditor.EditorApplication.delayCall += () => {
                var newObj = GameObject.Instantiate(tree, sampledPoint.c0, orientation, treeParent);
            };
        }

        entities.Dispose();
        ltws.Dispose();
        datas.Dispose();
        lods.Dispose();
        sampledPoints.Dispose();

        EditorSceneManager.MarkSceneDirty(subScene);
    }

    [Serializable]
    public struct SpawnConfiguration {
        public int Count;
        public float2 MinMaxAngles;
        public float2 MinMaxHeight;
        public float2 MinMaxHumidity;
        public float2 MinMaxTemperature;
    }
}