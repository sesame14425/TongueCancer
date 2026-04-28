using UnityEngine;
using UnityEngine.UI;
using TMPro;
using TongueCancer.Tools;

namespace TongueCancer.Training
{
    /// <summary>
    /// 訓練場景管理器（Training Scene Manager）：
    /// 協調整個訓練流程，包含場景狀態機、UI 更新與場次結束處理。
    ///
    /// Coordinates the full training flow including scene state machine,
    /// UI updates, and session end handling.
    /// </summary>
    public class TrainingSceneManager : MonoBehaviour
    {
        [Header("訓練元件 / Training Components")]
        public TongueLesionTrainer lesionTrainer;
        public ToolInteractionManager toolManager;

        [Header("UI 元件 / UI Components")]
        [Tooltip("場次開始畫面 Session start screen")]
        public GameObject startScreen;

        [Tooltip("訓練進行中 HUD")]
        public GameObject trainingHUD;

        [Tooltip("場次結束畫面 Session end screen")]
        public GameObject endScreen;

        [Tooltip("分數顯示文字 Score display text")]
        public TextMeshProUGUI scoreText;

        [Tooltip("當前病變資訊文字 Current lesion info text")]
        public TextMeshProUGUI lesionInfoText;

        [Tooltip("訓練報告文字 Training report text")]
        public TextMeshProUGUI reportText;

        [Tooltip("進度條 Progress bar")]
        public Slider progressBar;

        [Header("診斷按鈕 / Diagnosis Buttons")]
        [Tooltip("正常組織按鈕 Normal tissue button")]
        public Button btnNormal;

        [Tooltip("鱗狀細胞癌按鈕 SCC button")]
        public Button btnSCC;

        [Tooltip("白斑症按鈕 Leukoplakia button")]
        public Button btnLeukoplakia;

        [Tooltip("紅斑症按鈕 Erythroplakia button")]
        public Button btnErythroplakia;

        [Tooltip("下一個按鈕 Next lesion button")]
        public Button btnNext;

        [Tooltip("重新開始按鈕 Restart button")]
        public Button btnRestart;

        private TrainingState _state = TrainingState.Idle;
        private int _totalLesions;
        private int _completedLesions;

        public enum TrainingState
        {
            Idle,
            Training,
            AwaitingDiagnosis,
            Completed
        }

        private void Start()
        {
            SetupButtonListeners();
            SetState(TrainingState.Idle);
        }

        private void SetupButtonListeners()
        {
            if (btnNormal != null) btnNormal.onClick.AddListener(() => OnDiagnosisButtonClicked(LesionType.Normal));
            if (btnSCC != null) btnSCC.onClick.AddListener(() => OnDiagnosisButtonClicked(LesionType.SquamousCellCarcinoma));
            if (btnLeukoplakia != null) btnLeukoplakia.onClick.AddListener(() => OnDiagnosisButtonClicked(LesionType.Leukoplakia));
            if (btnErythroplakia != null) btnErythroplakia.onClick.AddListener(() => OnDiagnosisButtonClicked(LesionType.Erythroplakia));
            if (btnNext != null) btnNext.onClick.AddListener(OnNextClicked);
            if (btnRestart != null) btnRestart.onClick.AddListener(OnRestartClicked);

            if (lesionTrainer != null)
            {
                lesionTrainer.OnLesionActivated += OnLesionActivated;
                lesionTrainer.OnDiagnosisSubmitted += OnDiagnosisSubmitted;
                lesionTrainer.ScoreSystem.OnScoreUpdated += UpdateScoreDisplay;
            }
        }

        /// <summary>
        /// 切換訓練場景狀態並相應顯示/隱藏 UI。
        /// Switches the training state and shows/hides UI accordingly.
        /// </summary>
        private void SetState(TrainingState newState)
        {
            _state = newState;
            if (startScreen != null) startScreen.SetActive(newState == TrainingState.Idle);
            if (trainingHUD != null) trainingHUD.SetActive(newState == TrainingState.Training || newState == TrainingState.AwaitingDiagnosis);
            if (endScreen != null) endScreen.SetActive(newState == TrainingState.Completed);
        }

        /// <summary>
        /// 開始按鈕點擊（由 UI 呼叫）。
        /// Start button click (called from UI).
        /// </summary>
        public void OnStartClicked()
        {
            if (lesionTrainer == null) return;
            _totalLesions = lesionTrainer.sessionLesions.Count;
            _completedLesions = 0;

            UpdateProgress();
            lesionTrainer.StartTraining();
            SetState(TrainingState.Training);

            bool hasFirst = lesionTrainer.AdvanceToNextLesion();
            if (!hasFirst) SetState(TrainingState.Completed);
        }

        private void OnLesionActivated(LesionData lesion)
        {
            SetState(TrainingState.AwaitingDiagnosis);
            if (lesionInfoText != null)
                lesionInfoText.text = $"病變 {lesion.lesionId}\n{lesion.clinicalDescription}";
        }

        private void OnDiagnosisButtonClicked(LesionType type)
        {
            if (_state != TrainingState.AwaitingDiagnosis) return;
            lesionTrainer.SubmitDiagnosis(type, 1f);
        }

        private void OnDiagnosisSubmitted(string id, LesionType diagnosed, bool correct)
        {
            _completedLesions++;
            UpdateProgress();
            SetState(TrainingState.Training);
        }

        private void OnNextClicked()
        {
            bool hasNext = lesionTrainer.AdvanceToNextLesion();
            if (!hasNext)
            {
                SetState(TrainingState.Completed);
                ShowReport();
            }
        }

        private void OnRestartClicked()
        {
            SetState(TrainingState.Idle);
        }

        private void UpdateScoreDisplay(float score)
        {
            if (scoreText != null)
                scoreText.text = $"分數 Score: {score:F1}";
        }

        private void UpdateProgress()
        {
            if (progressBar != null && _totalLesions > 0)
                progressBar.value = (float)_completedLesions / _totalLesions;
        }

        private void ShowReport()
        {
            if (reportText != null && lesionTrainer != null)
                reportText.text = lesionTrainer.ScoreSystem.GenerateReport();
        }
    }
}
