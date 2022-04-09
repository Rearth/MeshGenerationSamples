using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Assertions;

public struct TriangleStorage :  IDisposable {
    public struct Triangle {
        public readonly ushort V1;
        public readonly ushort V2;
        public readonly ushort V3;
        public readonly float3 CircumCircleCenter;
        public readonly float CircumCircleRadiusSquared;
        public ushort T1, T2, T3;
        public bool IsDeleted;

        public Triangle(TriangleStorage storage, ushort v1, ushort v2, ushort v3) {
            V1 = v1;
            V2 = v2;
            V3 = v3;
            T1 = 0;
            T2 = 0;
            T3 = 0;
            IsDeleted = false;

            Geometry.CircumCircle(storage.F3(v1), storage.F3(v2), storage.F3(v3),
                out CircumCircleCenter, out var circleRadius);
            CircumCircleRadiusSquared = circleRadius * circleRadius;
        }

        public Edge Edge1 => new Edge(V1, V2);
        public Edge Edge2 => new Edge(V2, V3);
        public Edge Edge3 => new Edge(V3, V1);

        public bool ContainsVertex(int v) => V1 == v || V2 == v || V3 == v;
    }

    public struct EdgeRef {
        public readonly ushort TriangleIndex;
        public readonly int EdgeIndex;

        public EdgeRef(ushort triangleIndex, int edgeIndex) {
            TriangleIndex = triangleIndex;
            EdgeIndex = edgeIndex;
        }
    }

    public NativeArray<float2> Points;
    public NativeList<Triangle> Triangles;
    public NativeQueue<ushort> DeletedTriangles1;
    public NativeQueue<ushort> DeletedTriangles2;

    private bool _firstPool;
    public void SwapPool() => _firstPool = !_firstPool;

    public TriangleStorage(Allocator allocator, in int length) {
        
        Points = new NativeArray<float2>(length + 3, allocator);
        Triangles = new NativeList<Triangle>(allocator) {new Triangle {IsDeleted = true}};
        DeletedTriangles1 = new NativeQueue<ushort>(Allocator.Temp);
        DeletedTriangles2 = new NativeQueue<ushort>(Allocator.Temp);
        _firstPool = true;

    }

    public bool IsCreated => Points.IsCreated;

    public void Dispose() {
        if (Points.IsCreated)
            Points.Dispose();
        if (Triangles.IsCreated)
            Triangles.Dispose();
        if (DeletedTriangles1.IsCreated)
            DeletedTriangles1.Dispose();
        if (DeletedTriangles2.IsCreated)
            DeletedTriangles2.Dispose();
    }

    public void AddVertex(int i, float2 position) => Points[i] = position;

    public unsafe ushort AddTriangle(ushort v1, ushort v2, ushort v3) {
        ushort idx;
        if ((_firstPool ? DeletedTriangles1 : DeletedTriangles2).TryDequeue(out idx)) {
            Triangle* triPtr = (Triangle*) Triangles.GetUnsafePtr();
            Triangle* triangle = triPtr + idx;
            var t = new Triangle(this, v1, v2, v3);
            UnsafeUtility.CopyStructureToPtr(ref t, triangle);
        }
        else {
            var t = new Triangle(this, v1, v2, v3);
            Triangles.Add(t);
            idx = (ushort) (Triangles.Length - 1);
        }
        
        return idx;
    }

    public ushort AddTriangle(EdgeRef neighborEdge, ushort vertexIndex) {
        var deletedTriangle = Triangles[(int) neighborEdge.TriangleIndex];
        Assert.IsTrue(neighborEdge.EdgeIndex < 3);

        Edge e = default;
        switch (neighborEdge.EdgeIndex) {
            case 0:
                e = deletedTriangle.Edge1;
                break;
            case 1:
                e = deletedTriangle.Edge2;
                break;
            case 2:
                e = deletedTriangle.Edge3;
                break;
        }

        ushort i = AddTriangle(e.A, e.B, vertexIndex);
        ref Triangle newTriangle = ref Triangles.ElementAt(i);

        switch (neighborEdge.EdgeIndex) {
            case 0:
                newTriangle.T1 = deletedTriangle.T1;
                break;
            case 1:
                newTriangle.T1 = deletedTriangle.T2;
                break;
            case 2:
                newTriangle.T1 = deletedTriangle.T3;
                break;
        }

        if (newTriangle.T1 != 0)
            SetNeighbour(ref Triangles.ElementAt(newTriangle.T1), e.A, e.B, i); // !

        return i;
    }

    public ref Triangle RemoveTriangle(ushort triIndex) {
        ref var triangle = ref Triangles.ElementAt(triIndex);
        triangle.IsDeleted = true;
        Triangles[triIndex] = triangle;
        if (_firstPool)
            DeletedTriangles1.Enqueue(triIndex);
        else
            DeletedTriangles2.Enqueue(triIndex);
        return ref triangle;
    }

    public float3 F3(int i, float y = 0) => Geometry.V3(Points[i], y);

    public float2 F2(ushort pi1) => Points[pi1];

    public void SetNeighbour(ref Triangle t, ushort vertexA, ushort vertexB, ushort newNeighbourIndex) {
        if (t.Edge1.A == vertexB && t.Edge1.B == vertexA)
            t.T1 = newNeighbourIndex;
        else if (t.Edge2.A == vertexB && t.Edge2.B == vertexA)
            t.T2 = newNeighbourIndex;
        else if (t.Edge3.A == vertexB && t.Edge3.B == vertexA)
            t.T3 = newNeighbourIndex;
        else
            Assert.IsTrue(false);
    }
}

[BurstCompile]
public struct NormalDataToPointsJob : IJob {

    [ReadOnly] public NativeList<NormalPassJob.VertexData> data;
    [WriteOnly] public NativeArray<float2> points;
    
    public void Execute() {
        for (var i = 0; i < data.Length; i++) {
            var vertexData = data[i];
            var pos = vertexData.Position;
            points[i] = new float2(pos.xz);
        }
    }
}

public readonly struct Edge : IEquatable<Edge> {
    public readonly ushort A;
    public readonly ushort B;

    public Edge(ushort a, ushort b) {
        A = a;
        B = b;
    }

    public bool Equals(Edge other) => A.Equals(other.A) && B.Equals(other.B) || A.Equals(other.B) && B.Equals(other.A);

    public override bool Equals(object obj) => obj is Edge other && Equals(other);

    public override int GetHashCode() {
        unchecked {
            return (A.GetHashCode() * 397) ^ B.GetHashCode();
        }
    }

    public static bool operator ==(Edge left, Edge right) => left.Equals(right);

    public static bool operator !=(Edge left, Edge right) => !left.Equals(right);

    public override string ToString() => $"{nameof(A)}: {A}, {nameof(B)}: {B}";
}