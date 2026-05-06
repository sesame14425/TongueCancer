using UnityEngine;
using TongueCancer.TissueDeformation;

namespace TongueCancer.Tools
{
    /// <summary>
    /// 鑷子工具（Forceps Tool）：模擬夾取和牽引舌頭組織的操作，
    /// 讓學員練習組織的拉扯與固定。
    ///
    /// Forceps Tool: simulates grasping and retracting tongue tissue,
    /// allowing trainees to practise tissue traction and fixation.
    /// </summary>
    public class ForcepsTool : SurgicalToolBase
    {
        [Header("鑷子設定 / Forceps Settings")]
        [Tooltip("鑷子開合角度（度）Forceps open/close angle (degrees)")]
        [Range(0f, 45f)]
        public float jawAngle = 0f;

        [Tooltip("最大開合角度 Maximum jaw angle (degrees)")]
        [Range(5f, 60f)]
        public float maxJawAngle = 30f;

        [Tooltip("左鑷臂 Left jaw arm transform")]
        public Transform leftJaw;

        [Tooltip("右鑷臂 Right jaw arm transform")]
        public Transform rightJaw;

        [Tooltip("夾取力量（牛頓）Grasping force (Newtons)")]
        [Range(0.1f, 20f)]
        public float graspForce = 5f;

        private bool _isGrasping;
        private TissueDeformationController _graspedTissue;
        private Vector3 _graspLocalOffset;

        private void Awake()
        {
            toolName = "Forceps";
        }

        protected override void Update()
        {
            base.Update();
            UpdateJawVisual();
        }

        /// <summary>
        /// 更新左右鑷臂的旋轉視覺。
        /// Updates left/right jaw visual rotation.
        /// </summary>
        private void UpdateJawVisual()
        {
            if (leftJaw != null)
                leftJaw.localRotation = Quaternion.Euler(0f, 0f, jawAngle);
            if (rightJaw != null)
                rightJaw.localRotation = Quaternion.Euler(0f, 0f, -jawAngle);
        }

        /// <summary>
        /// 設定鑷子的開合角度（0 = 完全閉合）。
        /// Sets the forceps jaw angle (0 = fully closed).
        /// </summary>
        public void SetJawAngle(float normalizedOpen)
        {
            jawAngle = Mathf.Clamp01(normalizedOpen) * maxJawAngle;
        }

        /// <summary>
        /// 夾取指定組織。
        /// Grasps the specified tissue.
        /// </summary>
        public void Grasp(TissueDeformationController tissue)
        {
            if (_isGrasping) return;
            _isGrasping = true;
            _graspedTissue = tissue;
            SetJawAngle(0f); // 閉合 close jaws

            Vector3 contactPos = contactPoint != null ? contactPoint.position : transform.position;
            tissue.ApplyContactForce(contactPos, -transform.up * graspForce, contactRadius);
        }

        /// <summary>
        /// 釋放夾取的組織。
        /// Releases the grasped tissue.
        /// </summary>
        public void Release()
        {
            _isGrasping = false;
            _graspedTissue = null;
            SetJawAngle(1f); // 張開 open jaws
        }

        protected override void OnContact(TissueDeformationController tissue)
        {
            base.OnContact(tissue);
            // 閉合時自動夾取 / Auto-grasp when closed
            if (jawAngle < 5f && !_isGrasping)
                Grasp(tissue);
        }

        protected override void OnContactEnd(TissueDeformationController tissue)
        {
            if (_isGrasping) Release();
        }

        /// <summary>
        /// 牽引已夾取的組織（沿指定世界方向）。
        /// Retracts the grasped tissue in the specified world direction.
        /// </summary>
        public void RetractTissue(Vector3 worldDirection, float distance)
        {
            if (!_isGrasping || _graspedTissue == null) return;
            Vector3 contactPos = contactPoint != null ? contactPoint.position : transform.position;
            _graspedTissue.ApplyContactForce(contactPos, worldDirection.normalized * distance * graspForce, contactRadius * 2f);
        }

        public bool IsGrasping => _isGrasping;
    }
}
