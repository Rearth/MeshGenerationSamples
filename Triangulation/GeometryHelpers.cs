using System;
using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;


public static class Geometry {
    
    public static float3 V3(float2 v, float y = 0) => new float3(v.x, y, v.y);

    public static void InCenter(float2 p1, float2 p2, float2 p3, out float2 inCenter, out float inRadius) {
        var a = math.distance(p1, p2);
        var b = math.distance(p2, p3);
        var c = math.distance(p3, p1);

        var perimeter = (a + b + c);
        var x = (a * p1.x + b * p2.x + c * p3.x) / perimeter;
        var y = (a * p1.y + b * p2.y + c * p3.y) / perimeter;
        inCenter = new float2(x, y);

        var s = perimeter / 2;
        var triangleArea = math.sqrt(s * (s - a) * (s - b) * (s - c));
        inRadius = triangleArea / s;
    }
    
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void CircumCircle(float3 a, float3 b, float3 c, out float3 circleCenter, out float circleRadius) {
        PerpendicularBisector(a, b - a, out var perpAbPos, out var perpAbDir);
        PerpendicularBisector(a, c - a, out var perpAcPos, out var perpAcdir);
        var tAb = Intersection(perpAbPos, perpAbPos + perpAbDir, perpAcPos, perpAcPos + perpAcdir);
        circleCenter = perpAbPos + perpAbDir * tAb;

        circleRadius = math.length(circleCenter - a);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void PerpendicularBisector(float3 pos, float3 dir, out float3 bisectorPos,
        out float3 bisectorDir) {
        var m = dir * .5f;
        var cross = math.normalize(math.cross(math.normalize(dir), new float3(0, 1, 0)));
        bisectorPos = pos + m;
        bisectorDir = cross;
    }

    // http://paulbourke.net/geometry/pointlineplane/
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Intersection(float3 p1, float3 p2, float3 p3, float3 p4) {
        var tAb = ((p4.x - p3.x) * (p1.z - p3.z) - (p4.z - p3.z) * (p1.x - p3.x)) /
                  ((p4.z - p3.z) * (p2.x - p1.x) - (p4.x - p3.x) * (p2.z - p1.z));
        return tAb;
    }
}