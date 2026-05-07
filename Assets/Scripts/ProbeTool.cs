using UnityEngine;
using TongueCancer.TissueDeformation;

namespace TongueCancer.Tools
{
    /// <summary>
    /// 探針工具（Probe Tool）：用於按壓探測舌頭組織，
    /// 透過觸感回饋感受病變與正常組織之間的硬度差異。
    ///
    /// Probe Tool: presses against tongue tissue to detect
    /// hardness differences between lesion and normal tissue via haptic feedback.
    /// </summary>
    public class ProbeTool : SurgicalToolBase
    {
        [Header("探針設定 / Probe Settings")]
        [Tooltip("探針插入速度限制（m/s）Probe insertion speed limit (m/s)")]
        [Range(0.001f, 0.1f)]
        public float maxInsertionSpeed = 0.02f;

        [Tooltip("啟用視覺壓力指示器 Enable visual pressure indicator")]
        public bool showPressureIndicator = true;

        [Tooltip("壓力指示燈（Renderer）Pressure indicator renderer")]
        public Renderer pressureIndicatorRenderer;

        [Header("觸覺回饋 / Haptic Feedback")]
        [Tooltip("硬度偵測靈敏度 Hardness detection sensitivity")]
        [Range(0.1f, 10f)]
        public float hardnessSensitivity = 3f;

        private Vector3 _previousPosition;
        private float _currentInsertionDepth;
        private MaterialPropertyBlock _propBlock;

        private void Awake()
        {
            toolName = "Probe";
            _previousPosition = transform.position;
            _propBlock = new MaterialPropertyBlock();
        }

        protected override void Update()
        {
            base.Update();
            _previousPosition = transform.position;
        }

        /// <summary>
        /// 接觸組織時根據插入速度施加接觸力，並更新壓力指示器。
        /// Applies contact force based on insertion speed on tissue contact and updates pressure indicator.
        /// </summary>
        protected override void OnContact(TissueDeformationController tissue)
        {
            base.OnContact(tissue);

            Vector3 velocity = (transform.position - _previousPosition) / Time.deltaTime;
            float insertionSpeed = Mathf.Clamp(velocity.magnitude, 0f, maxInsertionSpeed);
            float appliedForce = contactForce * (insertionSpeed / maxInsertionSpeed);

            // 朝向接觸點的方向向量 / Direction towards contact point
            Vector3 forceDir = contactPoint != null
                ? (contactPoint.position - transform.position).normalized
                : -transform.up;

            tissue.ApplyContactForce(
                contactPoint != null ? contactPoint.position : transform.position,
                forceDir * appliedForce,
                contactRadius
            );

            UpdatePressureIndicator(appliedForce / contactForce);
        }

        protected override void OnContactEnd(TissueDeformationController tissue)
        {
            UpdatePressureIndicator(0f);
        }

        /// <summary>
        /// 更新壓力視覺指示燈顏色（綠→黃→紅）。
        /// Updates pressure indicator color (green→yellow→red).
        /// </summary>
        private void UpdatePressureIndicator(float normalizedPressure)
        {
            if (!showPressureIndicator || pressureIndicatorRenderer == null) return;

            Color indicatorColor = Color.Lerp(Color.green, Color.red, normalizedPressure);
            pressureIndicatorRenderer.GetPropertyBlock(_propBlock);
            _propBlock.SetColor("_EmissionColor", indicatorColor * 2f);
            pressureIndicatorRenderer.SetPropertyBlock(_propBlock);
        }
    }
}
