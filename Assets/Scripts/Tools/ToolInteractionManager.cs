using System.Collections.Generic;
using UnityEngine;

namespace TongueCancer.Tools
{
    /// <summary>
    /// 工具互動管理器（Tool Interaction Manager）：
    /// 統一管理場景中所有手術工具的啟用/切換，
    /// 提供 UI 事件接口讓訓練系統呼叫。
    ///
    /// Manages activation/switching of all surgical tools in the scene.
    /// Provides UI event interfaces for the training system.
    /// </summary>
    public class ToolInteractionManager : MonoBehaviour
    {
        [Header("工具清單 / Tool List")]
        [Tooltip("場景中所有可用工具 All available tools in scene")]
        public List<SurgicalToolBase> availableTools = new List<SurgicalToolBase>();

        [Header("初始工具 / Initial Tool")]
        [Tooltip("初始激活工具索引 Initial active tool index")]
        public int initialToolIndex = 0;

        public event System.Action<SurgicalToolBase> OnToolChanged;

        private SurgicalToolBase _activeTool;
        private int _activeIndex = -1;

        private void Start()
        {
            if (availableTools.Count > 0)
                SelectTool(Mathf.Clamp(initialToolIndex, 0, availableTools.Count - 1));
        }

        /// <summary>
        /// 依索引選取並啟用工具，停用其餘工具。
        /// Selects and activates the tool at the given index, deactivating all others.
        /// </summary>
        public void SelectTool(int index)
        {
            if (index < 0 || index >= availableTools.Count)
            {
                Debug.LogWarning($"[ToolInteractionManager] Invalid tool index: {index}");
                return;
            }

            if (_activeTool != null)
                _activeTool.Deactivate();

            _activeIndex = index;
            _activeTool = availableTools[index];
            _activeTool.Activate();
            OnToolChanged?.Invoke(_activeTool);

            Debug.Log($"[ToolInteractionManager] Selected tool: {_activeTool.toolName}");
        }

        /// <summary>
        /// 依名稱選取工具。
        /// Selects a tool by name.
        /// </summary>
        public void SelectToolByName(string name)
        {
            int idx = availableTools.FindIndex(t => t.toolName == name);
            if (idx >= 0)
                SelectTool(idx);
            else
                Debug.LogWarning($"[ToolInteractionManager] Tool not found: {name}");
        }

        /// <summary>
        /// 循環選取下一個工具。
        /// Cycles to the next tool.
        /// </summary>
        public void NextTool()
        {
            if (availableTools.Count == 0) return;
            SelectTool((_activeIndex + 1) % availableTools.Count);
        }

        /// <summary>
        /// 停用所有工具。
        /// Deactivates all tools.
        /// </summary>
        public void DeactivateAll()
        {
            foreach (SurgicalToolBase tool in availableTools)
                tool.Deactivate();
            _activeTool = null;
            _activeIndex = -1;
        }

        public SurgicalToolBase ActiveTool => _activeTool;

        public string ActiveToolName => _activeTool != null ? _activeTool.toolName : string.Empty;
    }
}
