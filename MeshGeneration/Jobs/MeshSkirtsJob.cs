using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

[BurstCompile]
public struct MeshSkirtsJob : IJob {
    public TerrainStaticData settings;
    public TerrainUVData uvData;

    [ReadOnly] public NativeHashMap<int2, int> vertexReferences;

    [WriteOnly] public NativeList<int3> triangles;

    public NativeList<VertexPassJob.VertexData> vertices;

    // it would be great to use delegates/function pointers for this, as the four direction actually only differ by 1 line.
    // However, this is not even remotely as performant, so this code duplication is the best way to go currently
    public void Execute() {
        var skirtSize = uvData.UVSize * settings.PlanetRadius * settings.HeightScale / settings.VertexCount / 50f;
        var offset = new float3(0, -skirtSize, 0);
        
        // skirt top
        var firstPoint = new int2(0, 0);

        var lastIndex = vertexReferences[firstPoint];

        var lastPoint = vertices[lastIndex];
        var lastPointDown = lastPoint.Position + offset;

        var initialPointdown = new VertexPassJob.VertexData {
            Position = lastPointDown,
            Normal = lastPoint.Normal
        };

        vertices.Add(initialPointdown);
        var lastDownIndex = vertices.Length - 1;

        for (int x = 1; x < settings.VertexCount; x++) {
            var point = new int2(x, 0);
            if (!vertexReferences.ContainsKey(point)) continue;

            var newPointIndex = vertexReferences[point];
            var newPoint = vertices[newPointIndex];
            var newPointDown = newPoint.Position + offset;

            var additionalPoint = new VertexPassJob.VertexData {
                Position = newPointDown,
                Normal = newPoint.Normal
            };

            vertices.Add(additionalPoint);
            var newDownIndex = vertices.Length - 1;

            var newTriA = new int3(lastIndex, newPointIndex, newDownIndex);
            var newTriB = new int3(lastIndex, newDownIndex, lastDownIndex);

            triangles.Add(newTriA);
            triangles.Add(newTriB);

            lastIndex = newPointIndex;
            lastDownIndex = newDownIndex;
        }
        
        // skirt left
        firstPoint = new int2(0, settings.VertexCount - 1);

        lastIndex = vertexReferences[firstPoint];

        lastPoint = vertices[lastIndex];
        lastPointDown = lastPoint.Position + offset;

        initialPointdown = new VertexPassJob.VertexData {
            Position = lastPointDown,
            Normal = lastPoint.Normal
        };

        vertices.Add(initialPointdown);
        lastDownIndex = vertices.Length - 1;

        for (int x = 1; x < settings.VertexCount; x++) {
            var point = new int2(0, settings.VertexCount - 1 - x);
            if (!vertexReferences.ContainsKey(point)) continue;

            var newPointIndex = vertexReferences[point];
            var newPoint = vertices[newPointIndex];
            var newPointDown = newPoint.Position + offset;

            var additionalPoint = new VertexPassJob.VertexData {
                Position = newPointDown,
                Normal = newPoint.Normal
            };

            vertices.Add(additionalPoint);
            var newDownIndex = vertices.Length - 1;

            var newTriA = new int3(lastIndex, newPointIndex, newDownIndex);
            var newTriB = new int3(lastIndex, newDownIndex, lastDownIndex);

            triangles.Add(newTriA);
            triangles.Add(newTriB);

            lastIndex = newPointIndex;
            lastDownIndex = newDownIndex;
        }
        
        // skirt bot
        firstPoint = new int2(settings.VertexCount - 1, settings.VertexCount - 1);

        lastIndex = vertexReferences[firstPoint];

        lastPoint = vertices[lastIndex];
        lastPointDown = lastPoint.Position + offset;

        initialPointdown = new VertexPassJob.VertexData {
            Position = lastPointDown,
            Normal = lastPoint.Normal
        };

        vertices.Add(initialPointdown);
        lastDownIndex = vertices.Length - 1;

        for (int x = 1; x < settings.VertexCount; x++) {
            var point = new int2(settings.VertexCount - 1 - x, settings.VertexCount - 1);
            if (!vertexReferences.ContainsKey(point)) continue;

            var newPointIndex = vertexReferences[point];
            var newPoint = vertices[newPointIndex];
            var newPointDown = newPoint.Position + offset;

            var additionalPoint = new VertexPassJob.VertexData {
                Position = newPointDown,
                Normal = newPoint.Normal
            };

            vertices.Add(additionalPoint);
            var newDownIndex = vertices.Length - 1;

            var newTriA = new int3(lastIndex, newPointIndex, newDownIndex);
            var newTriB = new int3(lastIndex, newDownIndex, lastDownIndex);

            triangles.Add(newTriA);
            triangles.Add(newTriB);

            lastIndex = newPointIndex;
            lastDownIndex = newDownIndex;
        }
        
        // skirt right
        firstPoint = new int2(settings.VertexCount - 1, 0);

        lastIndex = vertexReferences[firstPoint];

        lastPoint = vertices[lastIndex];
        lastPointDown = lastPoint.Position + offset;

        initialPointdown = new VertexPassJob.VertexData {
            Position = lastPointDown,
            Normal = lastPoint.Normal
        };

        vertices.Add(initialPointdown);
        lastDownIndex = vertices.Length - 1;

        for (int x = 1; x < settings.VertexCount; x++) {
            var point = new int2(settings.VertexCount - 1, x);
            if (!vertexReferences.ContainsKey(point)) continue;

            var newPointIndex = vertexReferences[point];
            var newPoint = vertices[newPointIndex];
            var newPointDown = newPoint.Position + offset;

            var additionalPoint = new VertexPassJob.VertexData {
                Position = newPointDown,
                Normal = newPoint.Normal
            };

            vertices.Add(additionalPoint);
            var newDownIndex = vertices.Length - 1;

            var newTriA = new int3(lastIndex, newPointIndex, newDownIndex);
            var newTriB = new int3(lastIndex, newDownIndex, lastDownIndex);

            triangles.Add(newTriA);
            triangles.Add(newTriB);

            lastIndex = newPointIndex;
            lastDownIndex = newDownIndex;
        }
    }
}