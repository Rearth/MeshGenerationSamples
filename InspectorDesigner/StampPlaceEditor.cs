#if UNITY_EDITOR
using System;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

public class StampPlaceEditor : EditorWindow {
    private bool placementActive;
    private StampParent stampParent;
    private GameObject activePreview;
    private float yRotation = 0f;
    private float2 lastPosition = 0f;
    
    private PreviewWindowProperties properties;

    private static StampPlaceEditor instance;

    private static readonly Action<SceneView> testAction = view => {
        var e = Event.current;
        var i = instance;

        i?.ProcessEvent(e);
    };

    public static void Init(StampParent parent) {
        var window = (StampPlaceEditor) GetWindow(typeof(StampPlaceEditor));
        window.Show();
        window.stampParent = parent;
        window.properties = CreateInstance<PreviewWindowProperties>();

        SceneView.beforeSceneGui += testAction;
        instance = window;
    }

    private void OnDestroy() {
        SceneView.beforeSceneGui -= testAction;

        if (activePreview) DestroyImmediate(activePreview);
    }

    private void OnGUI() {

        var serializedObject = new SerializedObject(properties);
        var serializedTexture = serializedObject.FindProperty("stamp");
        
        EditorGUI.BeginDisabledGroup(placementActive);

        EditorGUILayout.PropertyField(serializedTexture, false);
        
        if (GUILayout.Button("Add stamp")) {
            placementActive = true;
            CreatePreview();
        }

        EditorGUI.EndDisabledGroup();

        serializedObject.ApplyModifiedProperties();
    }

    private void ProcessEvent(Event e) {
        if (e.type == EventType.MouseDown && e.button == 0 && placementActive) {
            e.Use();
            MouseDownPressed(e);
        }
        else if ((e.type == EventType.MouseMove || e.type == EventType.MouseDrag) && placementActive) {
            MouseMoved(e);
        } else if (e.type == EventType.ScrollWheel && placementActive) {
            e.Use();
            MouseScrolled(e);
        }
    }

    private void MouseScrolled(Event e) {
        var scrollAmount = e.delta;
        activePreview.transform.localScale *= 1 + (scrollAmount.y * 0.05f);
    }

    private void MouseMoved(Event e) {
        
        var mousePos = e.mousePosition;
        var ppp = EditorGUIUtility.pixelsPerPoint;

        mousePos.y = SceneView.lastActiveSceneView.camera.pixelHeight - mousePos.y * ppp;
        mousePos.x *= ppp;

        var ray = SceneView.lastActiveSceneView.camera.ScreenPointToRay(mousePos);

        if (Physics.Raycast(ray, out var hit, Mathf.Infinity)) {
            activePreview.transform.position = hit.point;
            activePreview.transform.up = hit.normal;
            activePreview.transform.Rotate(Vector3.up, yRotation, Space.Self);
        }
    }

    private void MouseDownPressed(Event e) {
        placementActive = false;
        PlaceDownPreview();
    }

    private void CreatePreview() {
        if (activePreview) DestroyImmediate(activePreview);

        var preview = GameObject.Instantiate(stampParent.stampPrefab);
        activePreview = preview;
        activePreview.GetComponent<StampTextureAuthoring>().stamp = properties.stamp;
        activePreview.GetComponent<StampTextureAuthoring>().OnValidate();
        preview.name = properties.stamp.name;
    }

    private void PlaceDownPreview() {
        activePreview.transform.SetParent(stampParent.transform);
        EditorUtility.SetDirty(activePreview.transform);
        Selection.activeObject = activePreview;
        activePreview = null;
        yRotation = 0f;
        lastPosition = 0f;
    }
    
    private class PreviewWindowProperties : ScriptableObject {
        public Texture2D stamp;
    }
}
#endif