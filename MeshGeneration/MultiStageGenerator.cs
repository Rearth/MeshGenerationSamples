using System.Collections.Generic;
using System.Diagnostics;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

public class MultiStageGenerator : MonoBehaviour {
    public bool reload;
    public bool debugDraw;
    public GridSettings settings;
    public ProceduralGenerationSettings proceduralGenerationSettings;

    private void OnValidate() {
        Generate();
        if (reload) {
            reload = false;
        }
    }

    private List<(float3 a, float3 b, float3 c)> CachedTriangles = new List<(float3 a, float3 b, float3 c)>();

    private void OnDrawGizmosSelected() {
        
        if (!debugDraw) return;
        
        foreach (var elem in CachedTriangles) {
            var posA = elem.a + (float3) transform.position;
            var posB = elem.b + (float3) transform.position;
            var posC = elem.c + (float3) transform.position;
            Gizmos.DrawLine(posA, posB);
            Gizmos.DrawLine(posB, posC);
            Gizmos.DrawLine(posC, posA);
        }
    }

    private void Generate() {
        var pointCountInitial = settings.Count * settings.Count;
        var triangleCountHalf = (settings.Count - 1) * (settings.Count - 1);

        if (settings.Count % settings.CoreGridSpacing != 1) {
            Debug.Log("invalid core grid spacing");
            return;
        }

        var heights = new NativeArray<half>(pointCountInitial, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
        var vertices = new NativeList<NormalPassJob.VertexData>(pointCountInitial, Allocator.TempJob);
        var triangles = new NativeList<int3>(triangleCountHalf * 2, Allocator.TempJob);
        var pointToVertexRefs = new NativeHashMap<int2, int>(pointCountInitial, Allocator.TempJob);

        try {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var heightJob = new HeightSampleJob {
                settings = settings,
                heights = heights,
                generationSettings = proceduralGenerationSettings
            };

            var heightHandle = heightJob.Schedule(pointCountInitial, settings.Count);

            var normalsJob = new NormalPassJob {
                settings = settings,
                heights = heights,
                vertices = vertices.AsParallelWriter(),
                pointToVertexReferences = pointToVertexRefs.AsParallelWriter()
            };

            var normalsHandle = normalsJob.Schedule(pointCountInitial, settings.Count, heightHandle);

            var patchCountPerLine = (settings.Count - 1) / settings.CoreGridSpacing;
            var patchCount = patchCountPerLine * patchCountPerLine;

            var meshData = Mesh.AllocateWritableMeshData(1);
            var mainMesh = meshData[0];

            var triangulationHandle = new PatchTriangulationJob {
                settings = settings,
                vertexReferences = pointToVertexRefs,
                triangles = triangles.AsParallelWriter(),
                EdgeComputeMarker = new ProfilerMarker("EdgeComputeMarker"),
                InvalidSearchMarker = new ProfilerMarker("InvalidSearchMarker"),
                VertexGatherMarker = new ProfilerMarker("VertexGatherMarker"),
                TriangulationMarker = new ProfilerMarker("TriangulationMarker"),
                HoleTriangulationMarker = new ProfilerMarker("HoleTriangulationMarker")
            }.Schedule(patchCount, 1, normalsHandle);

            var meshingHandle = new MeshPreparationJob {
                meshData = mainMesh,
                triangles = triangles.AsDeferredJobArray(),
                vertices = vertices.AsDeferredJobArray()
            }.Schedule(triangulationHandle);
            
            meshingHandle.Complete();
            
            var indicesCount = triangles.Length * 3;
            mainMesh.subMeshCount = 1;
            mainMesh.SetSubMesh(0, new SubMeshDescriptor(0, indicesCount), NoFlags());
            var mesh = new Mesh {name = this.transform.name};
            var meshSize = settings.Count * settings.Distance;
            mesh.bounds = new Bounds(new Vector3(meshSize / 2, settings.HeightScale / 2, meshSize / 2), new Vector3(meshSize, settings.HeightScale, meshSize));
            Mesh.ApplyAndDisposeWritableMeshData(meshData, mesh, NoFlags());

            gameObject.GetComponent<MeshFilter>().mesh = mesh;
            
            stopWatch.Stop();
            Debug.Log("Generation took: " + stopWatch.ElapsedMilliseconds + "ms or ticks: " + stopWatch.ElapsedTicks);
            
            if (debugDraw) {
                CachedTriangles.Clear();
                foreach (var triangle in triangles) {
                    CachedTriangles.Add((vertices[triangle.x].Position, vertices[triangle.y].Position, vertices[triangle.z].Position));
                }
            }
        }
        finally {
            heights.Dispose();
            vertices.Dispose();
            triangles.Dispose();
            pointToVertexRefs.Dispose();
        }
    }

    private static MeshUpdateFlags NoFlags() {
        return MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;
    }
}