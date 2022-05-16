using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

public class AlignWithPlanetHelper : MonoBehaviour {

    public Vector3 planetCenter;
    public float planetRadius;

    public bool reload;

    private void OnValidate() {
        if (reload) {
            reload = false;
            var dir = (planetCenter - transform.position).normalized;
            var pos = planetCenter - dir * planetRadius * 1.00001f;
            transform.up = -dir;
            transform.position = pos;
        }
    }
}
