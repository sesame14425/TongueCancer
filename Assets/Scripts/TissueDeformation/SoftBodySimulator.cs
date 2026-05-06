using UnityEngine;

namespace TongueCancer.TissueDeformation
{
    /// <summary>
    /// 組織材質屬性提供者 + 視覺覆蓋層（Tissue Material Provider + Visual Overlay）：
    /// 提供病變/正常區域的局部硬度查詢，並在接觸時產生視覺上的表面凹陷動畫。
    /// 物理模擬由 MassSpringDeformation 的 Shape Matching 引擎負責；
    /// 本元件僅管理「視覺回饋」與「材質區分查詢」。
    ///
    /// Tissue material property provider + visual overlay:
    /// Supplies local hardness queries for lesion / normal regions and produces
    /// a surface-dimple visual animation on contact.
    /// Physics simulation is handled by MassSpringDeformation's Shape Matching engine;
    /// this component manages only visual feedback and material query.
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshCollider))]
    public class SoftBodySimulator : MonoBehaviour
    {
        [Header("材質硬度 / Material Hardness")]
        [Tooltip("正常組織基礎硬度（0=全軟，1=剛性）Baseline tissue hardness (0=fully soft, 1=rigid)")]
        [Range(0f, 1f)]
        public float tissueHardness = 0.3f;

        [Tooltip("病變區域硬度倍率 Lesion region hardness multiplier")]
        [Range(0.1f, 5f)]
        public float lesionHardnessMultiplier = 2f;

        [Header("病變區域 / Lesion Region")]
        [Tooltip("病變區域中心（本地座標）Lesion centre (local space)")]
        public Vector3 lesionCenter = Vector3.zero;

        [Tooltip("病變半徑 Lesion radius")]
        [Range(0f, 0.1f)]
        public float lesionRadius = 0.02f;

        [Header("視覺凹陷動畫 / Visual Dimple Animation")]
        [Tooltip("組織厚度（公尺）—決定最大視覺凹陷深度 Tissue thickness (m) — sets max visual dimple depth")]
        [Range(0.001f, 0.1f)]
        public float tissueThickness = 0.005f;

        [Tooltip("視覺回彈時間（秒）Visual rebound time (seconds)")]
        [Range(0.01f, 5f)]
        public float reboundTime = 0.5f;

        // ── 視覺層內部狀態（與 Shape Matching 物理完全分離）
        // ── Visual-layer internal state (completely decoupled from Shape Matching physics)
        private Mesh _overlayMesh;           // 僅供視覺凹陷用的獨立 Mesh 副本（若存在）
        private MeshCollider _meshCollider;
        private Vector3[] _originalVertices;
        private Vector3[] _currentVertices;
        private float[]   _displacementAmounts;
        private float[]   _reboundTimers;
        private bool      _visualLayerReady;

        private void Awake()
        {
            // 視覺層使用 Mesh 的唯讀快照，不干涉 Shape Matching 對同一網格的操作
            // The visual layer reads an initial snapshot; it does NOT drive the shared mesh
            // (MassSpringDeformation owns the mesh).  We keep a position cache for queries only.
            var mf = GetComponent<MeshFilter>();
            _meshCollider = GetComponent<MeshCollider>();

            if (mf != null && mf.sharedMesh != null)
            {
                _originalVertices    = mf.sharedMesh.vertices;
                _currentVertices     = (Vector3[])_originalVertices.Clone();
                _displacementAmounts = new float[_originalVertices.Length];
                _reboundTimers       = new float[_originalVertices.Length];
                _visualLayerReady    = true;
            }
        }

        private void Update()
        {
            // 視覺回彈動畫：純計時，不寫入網格（網格由 MassSpringDeformation 管理）
            // Visual rebound animation: timer-only, does NOT write to mesh (owned by MassSpringDeformation)
            if (!_visualLayerReady) return;
            for (int i = 0; i < _reboundTimers.Length; i++)
            {
                if (_reboundTimers[i] > 0f)
                    _reboundTimers[i] -= Time.deltaTime;
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 公開 API / Public API
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 根據接觸力觸發視覺凹陷計時（不修改 mesh）。
        /// Triggers the visual dimple timer for vertices near the contact point (does NOT modify mesh).
        /// </summary>
        /// <param name="contactPointWorld">接觸點（世界座標）Contact point (world space)</param>
        /// <param name="forceNewton">施力大小（牛頓）Force magnitude (Newtons)</param>
        /// <param name="influenceRadius">影響半徑（公尺）Influence radius (metres)</param>
        public void ApplyContactDeformation(Vector3 contactPointWorld, float forceNewton, float influenceRadius = 0.01f)
        {
            if (!_visualLayerReady) return;
            Vector3 localContact = transform.InverseTransformPoint(contactPointWorld);

            for (int i = 0; i < _originalVertices.Length; i++)
            {
                float dist = Vector3.Distance(_originalVertices[i], localContact);
                if (dist > influenceRadius) continue;

                float hardness    = GetLocalHardness(i);
                float falloff     = 1f - (dist / influenceRadius);
                float displacement = Mathf.Clamp(
                    (forceNewton / hardness) * falloff * tissueThickness,
                    0f, tissueThickness);

                _displacementAmounts[i] = displacement;
                _reboundTimers[i]       = reboundTime;
            }
        }

        /// <summary>
        /// 計算指定頂點位置的局部硬度，病變區域具有較高硬度。
        /// 結果可用於工具接觸力的比例縮放。
        /// Calculates local hardness at a vertex; lesion regions have higher hardness.
        /// The result can be used to scale tool contact forces.
        /// </summary>
        /// <param name="vertexIndex">原始頂點索引（未合併）Original vertex index (pre-merge)</param>
        public float GetLocalHardness(int vertexIndex)
        {
            if (!_visualLayerReady || vertexIndex < 0 || vertexIndex >= _originalVertices.Length)
                return Mathf.Max(tissueHardness, 0.01f);

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
        /// 取得指定頂點在回彈動畫中的當前視覺凹陷量（0 = 無凹陷）。
        /// Returns the current visual dimple displacement for a vertex (0 = no dimple).
        /// </summary>
        public float GetVisualDisplacement(int vertexIndex)
        {
            if (!_visualLayerReady || vertexIndex < 0 || vertexIndex >= _reboundTimers.Length)
                return 0f;
            if (_reboundTimers[vertexIndex] <= 0f) return 0f;
            float t = 1f - Mathf.Clamp01(_reboundTimers[vertexIndex] / reboundTime);
            return Mathf.Lerp(_displacementAmounts[vertexIndex], 0f, t);
        }

        /// <summary>
        /// 重置視覺凹陷狀態。
        /// Resets the visual dimple state.
        /// </summary>
        public void ResetDeformation()
        {
            if (!_visualLayerReady) return;
            System.Array.Clear(_displacementAmounts, 0, _displacementAmounts.Length);
            System.Array.Clear(_reboundTimers,       0, _reboundTimers.Length);
        }
    }
}
