using UnityEngine;

namespace TongueCancer.TissueDeformation
{
    /// <summary>
    /// 軟體模擬器（Soft Body Simulator）：使用頂點位移與法線平滑
    /// 模擬生物組織的軟性行為，適合即時互動的外科訓練場景。
    /// Soft Body Simulator: uses vertex displacement and normal smoothing
    /// to simulate soft biological tissue behavior for real-time surgical training.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshCollider))]
    public class SoftBodySimulator : MonoBehaviour
    {
        [Header("軟體參數 / Soft Body Parameters")]
        [Tooltip("壓縮硬度 Compression hardness (0 = fully soft, 1 = rigid)")]
        [Range(0f, 1f)]
        public float tissueHardness = 0.3f;

        [Tooltip("組織厚度（公尺）Tissue layer thickness (metres)")]
        [Range(0.001f, 0.1f)]
        public float tissueThickness = 0.005f;

        [Tooltip("回彈時間（秒）Rebound time (seconds)")]
        [Range(0.01f, 5f)]
        public float reboundTime = 0.5f;

        [Header("病變區域 / Lesion Regions")]
        [Tooltip("病變區域硬度倍率 Lesion region hardness multiplier")]
        [Range(0.1f, 5f)]
        public float lesionHardnessMultiplier = 2f;

        [Tooltip("病變區域中心（本地座標）Lesion center (local space)")]
        public Vector3 lesionCenter = Vector3.zero;

        [Tooltip("病變半徑 Lesion radius")]
        [Range(0f, 0.1f)]
        public float lesionRadius = 0.02f;

        // Internal state
        private Mesh _mesh;
        private MeshCollider _meshCollider;
        private Vector3[] _originalVertices;
        private Vector3[] _currentVertices;
        private float[] _displacementAmounts;
        private float[] _reboundTimers;

        private void Awake()
        {
            _mesh = GetComponent<MeshFilter>().mesh;
            _meshCollider = GetComponent<MeshCollider>();

            _originalVertices = _mesh.vertices;
            _currentVertices = (Vector3[])_originalVertices.Clone();
            _displacementAmounts = new float[_originalVertices.Length];
            _reboundTimers = new float[_originalVertices.Length];
        }

        private void Update()
        {
            bool meshUpdated = false;
            for (int i = 0; i < _currentVertices.Length; i++)
            {
                if (_reboundTimers[i] > 0f)
                {
                    _reboundTimers[i] -= Time.deltaTime;
                    float t = 1f - Mathf.Clamp01(_reboundTimers[i] / reboundTime);
                    _currentVertices[i] = Vector3.Lerp(
                        _originalVertices[i] + _mesh.normals[i] * (-_displacementAmounts[i]),
                        _originalVertices[i],
                        t
                    );
                    meshUpdated = true;
                }
            }

            if (meshUpdated)
            {
                _mesh.vertices = _currentVertices;
                _mesh.RecalculateNormals();
                _meshCollider.sharedMesh = _mesh;
            }
        }

        /// <summary>
        /// 根據接觸力在指定位置產生組織壓縮變形。
        /// Produces tissue compression deformation at the specified position based on contact force.
        /// </summary>
        /// <param name="contactPointWorld">接觸點（世界座標）Contact point (world space)</param>
        /// <param name="forceNewton">施力大小（牛頓）Force magnitude (Newtons)</param>
        /// <param name="influenceRadius">影響半徑（公尺）Influence radius (metres)</param>
        public void ApplyContactDeformation(Vector3 contactPointWorld, float forceNewton, float influenceRadius = 0.01f)
        {
            Vector3 localContact = transform.InverseTransformPoint(contactPointWorld);

            for (int i = 0; i < _currentVertices.Length; i++)
            {
                float dist = Vector3.Distance(_originalVertices[i], localContact);
                if (dist > influenceRadius) continue;

                float hardness = GetLocalHardness(i);
                float falloff = 1f - (dist / influenceRadius);
                float displacement = (forceNewton / hardness) * falloff * tissueThickness;
                displacement = Mathf.Clamp(displacement, 0f, tissueThickness);

                _displacementAmounts[i] = displacement;
                _reboundTimers[i] = reboundTime;

                Vector3[] normals = _mesh.normals;
                _currentVertices[i] = _originalVertices[i] - normals[i] * displacement;
            }

            _mesh.vertices = _currentVertices;
            _mesh.RecalculateNormals();
            _meshCollider.sharedMesh = _mesh;
        }

        /// <summary>
        /// 計算指定頂點位置的局部硬度，病變區域具有較高硬度。
        /// Calculates local hardness at a vertex; lesion regions have higher hardness.
        /// </summary>
        private float GetLocalHardness(int vertexIndex)
        {
            float baseHardness = Mathf.Max(tissueHardness, 0.01f);
            float dist = Vector3.Distance(_originalVertices[vertexIndex], lesionCenter);
            if (dist < lesionRadius)
            {
                float blendFactor = 1f - (dist / lesionRadius);
                baseHardness *= Mathf.Lerp(1f, lesionHardnessMultiplier, blendFactor);
            }
            return baseHardness;
        }

        /// <summary>
        /// 重置所有組織變形至初始狀態。
        /// Resets all tissue deformation to the initial state.
        /// </summary>
        public void ResetDeformation()
        {
            _currentVertices = (Vector3[])_originalVertices.Clone();
            _displacementAmounts = new float[_originalVertices.Length];
            _reboundTimers = new float[_originalVertices.Length];
            _mesh.vertices = _currentVertices;
            _mesh.RecalculateNormals();
            _meshCollider.sharedMesh = _mesh;
        }

        private void OnDestroy()
        {
            if (_mesh != null)
                _mesh.vertices = _originalVertices;
        }
    }
}
