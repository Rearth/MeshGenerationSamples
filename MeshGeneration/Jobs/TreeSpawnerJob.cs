using System;
using System.Runtime.CompilerServices;
using Orbis;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

// ReSharper disable ForCanBeConvertedToForeach

[BurstCompile]
public struct TreeSpawnerJob : IJob {
    [WriteOnly] public NativeList<float3x2> positions;

    [ReadOnly] public NativeArray<TerrainStampData> stamps;
    [ReadOnly] public NativeArray<ushort> heightmapLinear;
    [ReadOnly] public NativeHashMap<ushort, uint> beginIndices;
    [ReadOnly] public float4x4 terrainLTW;

    public TerrainStaticData settings;
    public TerrainTreeSpawner.SpawnConfiguration spawnConfig;

    public void Execute() {
        var random = new Random((uint) terrainLTW.GetHashCode());
        var terrainPos = new LocalToWorld {Value = terrainLTW}.Position;
        var usedLTW = float4x4.TRS(terrainPos, quaternion.identity, 1f);

        for (int i = 0; i < spawnConfig.Count; i++) {
            var direction = random.NextFloat3Direction();
            var right = math.normalize(getPerpendicularVector(direction));
            var forward = math.cross(direction, right);

            var offset = 1f / settings.PlanetRadius;
            var center = direction;
            var dirA = center + right * offset;
            var dirB = center + forward * offset;

            var sampleCenter = HeightSampleJob.SampleFromStamps(center, in settings, in stamps, in heightmapLinear, in beginIndices, in usedLTW);
            var sampleA = HeightSampleJob.SampleFromStamps(dirA, in settings, in stamps, in heightmapLinear, in beginIndices, in usedLTW);
            var sampleB = HeightSampleJob.SampleFromStamps(dirB, in settings, in stamps, in heightmapLinear, in beginIndices, in usedLTW);

            var centerP = new float3(0, sampleCenter.Height, 0);
            var normA = new float3(1, sampleA.Height, 0);
            var normB = new float3(0, sampleB.Height, 1);
            var normalDir = math.cross(normA - centerP, normB - centerP);
            var angle = Vector3.Angle(normalDir, -Vector3.up);

            var validPoint = EvaluatePosition(spawnConfig, sampleCenter, math.degrees(angle));
            if (!validPoint) continue;

            var targetPoint = terrainPos + direction * (settings.PlanetRadius + sampleCenter.Height * settings.HeightScale);
            positions.Add(new float3x2(targetPoint, direction));
            Debug.DrawLine(targetPoint, targetPoint + direction * 5, Color.red, 5f, true);
        }
    }

    public static float3 getPerpendicularVector(float3 direction) {
        return new float3(1, 1, -(direction.x + direction.y) / direction.z);
    }

    private static bool EvaluatePosition(in TerrainTreeSpawner.SpawnConfiguration config, in HeightSample sample, in float angle) {

        if (sample.Height < config.MinMaxHeight.x || sample.Height > config.MinMaxHeight.y) return false;
        if (sample.BiomeData.x < config.MinMaxTemperature.x || sample.BiomeData.x > config.MinMaxTemperature.y) return false;
        if (sample.BiomeData.y < config.MinMaxHumidity.x || sample.BiomeData.y > config.MinMaxHumidity.y) return false;
        if (angle < config.MinMaxAngles.x || angle > config.MinMaxAngles.y) return false;

        return true;
    }
}