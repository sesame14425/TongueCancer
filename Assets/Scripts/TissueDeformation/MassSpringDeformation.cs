using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;

namespace TongueCancer.TissueDeformation
{
    /// <summary>
    /// Shape Matching 變形引擎，使用 GPU Compute Shader 實現穩定的網格變形。
    /// Shape Matching deformation engine using GPU Compute Shader for stable mesh deformation.
    /// 流程：vertices_merge → Overlapping K-Means → per-cluster TransformGen → GPU Dispatch
    /// Pipeline: vertices_merge → Overlapping K-Means → per-cluster TransformGen → GPU Dispatch
    /// </summary>
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshCollider))]
    public class MassSpringDeformation : MonoBehaviour
    {
        [Header("Shape Matching 參數 / Parameters")]
        [Tooltip("GPU Shape Matching Compute Shader（ShapeMatching kernel）")]
        public ComputeShader DeformAlgorithm;

        [Range(0f, 1f)]
        [Tooltip("剛性比例：Beta=1 純線性，Beta=0 純旋轉 / Rigidity: 1=linear, 0=rotation")]
        public float Beta = 0.5f;

        [Range(0f, 1f)]
        [Tooltip("速度阻尼係數 / Velocity damping coefficient")]
        public float Damp = 0.005f;

        [Tooltip("總質量 / Total mass")]
        public float Mass = 1f;

        [Min(1)]
        [Tooltip("K-Means 群數 / Number of K-Means clusters")]
        public int cluster_num = 8;

        [Range(0f, 0.99f)]
        [Tooltip("Overlapping K-Means 重疊比例 / Overlap ratio for Overlapping K-Means")]
        public float Overlap = 0.1f;

        [Tooltip("啟用二次變形映射 / Enable quadratic deformation mapping")]
        public bool quadratic = false;

        [Tooltip("二次映射係數（9個）/ Quadratic mapping coefficients (9 values)")]
        public double[] coefs = new double[9];

        [Header("組織 Alpha 材質差異 / Tissue Material Alpha")]
        [Tooltip("正常組織 Alpha（較小＝較軟）/ Normal tissue alpha (smaller = softer)")]
        public float AlphaNormal = 0.05f;

        [Tooltip("腫瘤組織 Alpha（較大＝較硬）/ Tumour tissue alpha (larger = harder)")]
        public float AlphaTumor = 0.3f;

        [Header("接觸參數 / Contact Parameters")]
        public float pressKp = 60f;
        public float pressKd = 10f;
        public float contactRadius = 1.2f;
        [Range(1f, 6f)] public float contactFalloffPower = 2f;
        public float contactDecaySpeed = 3f;
        public float minPenetration = 0.0002f;

        [Header("穩定控制 / Stability")]
        public float maxDvPerStep = 0.2f;
        public float maxTotalDvPerFrame = 4.0f;
        public float globalVelocityDamping = 0.992f;
        public float velocityClamp = 2.5f;

        [Header("接觸位置修正 / Position Projection")]
        public bool enablePositionProjection = true;
        public float maxPositionCorrectionPerStep = 0.008f;

        [Tooltip("MeshCollider 更新間隔幀數 / Mesh collider refresh interval (frames)")]
        public int colliderRefreshInterval = 4;

        // 外部接觸（由 TissueDeformationController 設定）
        // External contact set by TissueDeformationController
        [HideInInspector] public Vector3 externalPos;
        [HideInInspector] public float externalRadius;
        [HideInInspector] public Vector3 externalVelocity;

        // ── Compute Shader 緩衝區 / Compute Shader buffers ──────────────────────
        private double[] _transformArray;
        private ComputeBuffer _transformBuffer;
        private double[] _centerArray;
        private ComputeBuffer _centerBuffer;
        private double[] _currentPosArray;
        private ComputeBuffer _currentPosBuffer;
        private double[] _velocityArray;
        private double[] _addedVelocityArray;
        private float[] _contactWeights;
        private ComputeBuffer _velocityBuffer;
        private double[] _originalPosArray;
        private ComputeBuffer _originalPosBuffer;
        private int[] _pointsHeadTailArray;
        private ComputeBuffer _pointsHeadTailBuffer;
        private int[] _clusterIdArray;
        private ComputeBuffer _clusterIdBuffer;
        private float[] _alphaArray;
        private ComputeBuffer _alphaBuffer;
        private int[] _clusterHeadTailArray;
        private int[] _pointsIdArray;
        private int _shapeMatchingKernel;
        private int _dim;

        // ── 網格狀態 / Mesh state ─────────────────────────────────────────────────
        private Mesh _mesh;
        private Vector3[] _vertices;
        private MeshCollider _meshCollider;
        private int _verticesNum;
        private int _mergedVerticesNum;
        internal int[] mergedVerticesTable;

        private Vector<double>[] _originalPosition;
        internal Vector<double>[] CurrentPosition;
        private Vector<double> _currentCenter;
        private Vector<double> _lastCenter;

        private Matrix<double>[] _transform;
        private Vector<double>[] _originalCenterPos;
        private Vector<double>[] _currentCenterPos;
        private double[] _mass;

        // ── EVD 暫存 / EVD scratch ────────────────────────────────────────────────
        private Matrix<double> _apq;
        private Matrix<double>[] _aqq;
        private Matrix<double> _aMatrix;
        private Matrix<double> _rotation;
        private Matrix<double> _stretching;
        private Evd<double> _evdStorage;
        private Matrix<double> _eigenValue;
        private Matrix<double> _eigenVectors;
        private Matrix<double> _apqSquare;
        private Matrix<double> _padding;

        private int _colliderRefreshCounter;

        // ── 力回饋輸出（供 TissueDeformationController 讀取）/ Haptic output ──────
        public double PendingHapticForce { get; private set; }
        public Vector3 PendingReactionForce { get; private set; }

        // ───────────────────────────────────────────────────────────────[...]

        private void Awake()
        {
            Init();
        }

        private void FixedUpdate()
        {
            ApplyExternalContact();
            FlushVelocityUpdates();

            _lastCenter = _currentCenter;
            _currentCenter = Operation.average(CurrentPosition);

            for (int i = 0; i < cluster_num; i++) TransformGen(i);
            CenterUpdate();
            TransformSet();

            _currentPosBuffer.GetData(_currentPosArray);
            Operation.DoubleArray2DoubleVectorArray(ref CurrentPosition, _currentPosArray);

            // 衰減力回饋 / Decay haptic output
            PendingHapticForce *= 0.75;
            if (PendingHapticForce < 1e-6) PendingHapticForce = 0.0;
            PendingReactionForce = Vector3.Lerp(PendingReactionForce, Vector3.zero, 0.35f);
            if (PendingReactionForce.sqrMagnitude < 1e-8f) PendingReactionForce = Vector3.zero;
        }

        private void Update()
        {
            MeshUpdate();
        }

        private void OnDisable()
        {
            ReleaseBuffers();
        }

        // ───────────────────────────────────────────────────────────────[...]
        // 初始化 / Initialisation
        // ───────────────────────────────────────────────────────────────[...]

        private void Init()
        {
            _mesh = GetComponent<MeshFilter>().mesh;
            _meshCollider = GetComponent<MeshCollider>();
            _vertices = _mesh.vertices;
            _verticesNum = _vertices.Length;

            // ① vertices_merge：去除重複頂點，建立合併映射表
            //    Remove duplicate vertices and build the mapping table
            var mergedList = new List<Vector3>();
            mergedVerticesTable = new int[_verticesNum];
            VerticesMerge(_vertices, mergedList, mergedVerticesTable);
            _mergedVerticesNum = mergedList.Count;

            _originalPosition = Operation.List2Array(mergedList);
            CurrentPosition   = Operation.List2Array(mergedList);

            _currentCenter = Operation.average(CurrentPosition);
            _lastCenter    = _currentCenter;

            _dim = quadratic ? 9 : 3;
            if (quadratic) _padding = CreateMatrix.Dense<double>(3, 6, 0);

            _transform          = new Matrix<double>[cluster_num];
            _originalCenterPos  = new Vector<double>[cluster_num];
            _currentCenterPos   = new Vector<double>[cluster_num];

            // ② Overlapping K-Means 分群
            OverlappingKMeans(cluster_num, Overlap, 30);

            if (quadratic)
            {
                _originalPosition  = Operation.QuadraticMapping(_originalPosition, coefs);
                _originalCenterPos = Operation.QuadraticMapping(_originalCenterPos, coefs);
            }

            // ③ 用 vertex color 決定每個合併頂點的 mass 與 alpha（材料差異）
            //    Use vertex colour to assign per-merged-vertex mass and alpha (material difference)
            _mass       = new double[_mergedVerticesNum];
            _alphaArray = new float[_mergedVerticesNum];
            Color[] vColors = _mesh.colors;
            float baseMass = (Mass <= 0f) ? 1.0f : (Mass / _mergedVerticesNum);

            for (int i = 0; i < _mergedVerticesNum; i++)
            {
                int origIdx = Array.IndexOf(mergedVerticesTable, i);
                Color c = (vColors != null && vColors.Length > origIdx && origIdx >= 0)
                          ? vColors[origIdx] : Color.white;

                if (c.r > 0.5f) // 腫瘤區域 / Tumour region
                {
                    _mass[i]       = baseMass * 2.0;
                    _alphaArray[i] = AlphaTumor;
                }
                else             // 正常組織 / Normal tissue
                {
                    _mass[i]       = baseMass;
                    _alphaArray[i] = AlphaNormal;
                }
                if (double.IsNaN(_mass[i]) || _mass[i] <= 0) _mass[i] = 1.0;
            }

            // ④ 預先計算 Aqq^-1
            _apq       = CreateMatrix.Dense<double>(3, _dim, 0);
            _aqq       = Operation.Aqq_Init(_originalPosition, _mass, _originalCenterPos,
                                            _clusterHeadTailArray, _pointsIdArray, cluster_num, _dim);
            _stretching = CreateMatrix.Dense<double>(3, 3, 0);
            _apqSquare  = CreateMatrix.Dense<double>(3, 3, 0);

            _addedVelocityArray = new double[3 * _mergedVerticesNum];
            _contactWeights     = new float[_mergedVerticesNum];

            ShaderInit();
        }

        // ───────────────────────────────────────────────────────────────[...]
        // 接觸求解 / Contact solve
        // ───────────────────────────────────────────────────────────────[...]

        private void ApplyExternalContact()
        {
            if (CurrentPosition == null || _addedVelocityArray == null) return;
            if (externalRadius <= 0f) return;

            _velocityBuffer.GetData(_velocityArray);

            float influenceR     = Mathf.Max(contactRadius, externalRadius * 1.25f);
            float totalW         = 0f;
            var   wCache         = new float[_mergedVerticesNum];
            var   nCache         = new Vector3[_mergedVerticesNum];
            var   penCache       = new float[_mergedVerticesNum];

            for (int v = 0; v < _mergedVerticesNum; v++)
            {
                Vector<double> p = CurrentPosition[v];
                Vector3 qWorld = transform.TransformPoint(
                    new Vector3((float)p[0], (float)p[1], (float)p[2]));
                Vector3 dir = qWorld - externalPos;
                float d = dir.magnitude;
                if (d < 1e-6f) continue;

                float penetration = externalRadius - d;
                if (penetration <= minPenetration) continue;

                float bestW = 1f - Mathf.Clamp01(
                    Mathf.Abs(d - externalRadius) / Mathf.Max(1e-4f, influenceR));
                if (bestW <= 0f) continue;

                wCache[v]   = bestW;
                nCache[v]   = dir / d;
                penCache[v] = penetration;
                totalW     += bestW;
            }

            if (totalW <= 1e-6f) return;

            float remainBudget = maxTotalDvPerFrame;
            for (int v = 0; v < _mergedVerticesNum; v++)
            {
                float w = wCache[v];
                if (w <= 0f) continue;

                Vector3 nPush = nCache[v];
                float   pen   = penCache[v];

                Vector3 vLocal = new Vector3(
                    (float)_velocityArray[3 * v + 0],
                    (float)_velocityArray[3 * v + 1],
                    (float)_velocityArray[3 * v + 2]);
                Vector3 vWorld = transform.TransformDirection(vLocal);
                float   vn     = Vector3.Dot(vWorld - externalVelocity, nPush);

                float shapedW = Mathf.Pow(w, Mathf.Max(1f, contactFalloffPower));
                float dv = (pressKp * pen - pressKd * vn) * shapedW * Time.fixedDeltaTime;
                dv = Mathf.Clamp(dv, 0f, maxDvPerStep);

                float dvBudget = remainBudget * (w / totalW);
                dv = Mathf.Min(dv, dvBudget);
                if (dv <= 0f) continue;

                Vector3 vAddWorld = nPush * dv;
                Vector3 vAddLocal = transform.InverseTransformDirection(vAddWorld);
                float   projMag   = Mathf.Min(maxPositionCorrectionPerStep, pen * 0.6f) * shapedW;
                ApplyPositionCorrection(v, nPush * projMag);

                _addedVelocityArray[3 * v + 0] += vAddLocal.x;
                _addedVelocityArray[3 * v + 1] += vAddLocal.y;
                _addedVelocityArray[3 * v + 2] += vAddLocal.z;

                if (shapedW > _contactWeights[v]) _contactWeights[v] = shapedW;

                double h = (pressKp * pen + Math.Max(0.0, (double)(-vn)) * pressKd) * shapedW * 4.0;
                PendingHapticForce  += h;
                PendingReactionForce += (-nPush) * (float)h;

                remainBudget -= dv;
                if (remainBudget <= 1e-6f) break;
            }
        }

        private void ApplyPositionCorrection(int v, Vector3 worldDelta)
        {
            if (!enablePositionProjection) return;
            if (worldDelta.sqrMagnitude <= 1e-12f) return;
            Vector3 localDelta = transform.InverseTransformDirection(worldDelta);
            CurrentPosition[v][0] += localDelta.x;
            CurrentPosition[v][1] += localDelta.y;
            CurrentPosition[v][2] += localDelta.z;
        }

        private void FlushVelocityUpdates()
        {
            bool needsUpdate = false;
            for (int i = 0; i < _addedVelocityArray.Length; i++)
                if (_addedVelocityArray[i] != 0.0) { needsUpdate = true; break; }
            if (!needsUpdate)
                for (int i = 0; i < _contactWeights.Length; i++)
                    if (_contactWeights[i] > 0f) { needsUpdate = true; break; }
            if (!needsUpdate) return;

            _velocityBuffer.GetData(_velocityArray);
            for (int k = 0; k < _mergedVerticesNum; k++)
            {
                _velocityArray[3 * k + 0] += _addedVelocityArray[3 * k + 0];
                _velocityArray[3 * k + 1] += _addedVelocityArray[3 * k + 1];
                _velocityArray[3 * k + 2] += _addedVelocityArray[3 * k + 2];

                _velocityArray[3 * k + 0] *= globalVelocityDamping;
                _velocityArray[3 * k + 1] *= globalVelocityDamping;
                _velocityArray[3 * k + 2] *= globalVelocityDamping;

                _velocityArray[3 * k + 0] = Math.Max(-velocityClamp,
                    Math.Min(velocityClamp, _velocityArray[3 * k + 0]));
                _velocityArray[3 * k + 1] = Math.Max(-velocityClamp,
                    Math.Min(velocityClamp, _velocityArray[3 * k + 1]));
                _velocityArray[3 * k + 2] = Math.Max(-velocityClamp,
                    Math.Min(velocityClamp, _velocityArray[3 * k + 2]));

                if (_contactWeights[k] > 0f)
                    _contactWeights[k] = Mathf.MoveTowards(_contactWeights[k], 0f,
                        Time.fixedDeltaTime * contactDecaySpeed);

                _addedVelocityArray[3 * k + 0] = 0.0;
                _addedVelocityArray[3 * k + 1] = 0.0;
                _addedVelocityArray[3 * k + 2] = 0.0;
            }
            _velocityBuffer.SetData(_velocityArray);
        }

        // ───────────────────────────────────────────────────────────────[...]
        // 網格更新 / Mesh update
        // ───────────────────────────────────────────────────────────────[...]

        private void MeshUpdate()
        {
            for (int i = 0; i < _verticesNum; i++)
            {
                Vector<double> pos = CurrentPosition[mergedVerticesTable[i]];
                _vertices[i] = new Vector3((float)pos.At(0), (float)pos.At(1), (float)pos.At(2));
            }
            _mesh.SetVertices(_vertices);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();

            _colliderRefreshCounter++;
            if (_colliderRefreshCounter >= colliderRefreshInterval)
            {
                _colliderRefreshCounter = 0;
                _meshCollider.sharedMesh = null;
                _meshCollider.sharedMesh = _mesh;
            }
        }

        // ───────────────────────────────────────────────────────────────[...]
        // GPU 緩衝區初始化 / GPU buffer initialisation
        // ───────────────────────────────────────────────────────────────[...]

        private void ShaderInit()
        {
            _shapeMatchingKernel = DeformAlgorithm.FindKernel("ShapeMatching");

            _transformArray    = new double[_dim * 3 * cluster_num];
            _centerArray       = new double[(3 + _dim) * cluster_num];
            _currentPosArray   = new double[3 * _mergedVerticesNum];
            _originalPosArray  = new double[_dim * _mergedVerticesNum];
            _velocityArray     = Operation.DoubleArrayInit(3 * _mergedVerticesNum, 0.0);

            _transformBuffer     = new ComputeBuffer(_dim * 3 * cluster_num, sizeof(double));
            _centerBuffer        = new ComputeBuffer((3 + _dim) * cluster_num, sizeof(double));
            _currentPosBuffer    = Operation.PositionBufferInit(ref _currentPosArray, CurrentPosition);
            _originalPosBuffer   = Operation.PositionBufferInit(ref _originalPosArray, _originalPosition);
            _velocityBuffer      = new ComputeBuffer(3 * _mergedVerticesNum, sizeof(double));
            _pointsHeadTailBuffer = new ComputeBuffer(2 * _mergedVerticesNum, sizeof(int));
            _clusterIdBuffer     = new ComputeBuffer(_clusterIdArray.Length, sizeof(int));
            _alphaBuffer         = new ComputeBuffer(_mergedVerticesNum, sizeof(float));
            _alphaBuffer.SetData(_alphaArray);

            // 初始化 transform 與 center 陣列 / Initialise transform and center arrays
            for (int i = 0; i < cluster_num; i++)
            {
                for (int a = 0; a < _dim * 3; a++)
                    // stride = _dim * 3 per cluster; identity entries are at (0,0),(1,1),(2,2)
                    _transformArray[i * (_dim * 3) + a] =
                        (a == 0 || a == _dim + 1 || a == 2 * _dim + 2) ? 1.0 : 0.0;

                for (int b = 0; b < _dim; b++)
                {
                    _centerArray[i * (3 + _dim) + b] = _originalCenterPos[i].At(b);
                    if (b < 3)
                        _centerArray[i * (3 + _dim) + b + _dim] = _currentCenterPos[i].At(b);
                }
            }

            _transformBuffer.SetData(_transformArray);
            _centerBuffer.SetData(_centerArray);
            _velocityBuffer.SetData(_velocityArray);
            _pointsHeadTailBuffer.SetData(_pointsHeadTailArray);
            _clusterIdBuffer.SetData(_clusterIdArray);

            DeformAlgorithm.SetInt("dim", _dim);
            DeformAlgorithm.SetInt("PointsNum", _mergedVerticesNum);
            DeformAlgorithm.SetFloat("DeltaTime", Time.fixedDeltaTime);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "Alphas",       _alphaBuffer);
            DeformAlgorithm.SetFloat("damp", Damp);
            DeformAlgorithm.SetFloat("mass", Mass);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "Transform",    _transformBuffer);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "Center",       _centerBuffer);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "CurrentPos",   _currentPosBuffer);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "OriginalPos",  _originalPosBuffer);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "Velocity",     _velocityBuffer);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "PointsHeadTail", _pointsHeadTailBuffer);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "ClusterIDs",   _clusterIdBuffer);
        }

        private void ReleaseBuffers()
        {
            _transformBuffer?.Release();      _transformBuffer     = null;
            _centerBuffer?.Release();         _centerBuffer        = null;
            _currentPosBuffer?.Release();     _currentPosBuffer    = null;
            _originalPosBuffer?.Release();    _originalPosBuffer   = null;
            _velocityBuffer?.Release();       _velocityBuffer      = null;
            _pointsHeadTailBuffer?.Release(); _pointsHeadTailBuffer = null;
            _clusterIdBuffer?.Release();      _clusterIdBuffer     = null;
            _alphaBuffer?.Release();          _alphaBuffer         = null;
        }

        // ───────────────────────────────────────────────────────────────[...]
        // vertices_merge：去除重複頂點
        // vertices_merge: remove duplicate vertices
        // ───────────────────────────────────────────────────────────────[...]

        private void VerticesMerge(Vector3[] vertices, List<Vector3> merged, int[] table)
        {
            merged.Add(vertices[0]);
            table[0] = 0;
            for (int i = 1; i < vertices.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < merged.Count; j++)
                {
                    if ((vertices[i] - merged[j]).magnitude < 0.0001f)
                    {
                        table[i] = j;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    table[i] = merged.Count;
                    merged.Add(vertices[i]);
                }
            }
        }

        // ───────────────────────────────────────────────────────────────[...]
        // Overlapping K-Means
        // ───────────────────────────────────────────────────────────────[...]

        private void OverlappingKMeans(int seedNum, float overlap, int maxIter)
        {
            _pointsHeadTailArray  = new int[2 * _mergedVerticesNum];
            _clusterHeadTailArray = new int[2 * cluster_num];

            var teams    = new List<(int id, Vector<double> position)>[seedNum];
            var seeds    = SeedGen(seedNum);
            var newSeeds = Operation.HardCopyArray(seeds);
            var distMatrix = new (int id, double distance)[seedNum][];
            int iter = 0;

            do
            {
                Clustering(newSeeds, CurrentPosition, teams);
                SeedReset(teams, seeds, newSeeds);
                iter++;
                if (iter == maxIter) Debug.Log("KMeans reach max iteration!");
            }
            while (!newSeeds.SequenceEqual(seeds) && iter < maxIter);

            if (overlap != 0f)
            {
                for (int i = 0; i < seedNum; i++)
                {
                    distMatrix[i] = new (int id, double distance)[_mergedVerticesNum];
                    for (int j = 0; j < _mergedVerticesNum; j++)
                        distMatrix[i][j] = (j, (CurrentPosition[j] - newSeeds[i]).L2Norm());
                    Operation.Sort(distMatrix[i], 0, _mergedVerticesNum - 1);

                    int increase     = (int)Math.Floor(overlap * teams[i].Count / (1f - overlap));
                    int addedNum     = 0;
                    int matrixIndex  = 0;
                    if (teams[i].Count == 0)
                        Debug.LogFormat("Cluster {0} doesn't have any point!", i);

                    while (addedNum < increase && matrixIndex < _mergedVerticesNum)
                    {
                        int ptIdx = distMatrix[i][matrixIndex].id;
                        if (!teams[i].Exists(x => x.id == ptIdx))
                        {
                            teams[i].Add((ptIdx, CurrentPosition[ptIdx].Clone()));
                            addedNum++;
                        }
                        matrixIndex++;
                    }
                    if (matrixIndex == _mergedVerticesNum)
                        Debug.LogFormat("Cluster {0} has all points!", i);
                }
            }

            int len = 0, head = 0, tail = 0;
            var pointsId = new List<int>();
            var clusterIdsPerVertex = new List<int>[_mergedVerticesNum];
            var clusterIdList       = new List<int>();
            for (int i = 0; i < _mergedVerticesNum; i++)
                clusterIdsPerVertex[i] = new List<int>();

            for (int i = 0; i < seedNum; i++)
            {
                var team = teams[i];
                len  = team.Count;
                tail = head + len - 1;
                _clusterHeadTailArray[i * 2]     = head;
                _clusterHeadTailArray[i * 2 + 1] = tail;
                head = tail + 1;
                for (int j = 0; j < len; j++)
                {
                    pointsId.Add(team[j].Item1);
                    clusterIdsPerVertex[team[j].Item1].Add(i);
                }
            }

            len = 0; head = 0; tail = 0;
            for (int i = 0; i < _mergedVerticesNum; i++)
            {
                len  = clusterIdsPerVertex[i].Count;
                clusterIdList = clusterIdList.Concat(clusterIdsPerVertex[i]).ToList();
                tail = head + len - 1;
                _pointsHeadTailArray[i * 2]     = head;
                _pointsHeadTailArray[i * 2 + 1] = tail;
                head = tail + 1;
            }
            _pointsIdArray  = pointsId.ToArray();
            _clusterIdArray = clusterIdList.ToArray();

            head = 0; tail = 0;
            for (int i = 0; i < seedNum; i++)
            {
                head = _clusterHeadTailArray[i * 2];
                tail = _clusterHeadTailArray[i * 2 + 1];
                _transform[i]         = CreateMatrix.DenseIdentity<double>(3, _dim);
                _originalCenterPos[i] = Operation.average(_originalPosition, _pointsIdArray, head, tail);
                _currentCenterPos[i]  = Operation.average(CurrentPosition,   _pointsIdArray, head, tail);
            }
        }

        private void Clustering(
            in Vector<double>[] seed,
            in Vector<double>[] position,
            List<(int id, Vector<double> position)>[] teams)
        {
            int seedNum  = seed.Length;
            int pointNum = position.Length;
            Array.Clear(teams, 0, seedNum);
            for (int i = 0; i < seedNum; i++)
                teams[i] = new List<(int id, Vector<double> position)>();

            for (int i = 0; i < pointNum; i++)
            {
                double minDis     = double.MaxValue;
                int    nearestSeed = 0;
                for (int j = 0; j < seedNum; j++)
                {
                    double d = (position[i] - seed[j]).L2Norm();
                    if (d < minDis) { minDis = d; nearestSeed = j; }
                }
                teams[nearestSeed].Add((i, position[i].Clone()));
            }
        }

        private void SeedReset(
            List<(int id, Vector<double> position)>[] teams,
            Vector<double>[] seeds,
            Vector<double>[] newSeeds)
        {
            for (int i = 0; i < seeds.Length; i++)
                seeds[i] = newSeeds[i].Clone();

            for (int i = 0; i < teams.Length; i++)
            {
                if (teams[i] == null || teams[i].Count == 0) continue;
                Vector<double> center = CreateVector.Dense<double>(new double[] { 0, 0, 0 });
                foreach (var point in teams[i]) center += point.position;
                center /= teams[i].Count;
                newSeeds[i] = center;
            }
        }

        private Vector<double>[] SeedGen(int seedNum)
        {
            int sample = (int)Math.Floor((double)_mergedVerticesNum / seedNum);
            var arr = new Vector<double>[seedNum];
            for (int i = 0; i < seedNum; i++)
                arr[i] = CurrentPosition[i * sample];
            return arr;
        }

        // ───────────────────────────────────────────────────────────────[...]
        // TransformGen：每幀每 cluster 計算最佳化變形矩陣
        // TransformGen: compute per-cluster optimal deformation matrix each frame
        //
        // ① 計算 Apq，乘以預先算好的 Aqq^-1 得到 _A
        // ② EVD 求平方根矩陣，分解出 Rotation / Stretching
        // ③ Transform = Beta*A + (1-Beta)*Rotation
        // ④ 調整 det 至 1（防止體積膨脹/收縮）
        // ───────────────────────────────────────────────────────────────[...]

        private void TransformGen(int groupIndex)
        {
            int head  = _clusterHeadTailArray[groupIndex * 2];
            int tail  = _clusterHeadTailArray[groupIndex * 2 + 1];
            int verts = tail - head + 1;

            // ① Apq
            _apq.Clear();
            for (int i = 0; i < verts; i++)
            {
                int pi = _pointsIdArray[head + i];
                _apq += (CurrentPosition[pi] - _currentCenterPos[groupIndex])
                        .Multiply(_mass[pi])
                        .OuterProduct(_originalPosition[pi] - _originalCenterPos[groupIndex]);
            }
            // 小量正則化（0.1）防止 Apq 在靜止狀態下退化為奇異矩陣
            // Small regularisation (0.1) prevents Apq from becoming singular at rest
            for (int d = 0; d < 3; d++) _apq[d, d] += 0.1;

            _aMatrix = _apq * _aqq[groupIndex]; // _A = Apq * Aqq^-1

            double det;
            if (quadratic)
            {
                // ② Stretching/Rotation 分解（二次模式）
                _apqSquare = _apq.SubMatrix(0, 3, 0, 3);
                // 微小正則化防止 Stretching 矩陣奇異 / Tiny regularisation to prevent singular Stretching inversion
                SquareRootMatrixCal(_apqSquare.Transpose() * _apqSquare, _stretching);
                for (int d = 0; d < 3; d++) _stretching[d, d] += 1e-5;
                _rotation = (_apqSquare * _stretching.Inverse()).Append(_padding);

                // ③ Transform = Beta*A + (1-Beta)*Rotation
                _transform[groupIndex] = (double)Beta * _aMatrix + (1.0 - Beta) * _rotation;
                det = _transform[groupIndex].SubMatrix(0, 3, 0, 3).Determinant();
            }
            else
            {
                // ② Stretching/Rotation 分解（線性模式）
                // 微小正則化防止 Stretching 矩陣奇異 / Tiny regularisation to prevent singular Stretching inversion
                SquareRootMatrixCal(_apq.Transpose() * _apq, _stretching);
                for (int d = 0; d < 3; d++) _stretching[d, d] += 1e-5;
                _rotation = _apq * _stretching.Inverse();

                // ③ Transform = Beta*A + (1-Beta)*Rotation
                _transform[groupIndex] = (double)Beta * _aMatrix + (1.0 - Beta) * _rotation;
                det = _transform[groupIndex].Determinant();
            }

            // ④ 調整 det 至 1 / Normalise determinant to 1
            if (double.IsNaN(det) || double.IsInfinity(det))
            {
                _transform[groupIndex] = CreateMatrix.DenseIdentity<double>(3, _dim);
                return;
            }
            det = Math.Sign(det) * Math.Pow(Math.Abs(det), 1.0 / 3.0);
            if (double.IsNaN(det) || double.IsInfinity(det) || Math.Abs(det) < 1e-5)
                det = (det >= 0 || double.IsNaN(det)) ? 1e-5 : -1e-5;
            _transform[groupIndex] = _transform[groupIndex].Divide(det);

            // 安全性檢查 / Safety check
            for (int r = 0; r < 3; r++)
                for (int c = 0; c < _dim; c++)
                {
                    double v = _transform[groupIndex][r, c];
                    if (double.IsNaN(v) || double.IsInfinity(v))
                    {
                        _transform[groupIndex] = CreateMatrix.DenseIdentity<double>(3, _dim);
                        return;
                    }
                }
        }

        /// <summary>
        /// EVD 方法求方陣平方根（用於 Stretching/Rotation 分解）。
        /// Computes the square root of a square matrix via EVD (for Stretching/Rotation decomposition).
        /// </summary>
        private void SquareRootMatrixCal(Matrix<double> matrix, Matrix<double> ans)
        {
            _evdStorage  = matrix.Evd();
            _eigenValue  = _evdStorage.D;
            _eigenVectors = _evdStorage.EigenVectors;
            for (int i = 0; i < _eigenValue.RowCount; i++)
                if (_eigenValue[i, i] < 0) _eigenValue[i, i] = 0;
            _eigenValue = Matrix<double>.Sqrt(_eigenValue);
            double[] t = { _eigenValue[0, 0], _eigenValue[1, 1], _eigenValue[2, 2] };
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    double val = 0;
                    for (int k = 0; k < 3; k++) val += t[k] * _eigenVectors[i, k] * _eigenVectors[j, k];
                    ans.At(i, j, val);
                }
        }

        // ───────────────────────────────────────────────────────────────[...]
        // GPU Dispatch
        // ───────────────────────────────────────────────────────────────[...]

        private void TransformSet()
        {
            for (int i = 0; i < cluster_num; i++)
                for (int row = 0; row < 3; row++)
                    for (int col = 0; col < _dim; col++)
                        _transformArray[i * 3 * _dim + row * _dim + col] = _transform[i].At(row, col);

            _transformBuffer.SetData(_transformArray);
            DeformAlgorithm.SetBuffer(_shapeMatchingKernel, "Transform", _transformBuffer);
            // 真正更新點位置在 ShapeMatching kernel 裡跑（Dispatch）
            // Actual vertex position update runs inside the ShapeMatching kernel (Dispatch)
            DeformAlgorithm.Dispatch(_shapeMatchingKernel, (_mergedVerticesNum + 63) / 64, 1, 1);
        }

        private void CenterUpdate()
        {
            for (int i = 0; i < cluster_num; i++)
            {
                int head = _clusterHeadTailArray[i * 2];
                int tail = _clusterHeadTailArray[i * 2 + 1];
                _currentCenterPos[i] = Operation.average(CurrentPosition, _pointsIdArray, head, tail);

                // centerReturnLerp: 插值因子，把 GPU 重心往原始位置拉回一點以維持穩定
                // centerReturnLerp: blend factor that pulls the GPU cluster centre slightly toward rest
                const double centerReturnLerp = 0.15;
                Vector<double> gpuCenter =
                    _currentCenterPos[i] * (1.0 - centerReturnLerp) + _originalCenterPos[i] * centerReturnLerp;
                for (int j = 0; j < 3; j++)
                    _centerArray[i * (_dim + 3) + _dim + j] = gpuCenter.At(j);
            }
            _centerBuffer.SetData(_centerArray);
        }

        // ───────────────────────────────────────────────────────────────[...]
        // Public API
        // ───────────────────────────────────────────────────────────────[...]

        /// <summary>
        /// 在指定世界座標位置施加力（速度衝量），由 TissueDeformationController 呼叫。
        /// Applies a velocity impulse at the given world position (called by TissueDeformationController).
        /// </summary>
        public void ApplyForceAtPosition(Vector3 worldPosition, Vector3 force, float radius = 0.01f)
        {
            if (CurrentPosition == null || _addedVelocityArray == null) return;
            Vector3 localPos   = transform.InverseTransformPoint(worldPosition);
            Vector3 localForce = transform.InverseTransformDirection(force);
            float   invMass    = (Mass > 0f) ? (1f / Mass) : 1f;

            for (int v = 0; v < _mergedVerticesNum; v++)
            {
                Vector<double> p = CurrentPosition[v];
                float dist = Vector3.Distance(
                    new Vector3((float)p[0], (float)p[1], (float)p[2]), localPos);
                if (dist >= radius) continue;
                float falloff = 1f - dist / radius;
                _addedVelocityArray[3 * v + 0] += localForce.x * falloff * invMass;
                _addedVelocityArray[3 * v + 1] += localForce.y * falloff * invMass;
                _addedVelocityArray[3 * v + 2] += localForce.z * falloff * invMass;
            }
        }

        /// <summary>
        /// 重置所有頂點回初始位置，並清空速度。
        /// Resets all vertices to their original positions and clears velocities.
        /// </summary>
        public void ResetDeformation()
        {
            if (CurrentPosition == null || _originalPosition == null) return;
            for (int i = 0; i < _mergedVerticesNum; i++)
                CurrentPosition[i] = _originalPosition[i].Clone();
            Array.Clear(_velocityArray, 0, _velocityArray.Length);
            Array.Clear(_addedVelocityArray, 0, _addedVelocityArray.Length);
            _velocityBuffer.SetData(_velocityArray);
            Operation.DoubleVectorArray2DoubleArray(ref _currentPosArray, CurrentPosition);
            _currentPosBuffer.SetData(_currentPosArray);
        }
    }
}
