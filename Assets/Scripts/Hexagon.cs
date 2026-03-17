using UnityEngine;

/// <summary>
/// Строит меш плоского гекса (flat-top) в плоскости XZ, 6 граней.
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class Hexagon : MonoBehaviour
{
    private const float Sqrt3 = 1.732050808f;
    private const int VertexCount = 7;

    /// <summary>Строит меш с заданным радиусом (центр → вершина).</summary>
    public void BuildMesh(float radius)
    {
        MeshFilter meshFilter = GetComponent<MeshFilter>();
        if (meshFilter == null)
            meshFilter = gameObject.AddComponent<MeshFilter>();

        float halfWidth = radius * Sqrt3 * 0.5f;

        Vector3[] vertices = new Vector3[VertexCount];
        vertices[0] = Vector3.zero;
        vertices[1] = new Vector3(0f, 0f, radius);
        vertices[2] = new Vector3(halfWidth, 0f, radius * 0.5f);
        vertices[3] = new Vector3(halfWidth, 0f, -radius * 0.5f);
        vertices[4] = new Vector3(0f, 0f, -radius);
        vertices[5] = new Vector3(-halfWidth, 0f, -radius * 0.5f);
        vertices[6] = new Vector3(-halfWidth, 0f, radius * 0.5f);

        int[] triangles =
        {
            0, 1, 2, 0, 2, 3, 0, 3, 4,
            0, 4, 5, 0, 5, 6, 0, 6, 1
        };

        Vector3[] normals = new Vector3[VertexCount];
        Vector2[] uv = new Vector2[VertexCount];
        for (int i = 0; i < VertexCount; i++)
        {
            normals[i] = Vector3.up;
            if (i == 0)
                uv[i] = new Vector2(0.5f, 0.5f);
            else
                uv[i] = new Vector2(
                    (vertices[i].x / halfWidth + 1f) * 0.5f,
                    (vertices[i].z / radius + 1f) * 0.5f);
        }

        Mesh mesh = new Mesh { name = "Hexagon" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uv);
        mesh.RecalculateBounds();

        meshFilter.sharedMesh = mesh;
    }
}
