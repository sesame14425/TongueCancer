using System.Collections;
using UnityEngine;
using UnityEngine.XR;

namespace TongueCancer.VRIntegration
{
    /// <summary>
    /// 觸覺回饋控制器（Haptic Feedback Controller）：
    /// 提供與組織互動時的各種觸覺回饋模式，
    /// 包含脈衝、漸進與病變偵測震動模式。
    ///
    /// Provides various haptic feedback patterns for tissue interaction,
    /// including pulse, progressive, and lesion-detection vibration modes.
    /// </summary>
    public class HapticFeedbackController : MonoBehaviour
    {
        [Header("觸覺回饋設定 / Haptic Settings")]
        [Tooltip("右手節點 Right hand XR node")]
        public XRNode rightHandNode = XRNode.RightHand;

        [Tooltip("左手節點 Left hand XR node")]
        public XRNode leftHandNode = XRNode.LeftHand;

        [Header("預設震動設定 / Default Vibration Settings")]
        [Tooltip("接觸震動強度 Contact vibration amplitude")]
        [Range(0f, 1f)]
        public float defaultContactAmplitude = 0.3f;

        [Tooltip("病變偵測震動強度 Lesion detection amplitude")]
        [Range(0f, 1f)]
        public float lesionDetectionAmplitude = 0.7f;

        [Tooltip("震動持續時間（秒）Vibration duration (seconds)")]
        [Range(0.01f, 1f)]
        public float defaultDuration = 0.1f;

        [Header("進階模式 / Advanced Modes")]
        [Tooltip("病變脈衝重複次數 Lesion pulse repeat count")]
        [Range(1, 10)]
        public int lesionPulseCount = 3;

        [Tooltip("脈衝間隔（秒）Pulse interval (seconds)")]
        [Range(0.05f, 0.5f)]
        public float pulseInterval = 0.1f;

        /// <summary>
        /// 觸覺回饋模式列舉。
        /// Haptic feedback mode enumeration.
        /// </summary>
        public enum HapticMode
        {
            /// <summary>單次脈衝 Single pulse</summary>
            SinglePulse,
            /// <summary>重複脈衝（病變偵測）Repeated pulse (lesion detection)</summary>
            LesionPulse,
            /// <summary>漸進式（根據壓力）Progressive (based on pressure)</summary>
            Progressive
        }

        /// <summary>
        /// 觸發單次觸覺回饋。
        /// Triggers a single haptic pulse.
        /// </summary>
        public void TriggerSinglePulse(bool rightHand, float amplitude = -1f, float duration = -1f)
        {
            float amp = amplitude < 0 ? defaultContactAmplitude : amplitude;
            float dur = duration < 0 ? defaultDuration : duration;
            SendImpulse(rightHand ? rightHandNode : leftHandNode, amp, dur);
        }

        /// <summary>
        /// 觸發病變偵測模式（多次短脈衝）。
        /// Triggers lesion-detection mode (multiple short pulses).
        /// </summary>
        public void TriggerLesionDetectionPulse(bool rightHand)
        {
            StartCoroutine(LesionPulseRoutine(rightHand ? rightHandNode : leftHandNode));
        }

        private IEnumerator LesionPulseRoutine(XRNode node)
        {
            for (int i = 0; i < lesionPulseCount; i++)
            {
                SendImpulse(node, lesionDetectionAmplitude, defaultDuration);
                yield return new WaitForSeconds(defaultDuration + pulseInterval);
            }
        }

        /// <summary>
        /// 根據壓力值（0–1）持續發送漸進式觸覺。
        /// Sends progressive haptic based on pressure value (0–1).
        /// </summary>
        public void UpdateProgressiveHaptic(bool rightHand, float normalizedPressure)
        {
            float amp = normalizedPressure * defaultContactAmplitude;
            SendImpulse(rightHand ? rightHandNode : leftHandNode, amp, Time.deltaTime);
        }

        /// <summary>
        /// 低階觸覺脈衝發送（封裝 XR 裝置 API）。
        /// Low-level haptic impulse sender (wraps XR device API).
        /// </summary>
        private void SendImpulse(XRNode node, float amplitude, float duration)
        {
#if UNITY_2021_1_OR_NEWER
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (!device.isValid) return;

            HapticCapabilities caps;
            if (device.TryGetHapticCapabilities(out caps) && caps.supportsImpulse)
                device.SendHapticImpulse(0, Mathf.Clamp01(amplitude), duration);
#endif
        }

        /// <summary>
        /// 停止所有觸覺回饋。
        /// Stops all haptic feedback.
        /// </summary>
        public void StopAllHaptics()
        {
            StopAllCoroutines();
#if UNITY_2021_1_OR_NEWER
            StopHapticOnNode(rightHandNode);
            StopHapticOnNode(leftHandNode);
#endif
        }

        private void StopHapticOnNode(XRNode node)
        {
#if UNITY_2021_1_OR_NEWER
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            if (device.isValid) device.StopHaptics();
#endif
        }

        private void OnDisable()
        {
            StopAllHaptics();
        }
    }
}
