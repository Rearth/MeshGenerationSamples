using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Random = Unity.Mathematics.Random;

// based on https://www.cs.ubc.ca/~rbridson/docs/bridson-siggraph07-poissondisk.pdf
// and a3geek's implementation at: https://gist.github.com/a3geek/8532817159b77c727040cf67c92af322

namespace Orbis {
    [BurstCompile]
    public static class FastPoisonDiskSampling {
        public const int DefaultIterationPerPoint = 30;

        private struct settings {
            public float2 bottomLeft;
            public float2 topRight;

            public float minDistance;
            public int iterationsPerPoint;

            public float cellSize;
            public int2 gridSize;
        }

        private struct containers {
            public NativeArray<float2> grid;
            public NativeList<float2> samplePoints;
            public NativeList<float2> activePoints;
        }

        [BurstCompile]
        public struct SamplingJob : IJob {
            public NativeList<float2> output;
            public Random random;

            [ReadOnly] public float2 bottomLeft;
            [ReadOnly] public float2 topRight;
            [ReadOnly] public float minDistance;
            [ReadOnly] public int iterationsPerPoint;

            public void Execute() {
                sample(bottomLeft, topRight, minDistance, random, output, iterationsPerPoint);
            }
        }

        public static void sample(float2 bottomLeft, float2 topRight, float minDistance, Random random, NativeList<float2> results, int iterationsPerPoint = DefaultIterationPerPoint) {
            var settings = getSettings(bottomLeft, topRight, minDistance, iterationsPerPoint);

            var gridTotalCount = (settings.gridSize.x + 1) * (settings.gridSize.y + 1);

            var bags = new containers() {
                grid = new NativeArray<float2>(gridTotalCount, Allocator.Temp),
                samplePoints = results,
                activePoints = new NativeList<float2>(10, Allocator.Temp)
            };

            setFirstPoint(ref settings, ref bags, ref random);

            do {
                var index = random.NextInt(0, bags.activePoints.Length);
                var point = bags.activePoints[index];

                var found = false;
                for (int i = 0; i < settings.iterationsPerPoint; i++) {
                    found |= GetNextPoint(in point, ref settings, ref bags, ref random);
                }

                if (found == false) {
                    bags.activePoints.RemoveAt(index);
                }
            } while (bags.activePoints.Length > 0);

            bags.grid.Dispose();
            bags.activePoints.Dispose();
        }

        private static bool GetNextPoint(in float2 point, ref settings settings, ref containers bags, ref Random random) {
            var found = false;
            var p = getRandomPosInCircle(settings.minDistance, 2f * settings.minDistance, ref random) + point;

            //check if p is out of bounds
            if (math.any(p < settings.bottomLeft) || math.any(p > settings.topRight)) {
                return false;
            }

            var min = settings.minDistance * settings.minDistance;
            var index = getGridIndex(p, ref settings);
            var drop = false;

            var around = 2;
            var fieldMin = new int2(math.max(0, index - around));
            var fieldMax = new int2(math.min(settings.gridSize, index + around));

            for (int i = fieldMin.x; i <= fieldMax.x && drop == false; i++) {
                for (int j = fieldMin.y; j < fieldMax.y && drop == false; j++) {
                    var q = bags.grid[getLinearIndex(new int2(i, j), ref settings)];
                    if (math.any(q != float2.zero) && math.lengthsq(q - p) <= min)
                        drop = true;
                }
            }

            if (drop == false) {
                found = true;

                bags.samplePoints.Add(p);
                bags.activePoints.Add(p);
                bags.grid[getLinearIndex(index, ref settings)] = p;
            }

            return found;
        }

        //sets a first point randomly in the area
        private static void setFirstPoint(ref settings settings, ref containers containers, ref Random random) {
            var first = new float2(random.NextFloat2(settings.bottomLeft, settings.topRight));
            var index = getGridIndex(first, ref settings);
            containers.grid[getLinearIndex(index, ref settings)] = first;
            containers.samplePoints.Add(first);
            containers.activePoints.Add(first);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int getLinearIndex(in int2 index, ref settings settings) {
            return index.x + index.y * settings.gridSize.x;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int2 getGridIndex(in float2 point, ref settings settings) {
            return (int2) math.floor((point - settings.bottomLeft) / settings.cellSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static settings getSettings(float2 bl, float2 tr, float min, int iterations) {
            return new settings() {
                bottomLeft = bl,
                topRight = tr,
                minDistance = min,
                iterationsPerPoint = iterations,
                cellSize = min / math.SQRT2,
                gridSize = (int2) math.ceil((tr - bl) / (min / math.SQRT2))
            };
        }


        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 getRandomPosInCircle(float fieldMin, float fieldMax, ref Random random) {
            var theta = random.NextFloat(0f, math.PI * 2f);
            var radius = math.sqrt(random.NextFloat(fieldMin * fieldMin, fieldMax * fieldMax));

            return new float2(radius * math.cos(theta), radius * math.sin(theta));
        }
    }
}