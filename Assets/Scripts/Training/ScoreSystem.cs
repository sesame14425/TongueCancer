using System.Collections.Generic;
using UnityEngine;

namespace TongueCancer.Training
{
    /// <summary>
    /// 評分系統（Score System）：
    /// 追蹤訓練員在舌頭病變分類訓練中的診斷準確率、時間與操作品質。
    ///
    /// Tracks trainee diagnostic accuracy, time, and operational quality
    /// during tongue lesion classification training.
    /// </summary>
    public class ScoreSystem : MonoBehaviour
    {
        [Header("評分權重 / Scoring Weights")]
        [Tooltip("診斷正確率權重 Diagnostic accuracy weight")]
        [Range(0f, 1f)]
        public float accuracyWeight = 0.5f;

        [Tooltip("完成時間權重 Completion time weight")]
        [Range(0f, 1f)]
        public float timeWeight = 0.3f;

        [Tooltip("操作品質權重 Operational quality weight")]
        [Range(0f, 1f)]
        public float qualityWeight = 0.2f;

        [Header("時間基準 / Time Benchmark")]
        [Tooltip("理想完成時間（秒）Ideal completion time (seconds)")]
        public float idealCompletionTime = 120f;

        // Session tracking
        private float _sessionStartTime;
        private int _totalLesions;
        private int _correctDiagnoses;
        private float _totalOperationQuality;
        private int _operationCount;
        private List<DiagnosisRecord> _records = new List<DiagnosisRecord>();

        public event System.Action<float> OnScoreUpdated;
        public event System.Action<DiagnosisRecord> OnDiagnosisRecorded;

        [System.Serializable]
        public class DiagnosisRecord
        {
            public string lesionId;
            public LesionType actualType;
            public LesionType diagnosedType;
            public bool isCorrect;
            public float timeToDiagnose;
            public float operationQuality;
        }

        /// <summary>
        /// 開始新訓練場次。
        /// Starts a new training session.
        /// </summary>
        public void StartSession(int totalLesions)
        {
            _totalLesions = totalLesions;
            _correctDiagnoses = 0;
            _totalOperationQuality = 0f;
            _operationCount = 0;
            _records.Clear();
            _sessionStartTime = Time.time;
        }

        /// <summary>
        /// 記錄一次診斷結果。
        /// Records a single diagnosis result.
        /// </summary>
        public void RecordDiagnosis(string lesionId, LesionType actual, LesionType diagnosed, float quality)
        {
            bool correct = actual == diagnosed;
            if (correct) _correctDiagnoses++;
            _operationCount++;
            _totalOperationQuality += Mathf.Clamp01(quality);

            DiagnosisRecord record = new DiagnosisRecord
            {
                lesionId = lesionId,
                actualType = actual,
                diagnosedType = diagnosed,
                isCorrect = correct,
                timeToDiagnose = Time.time - _sessionStartTime,
                operationQuality = quality
            };
            _records.Add(record);
            OnDiagnosisRecorded?.Invoke(record);

            float currentScore = CalculateScore();
            OnScoreUpdated?.Invoke(currentScore);
        }

        /// <summary>
        /// 計算目前場次的加權總分（0–100）。
        /// Calculates the weighted total score for the current session (0–100).
        /// </summary>
        public float CalculateScore()
        {
            float accuracyScore = _totalLesions > 0
                ? (float)_correctDiagnoses / _totalLesions
                : 0f;

            float elapsedTime = Time.time - _sessionStartTime;
            float timeScore = Mathf.Clamp01(1f - (elapsedTime - idealCompletionTime) / idealCompletionTime);

            float qualityScore = _operationCount > 0
                ? _totalOperationQuality / _operationCount
                : 0f;

            float total = (accuracyScore * accuracyWeight
                         + timeScore * timeWeight
                         + qualityScore * qualityWeight) * 100f;

            return Mathf.Clamp(total, 0f, 100f);
        }

        /// <summary>
        /// 產生本場次的摘要報告字串。
        /// Generates a summary report string for the current session.
        /// </summary>
        public string GenerateReport()
        {
            float elapsed = Time.time - _sessionStartTime;
            float finalScore = CalculateScore();
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("=== 訓練報告 / Training Report ===");
            sb.AppendLine($"最終分數 Final Score: {finalScore:F1} / 100");
            sb.AppendLine($"診斷正確率 Accuracy: {_correctDiagnoses}/{_totalLesions}");
            sb.AppendLine($"完成時間 Time: {elapsed:F1}s (理想 Ideal: {idealCompletionTime}s)");
            sb.AppendLine($"操作品質 Quality: {(_operationCount > 0 ? _totalOperationQuality / _operationCount : 0f):P0}");
            sb.AppendLine("--- 詳細記錄 Detailed Records ---");
            foreach (DiagnosisRecord r in _records)
            {
                string resultTag = r.isCorrect ? "✓" : "✗";
                sb.AppendLine($"{resultTag} [{r.lesionId}] 實際: {r.actualType} | 診斷: {r.diagnosedType}");
            }
            return sb.ToString();
        }

        public float CurrentScore => CalculateScore();
        public int CorrectDiagnoses => _correctDiagnoses;
        public int TotalLesions => _totalLesions;
        public IReadOnlyList<DiagnosisRecord> Records => _records;
    }
}
