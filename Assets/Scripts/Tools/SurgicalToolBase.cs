using UnityEngine;
using TongueCancer.TissueDeformation;

namespace TongueCancer.Tools
{
    /// <summary>
    /// 手術工具基礎類別（Surgical Tool Base）。
    /// 所有虛擬手術工具都繼承自此類別，提供統一的互動介面。
    ///
    /// Base class for all virtual surgical tools.
    /// Provides a unified interaction interface for tissue contact.
    /// </summary>
    public abstract class SurgicalToolBase : MonoBehaviour
    {
        [Header("工具屬性 / Tool Properties")]
        [Tooltip("工具名稱 Tool name")]
        public string toolName = "Surgical Tool";

        [Tooltip("接觸力大小（牛頓）Contact force magnitude (Newtons)")]
        [Range(0.01f, 50f)]
        public float contactForce = 1f;

        [Tooltip("接觸影響半徑（公尺）Contact influence radius (metres)")]
        [Range(0.001f, 0.05f)]
        public float contactRadius = 0.005f;

        [Tooltip("工具是否啟用 Whether the tool is active")]
        public bool isActive = false;

        [Header("碰撞偵測 / Collision Detection")]
        [Tooltip("工具接觸點（本地座標）Tool contact point (local space)")]
        public Transform contactPoint;

        // Events
        public event System.Action<Vector3, TissueDeformationController> OnTissueContact;
        public event System.Action OnToolActivated;
        public event System.Action OnToolDeactivated;

        protected TissueDeformationController CurrentTarget { get; private set; }
        protected bool IsContacting { get; private set; }

        protected virtual void Update()
        {
            if (!isActive) return;
            DetectContact();
        }

        /// <summary>
        /// 偵測與組織的接觸。子類別可覆寫此方法以實現特定偵測邏輯。
        /// Detects contact with tissue. Subclasses can override for specific logic.
        /// </summary>
        protected virtual void DetectContact()
        {
            if (contactPoint == null) return;

            Collider[] hits = Physics.OverlapSphere(contactPoint.position, contactRadius);
            TissueDeformationController target = null;

            foreach (Collider hit in hits)
            {
                target = hit.GetComponent<TissueDeformationController>();
                if (target != null) break;
            }

            if (target != null)
            {
                IsContacting = true;
                CurrentTarget = target;
                OnContact(target);
            }
            else if (IsContacting)
            {
                IsContacting = false;
                OnContactEnd(CurrentTarget);
                CurrentTarget = null;
            }
        }

        /// <summary>
        /// 工具開始接觸組織時呼叫。子類別實現具體行為。
        /// Called when the tool begins contacting tissue. Subclasses implement specific behavior.
        /// </summary>
        protected virtual void OnContact(TissueDeformationController tissue)
        {
            OnTissueContact?.Invoke(contactPoint.position, tissue);
        }

        /// <summary>
        /// 工具結束接觸時呼叫。
        /// Called when the tool ends contact.
        /// </summary>
        protected virtual void OnContactEnd(TissueDeformationController tissue) { }

        /// <summary>
        /// 啟用工具。
        /// Activates the tool.
        /// </summary>
        public virtual void Activate()
        {
            isActive = true;
            OnToolActivated?.Invoke();
        }

        /// <summary>
        /// 停用工具。
        /// Deactivates the tool.
        /// </summary>
        public virtual void Deactivate()
        {
            isActive = false;
            IsContacting = false;
            CurrentTarget = null;
            OnToolDeactivated?.Invoke();
        }

        private void OnDrawGizmosSelected()
        {
            if (contactPoint == null) return;
            Gizmos.color = isActive ? Color.red : Color.yellow;
            Gizmos.DrawWireSphere(contactPoint.position, contactRadius);
        }
    }
}
