using UnityEngine;

namespace TongueCancer.TissueDeformation
{
    /// <summary>
    /// 組織變形網格控制器（Tissue Deformation Mesh Controller）：
    /// 作為 MassSpringDeformation 與 SoftBodySimulator 的上層整合介面，
    /// 提供統一的 API 讓手術工具呼叫。
    ///
    /// Top-level integration controller for MassSpringDeformation and SoftBodySimulator.
    /// Provides a unified API for surgical tools to invoke.
    /// </summary>
    [RequireComponent(typeof(MassSpringDeformation))]
    [RequireComponent(typeof(SoftBodySimulator))]
    public class TissueDeformationController : MonoBehaviour
    {
        [Header("變形模式 / Deformation Mode")]
        public DeformationMode activeMode = DeformationMode.SoftBody;

        [Header("視覺回饋 / Visual Feedback")]
        [Tooltip("變形時的材質顏色疊加 Material color overlay during deformation")]
        public Color deformationHighlightColor = new Color(1f, 0.5f, 0.5f, 0.3f);

        [Tooltip("病變組織顏色 Lesion tissue color")]
        public Color lesionColor = new Color(0.8f, 0.2f, 0.2f, 1f);

        [Tooltip("正常組織顏色 Normal tissue color")]
        public Color normalTissueColor = new Color(0.95f, 0.75f, 0.75f, 1f);

        public enum DeformationMode
        {
            /// <summary>質量彈簧模型 Mass-spring model</summary>
            MassSpring,
            /// <summary>軟體模擬器 Soft body simulator</summary>
            SoftBody,
            /// <summary>兩者同時啟用 Both active simultaneously</summary>
            Combined
        }

        private MassSpringDeformation _massSpring;
        private SoftBodySimulator _softBody;
        private Renderer _renderer;
        private MaterialPropertyBlock _propertyBlock;

        private void Awake()
        {
            _massSpring = GetComponent<MassSpringDeformation>();
            _softBody = GetComponent<SoftBodySimulator>();
            _renderer = GetComponent<Renderer>();
            _propertyBlock = new MaterialPropertyBlock();

            SetMode(activeMode);
        }

        /// <summary>
        /// 切換變形模式並相應地啟用/停用底層元件。
        /// Switches the deformation mode, enabling/disabling underlying components accordingly.
        /// </summary>
        public void SetMode(DeformationMode mode)
        {
            activeMode = mode;
            _massSpring.enabled = mode == DeformationMode.MassSpring || mode == DeformationMode.Combined;
            _softBody.enabled = mode == DeformationMode.SoftBody || mode == DeformationMode.Combined;
        }

        /// <summary>
        /// 在指定世界座標位置施加接觸力，觸發對應的變形模型。
        /// Applies contact force at the specified world position, triggering the active deformation model.
        /// </summary>
        /// <param name="worldPosition">接觸點（世界座標）Contact point (world space)</param>
        /// <param name="forceVector">力向量（世界座標）Force vector (world space)</param>
        /// <param name="influenceRadius">影響半徑 Influence radius</param>
        public void ApplyContactForce(Vector3 worldPosition, Vector3 forceVector, float influenceRadius = 0.01f)
        {
            float forceMagnitude = forceVector.magnitude;

            if (activeMode == DeformationMode.MassSpring || activeMode == DeformationMode.Combined)
                _massSpring.ApplyForceAtPosition(worldPosition, forceVector, influenceRadius);

            if (activeMode == DeformationMode.SoftBody || activeMode == DeformationMode.Combined)
                _softBody.ApplyContactDeformation(worldPosition, forceMagnitude, influenceRadius);

            HighlightDeformation(worldPosition, influenceRadius);
        }

        /// <summary>
        /// 以視覺方式高亮顯示正在發生變形的區域。
        /// Visually highlights the area undergoing deformation.
        /// </summary>
        private void HighlightDeformation(Vector3 worldPosition, float radius)
        {
            if (_renderer == null) return;
            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor("_DeformHighlightColor", deformationHighlightColor);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>
        /// 設定病變標記位置與半徑，傳遞至 SoftBodySimulator 以調整局部硬度。
        /// Sets lesion marker position and radius, forwarded to SoftBodySimulator to adjust local hardness.
        /// </summary>
        public void SetLesionRegion(Vector3 localCenter, float radius)
        {
            _softBody.lesionCenter = localCenter;
            _softBody.lesionRadius = radius;
        }

        /// <summary>
        /// 套用病變組織顏色至渲染器。
        /// Applies lesion tissue color to the renderer.
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
            _massSpring.ResetDeformation();
            _softBody.ResetDeformation();
            if (_renderer != null)
            {
                _renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor("_BaseColor", normalTissueColor);
                _renderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
