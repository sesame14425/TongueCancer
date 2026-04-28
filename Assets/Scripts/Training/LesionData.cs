using UnityEngine;

namespace TongueCancer.Training
{
    /// <summary>
    /// 病變資料模型（Lesion Data Model）：
    /// 描述舌頭病變區域的類型、位置、大小與特徵。
    ///
    /// Data model describing a tongue lesion region's type, position, size and characteristics.
    /// </summary>
    [System.Serializable]
    public class LesionData
    {
        [Tooltip("病變 ID Lesion ID")]
        public string lesionId;

        [Tooltip("病變類型 Lesion type")]
        public LesionType lesionType;

        [Tooltip("病變中心（本地座標）Lesion center (local space)")]
        public Vector3 localCenter;

        [Tooltip("病變半徑（公尺）Lesion radius (metres)")]
        [Range(0.001f, 0.1f)]
        public float radius = 0.01f;

        [Tooltip("組織硬度（1 = 正常，>1 = 較硬）Tissue hardness (1 = normal, >1 = harder)")]
        [Range(0.5f, 5f)]
        public float hardnessRatio = 1f;

        [Tooltip("顏色偏移（與正常組織的差異）Color deviation from normal tissue")]
        public Color lesionColor = new Color(0.7f, 0.2f, 0.2f, 1f);

        [Tooltip("病變嚴重程度（0–1）Lesion severity (0–1)")]
        [Range(0f, 1f)]
        public float severity = 0.5f;

        [Tooltip("臨床描述 Clinical description")]
        [TextArea(2, 5)]
        public string clinicalDescription;

        /// <summary>
        /// 建立一個正常組織資料（作為對照組）。
        /// Creates a normal tissue entry (as a control).
        /// </summary>
        public static LesionData CreateNormal(string id, Vector3 center)
        {
            return new LesionData
            {
                lesionId = id,
                lesionType = LesionType.Normal,
                localCenter = center,
                radius = 0.01f,
                hardnessRatio = 1f,
                lesionColor = new Color(0.95f, 0.75f, 0.75f, 1f),
                severity = 0f,
                clinicalDescription = "正常黏膜組織 / Normal mucosal tissue"
            };
        }
    }

    /// <summary>
    /// 舌頭病變類型列舉。
    /// Enumeration of tongue lesion types.
    /// </summary>
    public enum LesionType
    {
        /// <summary>正常組織 Normal tissue</summary>
        Normal,
        /// <summary>鱗狀細胞癌 Squamous cell carcinoma</summary>
        SquamousCellCarcinoma,
        /// <summary>白斑症 Leukoplakia</summary>
        Leukoplakia,
        /// <summary>紅斑症 Erythroplakia</summary>
        Erythroplakia,
        /// <summary>口腔纖維化 Oral submucous fibrosis</summary>
        OralSubmucosisFibrosis,
        /// <summary>潰瘍 Ulcer</summary>
        Ulcer,
        /// <summary>乳突瘤 Papilloma</summary>
        Papilloma
    }
}
