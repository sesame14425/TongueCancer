# TongueCancer — 虛實整合舌頭病變組織區分訓練系統

**Unity 2021 Virtual-Physical Integrated Tongue Lesion Tissue Classification Training System**

## 專案概述 / Project Overview

本專案為使用 **Unity 2021** 開發的醫學教育訓練平台，提供虛擬舌頭病變組織的互動式手術訓練，
結合虛實整合（Virtual-Physical Integration）技術，讓學員透過虛擬手術工具探索並分類不同類型的舌頭病變組織。

This project is a medical education training platform developed with **Unity 2021**, providing interactive surgical training for virtual tongue lesion tissue classification. It combines virtual-physical integration technology, allowing trainees to explore and classify different types of tongue lesion tissue using virtual surgical tools.

---

## 功能特色 / Key Features

- 🧠 **虛擬組織變形演算法** — 質量彈簧系統（Mass-Spring）+ 軟體模擬器（Soft Body Simulator）
- 🔧 **手術操縱工具** — 探針（Probe）、鑷子（Forceps）與工具管理器
- 🏥 **病變分類訓練** — 支援 7 種病變類型的互動式診斷訓練
- 🎮 **虛實整合** — XR 控制器追蹤 + 觸覺回饋裝置支援
- 📊 **評分系統** — 診斷準確率、完成時間與操作品質加權評分

---

## 腳本結構 / Script Structure

```
Assets/
├── Scripts/
│   ├── TissueDeformation/          # 虛擬組織變形演算法
│   │   ├── MassSpringDeformation.cs      # 質量彈簧系統
│   │   ├── SoftBodySimulator.cs          # 軟體模擬器（含病變硬度差異）
│   │   └── TissueDeformationController.cs # 統一變形控制介面
│   │
│   ├── Tools/                      # 手術操縱工具
│   │   ├── SurgicalToolBase.cs           # 工具基礎類別
│   │   ├── ProbeTool.cs                  # 探針工具
│   │   ├── ForcepsTool.cs                # 鑷子工具
│   │   └── ToolInteractionManager.cs     # 工具選擇與管理
│   │
│   ├── Training/                   # 訓練系統
│   │   ├── LesionData.cs                 # 病變資料模型（7 種病變類型）
│   │   ├── ScoreSystem.cs                # 評分與報告系統
│   │   ├── TongueLesionTrainer.cs        # 病變訓練核心邏輯
│   │   └── TrainingSceneManager.cs       # 訓練場景狀態機與 UI 管理
│   │
│   └── VRIntegration/              # 虛實整合
│       ├── VirtualPhysicalBridge.cs      # XR 追蹤映射至虛擬工具
│       └── HapticFeedbackController.cs   # 觸覺回饋控制器
│
├── Prefabs/                        # 預製件（Prefabs）
├── Scenes/                         # 場景（Scenes）
└── Materials/                      # 材質（Materials）
```

---

## 病變類型 / Lesion Types

| 枚舉值 | 中文名稱 | 英文名稱 |
|--------|----------|----------|
| `Normal` | 正常組織 | Normal tissue |
| `SquamousCellCarcinoma` | 鱗狀細胞癌 | Squamous Cell Carcinoma |
| `Leukoplakia` | 白斑症 | Leukoplakia |
| `Erythroplakia` | 紅斑症 | Erythroplakia |
| `OralSubmucosisFibrosis` | 口腔纖維化 | Oral Submucous Fibrosis |
| `Ulcer` | 潰瘍 | Ulcer |
| `Papilloma` | 乳突瘤 | Papilloma |

---

## 組織變形演算法 / Tissue Deformation Algorithms

### 質量彈簧系統（Mass-Spring System）
`MassSpringDeformation.cs` 將每個網格頂點視為質量節點，以彈簧連接鄰近節點，模擬彈性組織行為：
- 可調整**彈簧剛度**（Spring Stiffness）、**阻尼**（Damping）與**節點質量**（Node Mass）
- 支援彈性恢復（Elastic Recovery），接觸後自動回彈
- `ApplyForceAtPosition()` — 在世界座標位置施加力

### 軟體模擬器（Soft Body Simulator）
`SoftBodySimulator.cs` 模擬生物組織的壓縮變形：
- 依局部硬度（含病變區域硬度差異）計算形變量
- 自動更新 `MeshCollider` 確保物理碰撞一致
- `ApplyContactDeformation()` — 根據施力大小產生壓縮凹陷

### 整合控制器（Deformation Controller）
`TissueDeformationController.cs` 提供統一 API：
- 三種模式：`MassSpring` / `SoftBody` / `Combined`
- 病變區域視覺化（顏色疊加）

---

## 手術工具使用方式 / Tool Usage

### 探針工具（Probe Tool）
- 按壓組織探測硬度，視覺壓力指示燈顯示施力大小
- 觸覺回饋強度反映組織硬度（病變 vs. 正常）

### 鑷子工具（Forceps Tool）
- 模擬夾取、牽引組織
- 透過 `SetJawAngle(0–1)` 控制開合程度
- `RetractTissue()` 沿指定方向牽拉組織

### 工具管理器（Tool Interaction Manager）
- `SelectTool(index)` / `SelectToolByName(name)` — 切換工具
- `NextTool()` — 循環切換（可綁定鍵盤 Tab 鍵）

---

## 訓練場景設置 / Training Scene Setup

1. 建立一個 GameObject 附加 `TissueDeformationController`、`MassSpringDeformation`、`SoftBodySimulator`
2. 在 Inspector 設定 `sessionLesions` 清單（`TongueLesionTrainer`）
3. 連接 `ToolInteractionManager` 並加入探針與鑷子
4. 設定 XR 控制器的 `VirtualPhysicalBridge`
5. 透過 UI 按鈕呼叫 `TrainingSceneManager.OnStartClicked()` 開始訓練

---

## 系統需求 / System Requirements

- **Unity 2021.3 LTS** 或更新版本
- **XR Plugin Management**（VR/AR 功能需要）
- **TextMeshPro**（UI 文字元件）
- 選用：OpenXR / Oculus XR Plugin（觸覺回饋）

---

## 授權 / License

本專案僅供學術研究與醫學教育用途。
This project is intended for academic research and medical education purposes only.
