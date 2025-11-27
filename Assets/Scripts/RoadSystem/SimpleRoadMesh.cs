using UnityEngine;
using UnityEngine.Splines;
using System.Collections.Generic;

[RequireComponent(typeof(SplineContainer))]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class SimpleRoadMesh : MonoBehaviour
{
    [Header("Settings")]
    public float roadWidth = 8f;
    public int resolution = 10; 
    public Material roadMaterial;

    public void Generate()
    {
        var container = GetComponent<SplineContainer>();
        if (container == null) return;
        
        var spline = container.Spline;
        if (spline.Count < 2) return; 

        List<Vector3> verts = new List<Vector3>();
        List<int> tris = new List<int>();
        List<Vector2> uvs = new List<Vector2>();

        // 1. MATERIAL SETUP
        var renderer = GetComponent<MeshRenderer>();
        if (roadMaterial != null) renderer.sharedMaterial = roadMaterial;
        else if (renderer.sharedMaterial == null) renderer.sharedMaterial = new Material(Shader.Find("Standard")); 

        float totalLength = container.CalculateLength();
        int steps = Mathf.CeilToInt(totalLength * resolution * 0.1f); 
        if (steps < 2) steps = 2;

        for (int i = 0; i <= steps; i++)
        {
            float t = (float)i / steps;
            
            Vector3 localPos = spline.EvaluatePosition(t);
            Vector3 localTangent = spline.EvaluateTangent(t);
            
            // --- CRITICAL FIX IS HERE ---
            // Cross(Up, Tangent) produces the correct RIGHT vector.
            // Previous code used Cross(Tangent, Up) which produced LEFT, flipping the mesh.
            Vector3 right = Vector3.Cross(Vector3.up, localTangent).normalized * (roadWidth * 0.5f);
            
            Vector3 pLeft = localPos - right;
            Vector3 pRight = localPos + right;
            
            verts.Add(pLeft);  // Index 0, 2, 4...
            verts.Add(pRight); // Index 1, 3, 5...
            
            float v = t * totalLength * 0.2f; 
            uvs.Add(new Vector2(0, v)); 
            uvs.Add(new Vector2(1, v));

            if (i > 0)
            {
                int currentCount = verts.Count;
                // Indices relative to current step
                int v0 = currentCount - 4; // Prev Left
                int v1 = currentCount - 3; // Prev Right
                int v2 = currentCount - 2; // Curr Left
                int v3 = currentCount - 1; // Curr Right

                // TRIANGLE WINDING (Clockwise = Up Facing)
                
                // Triangle 1: PrevLeft -> CurrLeft -> PrevRight
                tris.Add(v0); 
                tris.Add(v2); 
                tris.Add(v1);

                // Triangle 2: CurrLeft -> CurrRight -> PrevRight
                tris.Add(v2); 
                tris.Add(v3); 
                tris.Add(v1);
            }
        }

        Mesh mesh = new Mesh();
        mesh.name = "GeneratedRoad";
        mesh.vertices = verts.ToArray();
        mesh.triangles = tris.ToArray();
        mesh.uv = uvs.ToArray();
        
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        GetComponent<MeshFilter>().mesh = mesh;
        if (GetComponent<MeshCollider>()) GetComponent<MeshCollider>().sharedMesh = mesh;
    }
}