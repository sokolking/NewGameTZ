using System.Collections;
using UnityEngine;

/// <summary>
/// Создаёт плоскость травяного фона под гексовой сеткой.
/// Добавьте этот компонент на тот же GameObject, что и HexGrid.
///
/// Текстура: положите grass_seamless.jpg в Assets/Resources/ — загрузится автоматически.
/// Либо перетащите текстуру в поле Grass Texture в инспекторе.
/// </summary>
[RequireComponent(typeof(HexGrid))]
[DefaultExecutionOrder(100)]
public class GrassGroundPlane : MonoBehaviour
{
    [Header("Grass texture")]
    [Tooltip("Grass texture. If unset, loads Assets/Resources/grass_seamless.")]
    [SerializeField] private Texture2D _grassTexture;

    [Tooltip("One texture tile size in world units. Smaller = finer pattern.")]
    [SerializeField] private float _tileWorldSize = 3f;

    [Header("Plane transform")]
    [Tooltip("Plane position (local). If Scale is (0,0), computed from grid.")]
    [SerializeField] private Vector3 _position = new Vector3(0f, -0.02f, 0f);

    [Tooltip("Plane rotation (Euler). X=90 = horizontal plane.")]
    [SerializeField] private Vector3 _rotation = new Vector3(90f, 0f, 0f);

    [Tooltip("Plane scale (X, Y). (0,0) = auto from grid size + Border Padding.")]
    [SerializeField] private Vector2 _scale = Vector2.zero;

    [Tooltip("Padding around grid when auto-calculating scale (world units).")]
    [SerializeField] private float _borderPadding = 1.5f;

    private GameObject _groundPlane;

    private IEnumerator Start()
    {
        yield return null;
        BuildGroundPlane();
    }

    [ContextMenu("Перестроить плоскость травы")]
    public void BuildGroundPlane()
    {
        DestroyOldPlane();

        HexGrid grid = GetComponent<HexGrid>();
        grid.GetGridBoundsWorld(out float minX, out float maxX, out float minZ, out float maxZ);

        // Авторасчёт Scale, если не задан вручную
        float sizeX = _scale.x > 0f ? _scale.x : (maxX - minX) + _borderPadding * 2f;
        float sizeZ = _scale.y > 0f ? _scale.y : (maxZ - minZ) + _borderPadding * 2f;

        // Авторасчёт Position X/Z по центру сетки, если поле нулевое
        Vector3 pos = _position;
        if (pos.x == 0f && pos.z == 0f)
        {
            Vector3 worldCenter = new Vector3((minX + maxX) * 0.5f, 0f, (minZ + maxZ) * 0.5f);
            Vector3 localCenter = transform.InverseTransformPoint(worldCenter);
            pos.x = localCenter.x;
            pos.z = localCenter.z;
        }

        _groundPlane = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _groundPlane.name = "GrassGround";
        _groundPlane.transform.SetParent(transform);
        _groundPlane.transform.localPosition = pos;
        _groundPlane.transform.localRotation = Quaternion.Euler(_rotation);
        _groundPlane.transform.localScale = new Vector3(sizeX, sizeZ, 1f);

        Collider col = _groundPlane.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying) Destroy(col);
            else DestroyImmediate(col);
        }

        Texture2D tex = _grassTexture != null
            ? _grassTexture
            : Resources.Load<Texture2D>("grass_seamless");

        MeshRenderer mr = _groundPlane.GetComponent<MeshRenderer>();
        mr.sharedMaterial = CreateGrassMaterial(tex, sizeX, sizeZ);
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
    }

    private void DestroyOldPlane()
    {
        if (_groundPlane != null)
        {
            if (Application.isPlaying) Destroy(_groundPlane);
            else DestroyImmediate(_groundPlane);
            _groundPlane = null;
        }

        Transform existing = transform.Find("GrassGround");
        if (existing != null)
        {
            if (Application.isPlaying) Destroy(existing.gameObject);
            else DestroyImmediate(existing.gameObject);
        }
    }

    private Material CreateGrassMaterial(Texture2D tex, float sizeX, float sizeZ)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Unlit/Texture");
        Material mat = new Material(shader) { name = "GrassMaterial" };

        if (tex == null)
        {
            Debug.LogWarning("[GrassGroundPlane] Texture not found. " +
                             "Place grass_seamless.jpg in Assets/Resources/ or assign in the inspector.");
            return mat;
        }

        float tilesX = Mathf.Max(1f, sizeX / _tileWorldSize);
        float tilesZ = Mathf.Max(1f, sizeZ / _tileWorldSize);
        Vector2 tiling = new Vector2(tilesX, tilesZ);

        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", tex);
            mat.SetTextureScale("_BaseMap", tiling);
        }

        if (mat.HasProperty("_MainTex"))
        {
            mat.SetTexture("_MainTex", tex);
            mat.SetTextureScale("_MainTex", tiling);
        }

        return mat;
    }
}
