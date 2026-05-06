using UnityEngine;
using TongueCancer.Tools;
using TongueCancer.TissueDeformation;

namespace TongueCancer.VRIntegration
{
    /// <summary>
    /// 虛實整合橋接器（Virtual-Physical Bridge）：
    /// 將 XR 控制器的位置/旋轉映射到虛擬手術工具，
    /// 並將工具與組織互動的結果傳回觸覺設備（如果存在）。
    ///
    /// Maps XR controller pose to virtual surgical tools and
    /// relays tissue interaction results back to haptic devices (if present).
    /// </summary>
    public class VirtualPhysicalBridge : MonoBehaviour
    {
        [Header("XR 追蹤目標 / XR Tracking Targets")]
        [Tooltip("右手 XR 控制器或追蹤點 Right hand XR controller/tracking anchor")]
        public Transform rightHandAnchor;

        [Tooltip("左手 XR 控制器或追蹤點 Left hand XR controller/tracking anchor")]
        public Transform leftHandAnchor;

        [Header("虛擬工具 / Virtual Tools")]
        [Tooltip("右手對應的虛擬工具 Virtual tool attached to right hand")]
        public SurgicalToolBase rightHandTool;

        [Tooltip("左手對應的虛擬工具 Virtual tool attached to left hand")]
        public SurgicalToolBase leftHandTool;

        [Header("位置平滑 / Position Smoothing")]
        [Tooltip("追蹤平滑係數 Tracking smoothing factor (0 = no smoothing)")]
        [Range(0f, 0.99f)]
        public float trackingSmoothing = 0.1f;

        [Header("觸覺回饋 / Haptic Feedback")]
        [Tooltip("啟用觸覺回饋（需硬體支援）Enable haptic feedback (requires hardware)")]
        public bool enableHaptics = true;

        [Tooltip("組織接觸時的震動強度 Vibration amplitude on tissue contact (0–1)")]
        [Range(0f, 1f)]
        public float contactHapticAmplitude = 0.3f;

        [Tooltip("觸覺震動持續時間（秒）Haptic vibration duration (seconds)")]
        [Range(0.01f, 0.5f)]
        public float hapticDuration = 0.1f;

        private Vector3 _rightSmoothedPos;
        private Quaternion _rightSmoothedRot;
        private Vector3 _leftSmoothedPos;
        private Quaternion _leftSmoothedRot;

        private void Start()
        {
            // 訂閱工具接觸事件 / Subscribe to tool contact events
            if (rightHandTool != null)
                rightHandTool.OnTissueContact += (pos, tissue) => TriggerHaptic(true, contactHapticAmplitude);
            if (leftHandTool != null)
                leftHandTool.OnTissueContact += (pos, tissue) => TriggerHaptic(false, contactHapticAmplitude);

            // 初始化平滑位置 / Initialise smoothed positions
            if (rightHandAnchor != null)
            {
                _rightSmoothedPos = rightHandAnchor.position;
                _rightSmoothedRot = rightHandAnchor.rotation;
            }
            if (leftHandAnchor != null)
            {
                _leftSmoothedPos = leftHandAnchor.position;
                _leftSmoothedRot = leftHandAnchor.rotation;
            }
        }

        private void Update()
        {
            UpdateToolPose(rightHandAnchor, rightHandTool, ref _rightSmoothedPos, ref _rightSmoothedRot);
            UpdateToolPose(leftHandAnchor, leftHandTool, ref _leftSmoothedPos, ref _leftSmoothedRot);
        }

        /// <summary>
        /// 將追蹤錨點的位姿平滑映射至虛擬工具的 Transform。
        /// Smoothly maps a tracking anchor pose to the virtual tool's Transform.
        /// </summary>
        private void UpdateToolPose(Transform anchor, SurgicalToolBase tool,
                                    ref Vector3 smoothedPos, ref Quaternion smoothedRot)
        {
            if (anchor == null || tool == null) return;

            smoothedPos = Vector3.Lerp(smoothedPos, anchor.position, 1f - trackingSmoothing);
            smoothedRot = Quaternion.Slerp(smoothedRot, anchor.rotation, 1f - trackingSmoothing);

            tool.transform.position = smoothedPos;
            tool.transform.rotation = smoothedRot;
        }

        /// <summary>
        /// 向指定手的 XR 控制器發送觸覺震動請求。
        /// Sends a haptic vibration request to the specified hand's XR controller.
        /// </summary>
        /// <param name="rightHand">是否為右手 Whether it is the right hand</param>
        /// <param name="amplitude">震動強度 Vibration amplitude (0–1)</param>
        public void TriggerHaptic(bool rightHand, float amplitude)
        {
            if (!enableHaptics) return;

#if UNITY_2021_1_OR_NEWER && (UNITY_ANDROID || UNITY_STANDALONE || UNITY_WSA)
            // Unity XR Input haptic pulse (device-agnostic)
            var device = rightHand
                ? UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand)
                : UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.LeftHand);

            if (device.isValid)
            {
                UnityEngine.XR.HapticCapabilities caps;
                if (device.TryGetHapticCapabilities(out caps) && caps.supportsImpulse)
                    device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), hapticDuration);
            }
#endif
            // 非 XR 平台時靜默忽略 / Silently ignored on non-XR platforms
        }

        /// <summary>
        /// 依組織硬度動態調整觸覺回饋強度（用於區分病變與正常組織）。
        /// Dynamically adjusts haptic intensity based on tissue hardness
        /// (used to distinguish lesion from normal tissue).
        /// </summary>
        public void SendHardnessFeedback(bool rightHand, float hardnessRatio)
        {
            float amplitude = Mathf.Clamp01(hardnessRatio / 5f) * contactHapticAmplitude;
            TriggerHaptic(rightHand, amplitude);
        }
    }
}
