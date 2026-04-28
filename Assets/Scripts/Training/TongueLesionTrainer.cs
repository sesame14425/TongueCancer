using System.Collections.Generic;
using UnityEngine;
using TongueCancer.TissueDeformation;

namespace TongueCancer.Training
{
    /// <summary>
    /// 舌頭病變訓練器（Tongue Lesion Trainer）：
    /// 管理訓練場景中各個病變區域的生成、顯示與互動邏輯，
    /// 讓學員透過探針或鑷子探索並診斷各病變類型。
    ///
    /// Manages lesion region generation, display, and interaction in the training scene.
    /// Trainees use the probe or forceps to explore and diagnose lesion types.
    /// </summary>
    public class TongueLesionTrainer : MonoBehaviour
    {
        [Header("訓練資料 / Training Data")]
        [Tooltip("本場次的病變資料清單 Lesion data list for this session")]
        public List<LesionData> sessionLesions = new List<LesionData>();

        [Header("組織參照 / Tissue Reference")]
        [Tooltip("舌頭組織控制器 Tongue tissue deformation controller")]
        public TissueDeformationController tongueController;

        [Header("病變標記物件 / Lesion Marker Objects")]
        [Tooltip("病變標記 Prefab（可選）Lesion marker prefab (optional)")]
        public GameObject lesionMarkerPrefab;

        [Tooltip("是否在開始時隱藏病變標記 Hide lesion markers at start")]
        public bool hideMarkersAtStart = true;

        [Header("診斷 UI / Diagnosis UI")]
        [Tooltip("診斷選項面板 Diagnosis options panel")]
        public GameObject diagnosisPanelRoot;

        private int _currentLesionIndex = -1;
        private ScoreSystem _scoreSystem;
        private List<GameObject> _markerInstances = new List<GameObject>();

        public event System.Action<LesionData> OnLesionActivated;
        public event System.Action<string, LesionType, bool> OnDiagnosisSubmitted;

        private void Awake()
        {
            _scoreSystem = GetComponent<ScoreSystem>();
            if (_scoreSystem == null)
                _scoreSystem = gameObject.AddComponent<ScoreSystem>();
        }

        /// <summary>
        /// 開始訓練場次，初始化所有病變標記。
        /// Starts the training session and initialises all lesion markers.
        /// </summary>
        public void StartTraining()
        {
            _scoreSystem.StartSession(sessionLesions.Count);
            ClearMarkers();
            SpawnLesionMarkers();
            _currentLesionIndex = -1;
            Debug.Log("[TongueLesionTrainer] Training session started.");
        }

        /// <summary>
        /// 生成場景中所有病變視覺標記。
        /// Spawns visual markers for all lesions in the scene.
        /// </summary>
        private void SpawnLesionMarkers()
        {
            foreach (LesionData lesion in sessionLesions)
            {
                if (lesionMarkerPrefab == null || tongueController == null) break;

                Vector3 worldPos = tongueController.transform.TransformPoint(lesion.localCenter);
                GameObject marker = Instantiate(lesionMarkerPrefab, worldPos, Quaternion.identity, tongueController.transform);
                marker.name = $"LesionMarker_{lesion.lesionId}";

                Renderer rend = marker.GetComponent<Renderer>();
                if (rend != null)
                {
                    MaterialPropertyBlock block = new MaterialPropertyBlock();
                    rend.GetPropertyBlock(block);
                    block.SetColor("_BaseColor", lesion.lesionColor);
                    rend.SetPropertyBlock(block);
                }

                if (hideMarkersAtStart)
                    marker.SetActive(false);

                _markerInstances.Add(marker);
            }
        }

        private void ClearMarkers()
        {
            foreach (GameObject go in _markerInstances)
            {
                if (go != null) Destroy(go);
            }
            _markerInstances.Clear();
        }

        /// <summary>
        /// 啟動下一個病變讓學員診斷。
        /// Activates the next lesion for the trainee to diagnose.
        /// </summary>
        public bool AdvanceToNextLesion()
        {
            _currentLesionIndex++;
            if (_currentLesionIndex >= sessionLesions.Count)
            {
                Debug.Log("[TongueLesionTrainer] All lesions completed.");
                return false;
            }

            LesionData current = sessionLesions[_currentLesionIndex];
            ActivateLesion(current);
            return true;
        }

        /// <summary>
        /// 啟用指定病變：更新組織變形控制器與視覺標記。
        /// Activates the specified lesion: updates the tissue controller and visual marker.
        /// </summary>
        private void ActivateLesion(LesionData lesion)
        {
            if (tongueController != null)
            {
                tongueController.SetLesionRegion(lesion.localCenter, lesion.radius);
                tongueController.ApplyLesionVisualization(lesion.lesionType != LesionType.Normal);
            }

            // 顯示對應標記 / Show corresponding marker
            if (_currentLesionIndex < _markerInstances.Count && _markerInstances[_currentLesionIndex] != null)
                _markerInstances[_currentLesionIndex].SetActive(true);

            if (diagnosisPanelRoot != null)
                diagnosisPanelRoot.SetActive(true);

            OnLesionActivated?.Invoke(lesion);
            Debug.Log($"[TongueLesionTrainer] Activated lesion: {lesion.lesionId} ({lesion.lesionType})");
        }

        /// <summary>
        /// 提交診斷結果（由 UI 按鈕呼叫）。
        /// Submits the diagnosis result (called by UI buttons).
        /// </summary>
        /// <param name="diagnosedType">學員選擇的診斷類型 Trainee-selected type</param>
        /// <param name="operationQuality">0–1 操作品質分數 Operation quality score</param>
        public void SubmitDiagnosis(LesionType diagnosedType, float operationQuality = 1f)
        {
            if (_currentLesionIndex < 0 || _currentLesionIndex >= sessionLesions.Count) return;

            LesionData current = sessionLesions[_currentLesionIndex];
            bool correct = current.lesionType == diagnosedType;

            _scoreSystem.RecordDiagnosis(current.lesionId, current.lesionType, diagnosedType, operationQuality);
            OnDiagnosisSubmitted?.Invoke(current.lesionId, diagnosedType, correct);

            if (diagnosisPanelRoot != null)
                diagnosisPanelRoot.SetActive(false);

            Debug.Log($"[TongueLesionTrainer] Diagnosis for {current.lesionId}: {diagnosedType} | Correct: {correct}");
        }

        public ScoreSystem ScoreSystem => _scoreSystem;
        public LesionData CurrentLesion => (_currentLesionIndex >= 0 && _currentLesionIndex < sessionLesions.Count)
            ? sessionLesions[_currentLesionIndex] : null;
    }
}
