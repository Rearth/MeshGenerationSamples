using Orbis;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = Unity.Mathematics.Random;

[BurstCompile(FloatPrecision.Low, FloatMode.Fast)]
public struct VegetationSpawningJob : IJob {
    [WriteOnly] public NativeList<Translation> positions;
    [WriteOnly] public NativeList<Rotation> rotations;

    [ReadOnly] public NativeArray<TerrainStampData> stamps;
    [ReadOnly] public NativeArray<ushort> heightmapLinear;
    [ReadOnly] public NativeHashMap<ushort, uint> beginIndices;
    [ReadOnly] public float4x4 terrainInverse;
    [ReadOnly] public float4x4 terrainLTW;

    public TerrainStaticData settings;
    public TerrainUVData uvData;
    public TerrainFoliageSetting spawnConfig;

    public void Execute() {
        var count = spawnConfig.Count;
        var minDistance = uvData.UVSize / count;
        var random = new Random(settings.VertexCount);
        var points = new NativeList<float2>(Allocator.Temp);

        FastPoisonDiskSampling.sample(uvData.UVStart, uvData.UVStart + uvData.UVSize, minDistance, random, points, 5);

        for (var i = 0; i < points.Length; i++) {
            var candidate = points[i];
            var localFlatPosition = new float3(candidate.x * 2f - 1f, 1f, candidate.y * 2f - 1f);
            var sphereCenter = new float3(0, 0, 0);
            var sphereRadius = settings.PlanetRadius;

            var pointOnSphere = Geometry.TransformPointFlatToSphere(in localFlatPosition, in sphereCenter, sphereRadius);
            var direction = math.normalize(pointOnSphere);
            var heightSample = HeightSampleJob.SampleFromStamps(direction, in settings, in stamps, in heightmapLinear, in beginIndices, in terrainInverse, in terrainLTW);
            var localPosition = Geometry.TransformPointFlatToSphere(in localFlatPosition, in sphereCenter, sphereRadius + heightSample.Height * settings.HeightScale);

            if (!EvaluatePositionInitial(in spawnConfig, in heightSample, in settings)) continue;
            // height & biome seems correct, calculate angles for cliff check
            var right = math.normalize(GetPerpendicularVector(direction));
            var forward = math.cross(direction, right);

            var offset = 1f / settings.PlanetRadius;
            var center = direction;
            var dirA = center + right * offset;
            var dirB = center + forward * offset;
            var heightSampleA = HeightSampleJob.SampleFromStamps(dirA, in settings, in stamps, in heightmapLinear, in beginIndices, in terrainInverse, in terrainLTW);
            var heightSampleB = HeightSampleJob.SampleFromStamps(dirB, in settings, in stamps, in heightmapLinear, in beginIndices, in terrainInverse, in terrainLTW);

            var centerP = new float3(0, heightSample.Height, 0);
            var normA = new float3(1, heightSampleA.Height, 0);
            var normB = new float3(0, heightSampleB.Height, 1);
            var normalDir = math.cross(normA - centerP, normB - centerP);
            var angle = math.degrees(Vector3.Angle(normalDir, -Vector3.up));
            if (angle < spawnConfig.MinMaxAngles.x || angle > spawnConfig.MinMaxAngles.y) continue;

            var worldPoint = Geometry.TransformPosition(in localPosition, in terrainLTW);
            var worldDirection = math.rotate(terrainLTW, direction);
            var worldForward = GetPerpendicularVector(worldDirection);
            var rotation = quaternion.LookRotation(worldForward, worldDirection);

            positions.Add(new Translation {Value = worldPoint});
            rotations.Add(new Rotation {Value = rotation});
        }
    }

    private static float3 GetPerpendicularVector(float3 direction) {
        return new float3(1, 1, -(direction.x + direction.y) / direction.z);
    }

    // ignoring angles
    private static bool EvaluatePositionInitial(in TerrainFoliageSetting config, in HeightSample sample, in TerrainStaticData settings) {
        if (sample.Height * settings.HeightScale < config.MinMaxHeight.x || sample.Height * settings.HeightScale > config.MinMaxHeight.y) return false;
        if (sample.BiomeData.x < config.MinMaxTemperature.x || sample.BiomeData.x > config.MinMaxTemperature.y) return false;
        if (sample.BiomeData.y < config.MinMaxHumidity.x || sample.BiomeData.y > config.MinMaxHumidity.y) return false;

        return true;
    }
}