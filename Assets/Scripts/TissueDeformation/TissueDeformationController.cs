using UnityEngine;

namespace TongueCancer.TissueDeformation
{
    /// <summary>
    /// 組織變形整合控制器（Tissue Deformation Integration Controller）：
    /// 整合 MassSpringDeformation（GPU Shape Matching 引擎）與
    /// SoftBodySimulator（材質屬性 + 視覺覆蓋層），提供統一的外部 API。
    ///
    /// Integration controller bridging MassSpringDeformation (GPU Shape Matching engine)
    /// and SoftBodySimulator (material properties + visual overlay).
    /// Exposes a unified API for surgical tools.
    /// </summary>
    [RequireComponent(typeof(MassSpringDeformation))]
    [RequireComponent(typeof(SoftBodySimulator))]
    public class TissueDeformationController : MonoBehaviour
    {
        [Header("外部接觸工具 / External Contact Tool")]
        [Tooltip("手術工具控制器（可為空）Surgical tool controller (can be null)")]
        public ToolController tool;

        [Header("視覺回饋 / Visual Feedback")]
        [Tooltip("變形高亮顏色（接觸時）Material colour overlay during deformation")]
        public Color deformationHighlightColor = new Color(1f, 0.5f, 0.5f, 0.3f);

        [Tooltip("病變組織顏色 Lesion tissue colour")]
        public Color lesionColor = new Color(0.8f, 0.2f, 0.2f, 1f);

        [Tooltip("正常組織顏色 Normal tissue colour")]
        public Color normalTissueColor = new Color(0.95f, 0.75f, 0.75f, 1f);

        [Header("力回饋平滑 / Haptic Smoothing")]
        [Range(0f, 1f)]
        public float hapticSmoothing = 0.15f;

        [Header("接觸力限制 / Contact Force Limits")]
        [Tooltip("最大壓入深度（公尺）Max indentation depth (metres)")]
        [Range(0.0001f, 0.05f)]
        public float maxIndentDepth = 0.01f;

        [Tooltip("最大接觸力（牛頓）Max contact force (Newtons)")]
        [Range(0.01f, 50f)]
        public float maxContactForce = 5f;

        // ── 子元件 / Sub-components ───────────────────────────────────────────────
        private MassSpringDeformation _shapeMatching;
        private SoftBodySimulator     _softBody;
        private Renderer              _renderer;
        private MaterialPropertyBlock _propertyBlock;

        // ── 力回饋濾波 / Haptic filtering ─────────────────────────────────────────
        private double  _hapticForceFiltered   = 0.0;
        private Vector3 _reactionForceFiltered = Vector3.zero;

        public double  CurrentHapticForceY   => _hapticForceFiltered;
        public Vector3 ReactionForce         => _reactionForceFiltered;

        // ─────────────────────────────────────────────────────────────────────────

        private void Awake()
        {
            _shapeMatching = GetComponent<MassSpringDeformation>();
            _softBody      = GetComponent<SoftBodySimulator>();
            _renderer      = GetComponent<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();
        }

        private void FixedUpdate()
        {
            // 過濾 Shape Matching 引擎輸出的力回饋，轉發給工具
            // Filter haptic output from the Shape Matching engine and forward to tool
            _hapticForceFiltered = Mathf.Lerp(
                (float)_hapticForceFiltered,
                (float)_shapeMatching.PendingHapticForce,
                hapticSmoothing);

            _reactionForceFiltered = Vector3.Lerp(
                _reactionForceFiltered,
                _shapeMatching.PendingReactionForce,
                hapticSmoothing);

            if (tool != null)
                tool.SetReactionForce(_reactionForceFiltered);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 公開 API / Public API
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// 在指定世界座標位置施加接觸力：
        /// ① 物理層 → MassSpringDeformation.ApplyForceAtPosition()（速度衝量）
        /// ② 視覺層 → SoftBodySimulator.ApplyContactDeformation()（表面凹陷計時）
        ///
        /// Applies contact force at the specified world position:
        /// ① Physics layer → MassSpringDeformation.ApplyForceAtPosition() (velocity impulse)
        /// ② Visual layer  → SoftBodySimulator.ApplyContactDeformation() (surface dimple timer)
        /// </summary>
        public void ApplyContactForce(Vector3 worldPosition, Vector3 forceVector, float influenceRadius = 0.01f)
        {
            float penetration = forceVector.magnitude;
            float normalizedPressure = maxIndentDepth > 1e-6f
                ? Mathf.Clamp01(penetration / maxIndentDepth)
                : 0f;
            float appliedForce = maxContactForce * normalizedPressure;

            Vector3 appliedVector = forceVector.sqrMagnitude > 1e-8f
                ? forceVector.normalized * appliedForce
                : Vector3.zero;

            _shapeMatching.ApplyForceAtPosition(worldPosition, appliedVector, influenceRadius);
            _softBody.ApplyContactDeformation(worldPosition, appliedForce, influenceRadius);
            HighlightDeformation(worldPosition, influenceRadius);
        }

        /// <summary>
        /// 設定外部接觸球體資訊（由 ToolController 每幀呼叫）。
        /// Sets external contact sphere information (called every frame by ToolController).
        /// </summary>
        public void SetExternalContact(Vector3 worldPos, float radius, Vector3 worldVelocity)
        {
            _shapeMatching.externalPos      = worldPos;
            _shapeMatching.externalRadius   = radius;
            _shapeMatching.externalVelocity = worldVelocity;
        }

        /// <summary>
        /// 清除外部接觸（工具離開時呼叫）。
        /// Clears external contact (call when tool leaves).
        /// </summary>
        public void ClearExternalContact()
        {
            _shapeMatching.externalRadius = 0f;
        }

        /// <summary>
        /// 設定病變標記位置與半徑，傳遞至 SoftBodySimulator 以調整局部硬度查詢。
        /// Sets lesion marker position and radius, forwarded to SoftBodySimulator for local hardness queries.
        /// </summary>
        public void SetLesionRegion(Vector3 localCenter, float radius)
        {
            _softBody.lesionCenter = localCenter;
            _softBody.lesionRadius = radius;
        }

        /// <summary>
        /// 套用病變組織顏色至渲染器。
        /// Applies lesion tissue colour to the renderer.
        /// </summary>
        public void ApplyLesionVisualization(bool isLesion)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor("_BaseColor", isLesion ? lesionColor : normalTissueColor);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>
        /// 重置所有變形與視覺狀態。
        /// Resets all deformation and visual state.
        /// </summary>
        public void ResetAll()
        {
            _shapeMatching.ResetDeformation();
            _softBody.ResetDeformation();

            if (_renderer != null)
            {
                _renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor("_BaseColor", normalTissueColor);
                _renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // 視覺高亮 / Visual highlight
        // ─────────────────────────────────────────────────────────────────────────

        private void HighlightDeformation(Vector3 worldPosition, float radius)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor("_DeformHighlightColor", deformationHighlightColor);
            _renderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
