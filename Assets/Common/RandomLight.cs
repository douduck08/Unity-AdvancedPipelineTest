using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class RandomLight : MonoBehaviour {

    [SerializeField] Light prefab;
    [SerializeField] int number;
    [SerializeField] float range;

    [SerializeField] List<Light> lights = new List<Light> ();

    public List<Light> GetLights () {
        return lights;
    }

    void OnDrawGizmosSelected () {
        Gizmos.matrix = this.transform.localToWorldMatrix;
        Gizmos.DrawWireSphere (Vector3.zero, range);
    }

#if UNITY_EDITOR
    [ContextMenu ("Generate Lights")]
    void Generate () {
        ClearLights ();
        GenerateLights ();
    }

    void ClearLights () {
        foreach (var light in lights) {
            DestroyImmediate (light.gameObject);
        }
        lights.Clear ();
    }

    void GenerateLights () {
        for (int i = 0; i < number; i++) {
            var go = PrefabUtility.InstantiatePrefab (prefab.gameObject) as GameObject;
            go.transform.parent = this.transform;
            var radius = Random.Range (0.1f, range);
            go.transform.localPosition = Random.insideUnitSphere * radius;

            var light = go.GetComponent<Light> ();
            light.color = new Color (Random.Range (0.5f, 1f), Random.Range (0.5f, 1f), Random.Range (0.5f, 1f));
            lights.Add (light);
        }
    }
#endif
}