using UnityEngine;
using System;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Factorization;
using System.Runtime.InteropServices;
using UnityEngine.ProBuilder.Shapes;
using UnityEngine.SocialPlatforms;

[RequireComponent(typeof(MeshFilter))]
[RequireComponent(typeof(MeshCollider))]
public class DeformableBody : MonoBehaviour
{
    public enum ContactSolveMode
    {
        LegacyVelocityInjection = 0,
        XPBDSkeleton = 1
    }

    public float Mass = 1;                  //affect the magnitude of disturbance caused by impacts
    [Range(0.0f,1.0f)] 
    public float Alpha = 0.01f;             //affect the deformation velocity of each vertex
    [Range(0.0f,1.0f)]
    public float Beta = 0.5f;               //affect the rigidity of virtual objects
    [Range(0.0f,1.0f)]
    public float Damp = 0.005f;             //affect the decay of velocity at each vertex
    [Min(1)]
    public int cluster_num = 8;             //define the number of clusters
    [Range(0.0f,0.99f)]
    public float Overlap = 0.1f;            //define the ratio of between each cluster
    
    public bool quadratic = false;
    public double[] coefs = new double[9];

    public Vector3 externalPos;
    public float externalRadius;
    public Vector3 externalVelocity;
    public ToolController tool;

    
    [Header("組織 Alpha 差異設定")]
    public float AlphaNormal = 0.05f; // 正常組織 (軟)
    public float AlphaTumor = 0.3f;   // 腫瘤組織 (硬)
    
    [Header("力回饋")]
    public double currentHapticForceY = 0.0;
    [Range(0f, 1f)] public float hapticSmoothing = 0.15f;
    private double hapticForceFiltered = 0.0;
    private Vector3 reactionForceFiltered = Vector3.zero;
    private Vector3 pendingReactionForce = Vector3.zero;
    public double hapticDeadzoneEnter = 0.03; // 進入門檻
    public double hapticDeadzoneExit = 0.015; // 退出門檻（遲滯）
    private bool hapticActive = false;
        private double pendingHapticForce = 0.0;
    [Header("接觸模型")]
    public float pressKp = 60f;                 // 穿透 -> 推回速度
    public float pressKd = 10f;                 // 法向阻尼
    public float contactRadius = 1.2f;         // 接觸影響半徑(世界)
    [Range(1f, 6f)] public float contactFalloffPower = 2f;
    public float contactDecaySpeed = 3f;
    public float minPenetration = 0.0002f;
    [Header("接觸求解模式")]
    public ContactSolveMode contactSolveMode = ContactSolveMode.LegacyVelocityInjection;
    [Header("XPBD (Skeleton)")]
    [Min(1)] public int xpbdSubsteps = 2;
    [Min(1)] public int xpbdIterations = 3;
    [Range(0f, 1f)] public float xpbdCompliance = 0.0005f;
    public float xpbdMaxForce = 20f;
    [Header("穩定控制")]
    public float maxDvPerStep = 0.2f;          // 每頂點每幀最大速度增量
    public float maxTotalDvPerFrame = 4.0f;     // 每幀總注入上限（防爆）
    public float globalVelocityDamping = 0.992f;// 全域速度阻尼
    public float velocityClamp = 2.5f;          // 每分量夾制
    [Header("接觸位置修正")]
    public bool enablePositionProjection = true;
    public float maxPositionCorrectionPerStep = 0.008f;

    public int colliderRefreshInterval = 4;     // MeshCollider 更新間隔幀數
    private int colliderRefreshCounter = 0;
    

    public ComputeShader DeformAlgorithm;
    double[] transform_array;       //variable
    ComputeBuffer TransformBuffer;
    double[] center_array;          //variable
    ComputeBuffer CenterBuffer;
    double[] currentPos_array;      //variable
    ComputeBuffer CurrentPosBuffer; 
    double[] velocity_array;        //variable
    double[] added_velocity_array;
    float[] contact_weights;
    ComputeBuffer VelocityBuffer;     
    double[] originalPos_array;     //constant   
    ComputeBuffer OriginalPosBuffer;   
    int[] pointsHeadTail_array;     //constant
    ComputeBuffer PointsHeadTail;  
    int[] clusterID_array;          //constant
    ComputeBuffer ClusterID;
    float[] alpha_array;
    ComputeBuffer AlphaBuffer;
    int[] clusterHeadTail_array;    //constant
    int[] pointsID_array;           //constant
    int ShapeMatchingKernelIndex;   //constant  
    int dim;

    Mesh mesh;
    Vector3[] vertices;
    MeshCollider mesh_collider;
    List<Vector3> MergedVertices; //a vertices list without duplicate vertices
    
    int triangle_num;
    int vertices_num;
    Vector<double>[] mergedVertices_pos;
    int mergedVertices_num;    //the total number of points after merging
    int[] mergedVerticesTable; //the original index of points to new index after merging
    int[] triangles;
    double[] mass;

    Vector<double>[] OriginalPosition;//This is local coordinate 
    Vector<double>[] CurrentPosition; //This is local coordinate 
    Vector<double> CurrentCenterPos;  //This is local coordinate
    Vector<double> LastCenterPos; //This is local coordinate

    //ShapeMatching[] ClusterParticle;//This is out of date

    Matrix<double>[] Transform;
    Vector<double>[] originalCenterPos;
    Vector<double>[] currentCenterPos;

    Matrix<double> _Apq;
    Matrix<double>[] _Aqq;
    Matrix<double> _A;
    Matrix<double> _Rotation;
    Matrix<double> _Stretching;
    Evd<double> _EvdStorage;
    Matrix<double> _EigenValue;
    Matrix<double> _EigenVectors;

    Matrix<double> Apq_Square;
    Matrix<double> padding;

    // Start is called before the first frame update
    void Start(){

        Physics.defaultMaxDepenetrationVelocity = 0.5f;

        Init(); 
        Debug.Log(mergedVertices_num);
        //Debuger.CommandPosition_Gen(CurrentPosition, currentPos_array, CurrentPosBuffer);
        //CenterUpdate();
    }

    //The entire algorithm is executed in FixedUpdate()
    void FixedUpdate()
    {
        ApplyExternalToolContact();

        bool needsVelocityUpdate = false;
        for (int i = 0; i < added_velocity_array.Length; i++)
        {
            if (added_velocity_array[i] != 0.0) { needsVelocityUpdate = true; break; }
        }
        if (!needsVelocityUpdate)
        {
            for (int i = 0; i < contact_weights.Length; i++)
            {
                if (contact_weights[i] > 0.0f) { needsVelocityUpdate = true; break; }
            }
        }

        if (needsVelocityUpdate)
        {
            VelocityBuffer.GetData(velocity_array);

            for (int k = 0; k < mergedVertices_num; k++)
            {
                // 1) 注入接觸速度
                velocity_array[3 * k + 0] += added_velocity_array[3 * k + 0];
                velocity_array[3 * k + 1] += added_velocity_array[3 * k + 1];
                velocity_array[3 * k + 2] += added_velocity_array[3 * k + 2];

                // 2) 全域阻尼（注意正確索引）
                velocity_array[3 * k + 0] *= globalVelocityDamping;
                velocity_array[3 * k + 1] *= globalVelocityDamping;
                velocity_array[3 * k + 2] *= globalVelocityDamping;

                // 3) 夾制
                velocity_array[3 * k + 0] = Math.Max(-velocityClamp, Math.Min(velocityClamp, velocity_array[3 * k + 0]));
                velocity_array[3 * k + 1] = Math.Max(-velocityClamp, Math.Min(velocityClamp, velocity_array[3 * k + 1]));
                velocity_array[3 * k + 2] = Math.Max(-velocityClamp, Math.Min(velocityClamp, velocity_array[3 * k + 2]));

                // 4) 權重衰減
                if (contact_weights[k] > 0f)
                    contact_weights[k] = Mathf.MoveTowards(contact_weights[k], 0f, Time.fixedDeltaTime * contactDecaySpeed);

                // 5) 清空暫存
                added_velocity_array[3 * k + 0] = 0.0;
                added_velocity_array[3 * k + 1] = 0.0;
                added_velocity_array[3 * k + 2] = 0.0;
            }

            VelocityBuffer.SetData(velocity_array);
        }

        LastCenterPos = CurrentCenterPos;
        CurrentCenterPos = Operation.average(CurrentPosition);
        for (int i = 0; i < cluster_num; i++) TransformGen(i);
        CenterUpdate();
        TransformSet();
        
        CurrentPosBuffer.GetData(currentPos_array);
        Operation.DoubleArray2DoubleVectorArray(ref CurrentPosition, currentPos_array);
        
        // haptic 輸出
        hapticForceFiltered = Mathf.Lerp((float)hapticForceFiltered, (float)pendingHapticForce, hapticSmoothing);
        currentHapticForceY = hapticForceFiltered;
        
        reactionForceFiltered = Vector3.Lerp(reactionForceFiltered, pendingReactionForce, hapticSmoothing);

        pendingHapticForce *= 0.75;
        if (pendingHapticForce < 1e-6) pendingHapticForce = 0.0;
        pendingReactionForce = Vector3.Lerp(pendingReactionForce, Vector3.zero, 0.35f);
        if (pendingReactionForce.sqrMagnitude < 1e-8f) pendingReactionForce = Vector3.zero;
        if (tool != null)
        {
            tool.SetReactionForce(reactionForceFiltered);
        }
        //The following commented code is for debuging.
        //Debuger.VectorArrayCheck(CurrentPosition);

        /*
        if (velocity_array != null)
        {
            VelocityBuffer.GetData(velocity_array);
        }
        VelocityBuffer.GetData(velocity_array);
        for(int i = 0; i < 162; i++){
            for(int j = 2; j < 3; j++){
                 //Debug.LogFormat("The vertex {0}'s {1}th value is {2}",i,j,temp[i*3 + j]);
                Debug.LogFormat("the z veloctiy is {0}", velocity_array[3*i + j]);
            }
        }    
        Debug.Log("----------");
        */
        //Debuger.VectorArrayCheck(currentCenterPos);
    }

    void ApplyExternalToolContact()
    {
        if (contactSolveMode == ContactSolveMode.XPBDSkeleton)
        {
            ApplyExternalToolContactXPBDSkeleton();
            return;
        }
        ApplyExternalToolContactLegacy();
    }

    void ApplyExternalToolContactLegacy()
    {
        if (CurrentPosition == null || added_velocity_array == null) return;
        if (externalRadius <= 0f) return;

        VelocityBuffer.GetData(velocity_array);

        float influenceR = Mathf.Max(contactRadius, externalRadius * 1.25f);
        float totalW = 0f;
        float[] wCache = new float[mergedVertices_num];
        Vector3[] nCache = new Vector3[mergedVertices_num];
        float[] penCache = new float[mergedVertices_num];

        int candidateCount = 0;

        for (int v = 0; v < mergedVertices_num; v++)
        {
            Vector<double> p = CurrentPosition[v];
            Vector3 qWorld = transform.TransformPoint(new Vector3((float)p[0], (float)p[1], (float)p[2]));
            Vector3 dir = qWorld - externalPos;
            float d = dir.magnitude;
            if (d < 1e-6f) continue;

            float penetration = externalRadius - d;
            if (penetration <= minPenetration) continue;

            float shellDist = Mathf.Abs(d - externalRadius);
            float bestW = 1f - Mathf.Clamp01(shellDist / Mathf.Max(1e-4f, influenceR));
            if (bestW <= 0f) continue;

            wCache[v] = bestW;
            // Push vertices away from tool center to create visible indentation
            nCache[v] = dir / d;
            penCache[v] = penetration;
            totalW += bestW;
            candidateCount++;
        }

        if (totalW <= 1e-6f) return;

        float remainBudget = maxTotalDvPerFrame;

        for (int v = 0; v < mergedVertices_num; v++)
        {
            float w = wCache[v];
            if (w <= 0f) continue;

            Vector3 nPush = nCache[v];
            float pen = penCache[v];

            Vector3 vLocal = new Vector3(
                (float)velocity_array[3 * v + 0],
                (float)velocity_array[3 * v + 1],
                (float)velocity_array[3 * v + 2]
            );
            Vector3 vWorld = transform.TransformDirection(vLocal);

            float vn = Vector3.Dot(vWorld - externalVelocity, nPush);
            float shapedW = Mathf.Pow(w, Mathf.Max(1f, contactFalloffPower));
            float dv = (pressKp * pen - pressKd * vn) * shapedW * Time.fixedDeltaTime;
            dv = Mathf.Clamp(dv, 0f, maxDvPerStep);

            float share = w / totalW;
            float dvBudget = remainBudget * share;
            dv = Mathf.Min(dv, dvBudget);
            if (dv <= 0f) continue;

            Vector3 vAddWorld = nPush * dv;
            Vector3 vAddLocal = transform.InverseTransformDirection(vAddWorld);
            float projMag = Mathf.Min(maxPositionCorrectionPerStep, pen * 0.6f) * shapedW;
            ApplyPositionCorrection(v, nPush * projMag);
            added_velocity_array[3 * v + 0] += vAddLocal.x;
            added_velocity_array[3 * v + 1] += vAddLocal.y;
            added_velocity_array[3 * v + 2] += vAddLocal.z;

            if (shapedW > contact_weights[v]) contact_weights[v] = shapedW;

            double h = (pressKp * pen + Math.Max(0f, -vn) * pressKd) * shapedW * 4.0;
            pendingHapticForce += h;
            pendingReactionForce += (-nPush) * (float)h;

            remainBudget -= dv;
            if (remainBudget <= 1e-6f) break;
        }

        Debug.Log($"[EXT_CONTACT] candidates={candidateCount}, totalW={totalW:F5}, radius={externalRadius:F4}");
    }

    void ApplyPositionCorrection(int v, Vector3 worldDelta)
    {
        if (!enablePositionProjection) return;
        if (worldDelta.sqrMagnitude <= 1e-12f) return;

        Vector3 localDelta = transform.InverseTransformDirection(worldDelta);
        CurrentPosition[v][0] += localDelta.x;
        CurrentPosition[v][1] += localDelta.y;
        CurrentPosition[v][2] += localDelta.z;
    }

    void ApplyExternalToolContactXPBDSkeleton()
    {
        if (CurrentPosition == null || added_velocity_array == null) return;
        if (externalRadius <= 0f) return;

        float dt = Time.fixedDeltaTime;
        if (dt <= 1e-6f) return;

        int substeps = Mathf.Max(1, xpbdSubsteps);
        int iterations = Mathf.Max(1, xpbdIterations);
        float subDt = dt / substeps;
        float alpha = Mathf.Max(0f, xpbdCompliance) / (subDt * subDt); // XPBD α~

        double sumAbsLambda = 0.0;
        int activeConstraints = 0;

        for (int s = 0; s < substeps; s++)
        {
            float[] lambda = new float[mergedVertices_num];

            for (int it = 0; it < iterations; it++)
            {
                for (int v = 0; v < mergedVertices_num; v++)
                {
                    Vector<double> p = CurrentPosition[v];
                    Vector3 qWorld = transform.TransformPoint(new Vector3((float)p[0], (float)p[1], (float)p[2]));

                    Vector3 dir = qWorld - externalPos;
                    float d = dir.magnitude;
                    if (d < 1e-6f) continue;

                    float penetration = externalRadius - d;
                    if (penetration <= minPenetration) continue;

                    float w = 1f - Mathf.Clamp01(Mathf.Abs(d - externalRadius) / Mathf.Max(1e-4f, contactRadius));
                    if (w <= 0f) continue;

                    float invMass = (float)(1.0 / Math.Max(1e-8, mass[v]));
                    invMass *= Mathf.Max(0.05f, w); // 權重衰減

                    float C = penetration; // C>0 代表穿透
                    float deltaLambda = (-C - alpha * lambda[v]) / (invMass + alpha);
                    lambda[v] += deltaLambda;

                    Vector3 nOut = dir / d; // 往球外方向修正
                    Vector3 dxWorld = -invMass * deltaLambda * nOut;
                    Vector3 dxLocal = transform.InverseTransformDirection(dxWorld);
                    ApplyPositionCorrection(v, dxWorld);

                    Vector3 vAdd = dxLocal / Mathf.Max(1e-6f, subDt);
                    float vAddMag = vAdd.magnitude;
                    if (vAddMag > maxDvPerStep)
                    {
                        vAdd *= maxDvPerStep / Mathf.Max(1e-6f, vAddMag);
                    }

                    added_velocity_array[3 * v + 0] += vAdd.x;
                    added_velocity_array[3 * v + 1] += vAdd.y;
                    added_velocity_array[3 * v + 2] += vAdd.z;

                    if (w > contact_weights[v]) contact_weights[v] = w;
                    sumAbsLambda += Math.Abs(deltaLambda);
                    activeConstraints++;
                }
            }
        }

        // skeleton force readout: F ~= Σ|Δλ| / dt²
        double forceEst = (dt > 1e-6f) ? (sumAbsLambda / (dt * dt)) : 0.0;
        pendingHapticForce += Math.Min(forceEst, xpbdMaxForce);

        if (activeConstraints > 0)
        {
            Debug.Log($"[XPBD_CONTACT] active={activeConstraints}, force={forceEst:F4}, sub={substeps}, it={iterations}");
        }
    }

    // Update is called once per frame
    // Update() is only used to update the mesh vertices computed by the algorithm in FixedUpdate()
    void Update(){
        mesh_update();
    }

    //OnDisable() will be called when the program stop, it is used to return memeory space.
    void OnDisable(){
        if (TransformBuffer != null)
        {
            TransformBuffer.Release();
            TransformBuffer = null;
        }           
        CenterBuffer.Release();
        CurrentPosBuffer.Release();
        OriginalPosBuffer.Release();
        PointsHeadTail.Release();
        ClusterID.Release();
        VelocityBuffer.Release();
        if (AlphaBuffer != null) AlphaBuffer.Release();
    }

    //Declare varialbes and memory space, construct a new vertices array without duplicated vertices, perform overlapping k-means, and initialize buffers for GPGPU
    void Init(){

        mesh = GetComponent<MeshFilter>().mesh;
        mesh_collider = GetComponent<MeshCollider>();
        vertices = mesh.vertices;
        triangles = mesh.triangles;
        vertices_num = vertices.Length;
        triangle_num = triangles.Length/3;

        MergedVertices = new List<Vector3>();
        mergedVerticesTable = new int[vertices.Length];

        vertices_merge(vertices, MergedVertices, mergedVerticesTable);   //construct a new vertices array without duplicated vertices

        mergedVertices_num = MergedVertices.Count;
        OriginalPosition = Operation.List2Array(MergedVertices);
        CurrentPosition = Operation.List2Array(MergedVertices);
        mergedVertices_pos = Operation.List2Array(MergedVertices);
        
        CurrentCenterPos = Operation.average(CurrentPosition);
        LastCenterPos = CurrentCenterPos;

        if(quadratic){
            dim = 9;
            //OriginalPosition = Operation.QuadraticMapping(Operation.List2Array(MergedVertices));
            padding = CreateMatrix.Dense<double>(3,6,0);
        }
        else{
            dim = 3;
            //OriginalPosition = Operation.List2Array(MergedVertices);
        }
        //
        //velocity = Operation.VectorArrayInit(mergedVertices_num, 3, 0);
        //
        //gravity = CreateVector.Dense<double>(new double[]{0.0, -0.98, 0.0});
        Transform = new Matrix<double>[cluster_num];
        originalCenterPos = new Vector<double>[cluster_num];
        currentCenterPos = new Vector<double>[cluster_num];

        OverlappingKMeans(cluster_num, Overlap, 30);  //perform overlapping k-means
        
        if(quadratic){
            OriginalPosition = Operation.QuadraticMapping(OriginalPosition, coefs);
            originalCenterPos = Operation.QuadraticMapping(originalCenterPos, coefs);
        }

        mass = new double[mergedVertices_num];
        alpha_array = new float[mergedVertices_num];
        Color[] vColors = mesh.colors;

        float baseMass = (Mass <= 0) ? 1.0f : (Mass / mergedVertices_num);

        for (int i = 0; i < mergedVertices_num; i++)
        {
            int originalIdx = Array.IndexOf(mergedVerticesTable, i);

            Color c = (vColors != null && vColors.Length > originalIdx && originalIdx >= 0)
                      ? vColors[originalIdx] : Color.white;

            if (c.r > 0.5f) // 紅色區域 (腫瘤)
            {
                mass[i] = baseMass * 2.0f;
                alpha_array[i] = AlphaTumor;
            }
            else // 一般區域
            {
                mass[i] = baseMass;
                alpha_array[i] = AlphaNormal;
            }

            if (double.IsNaN(mass[i]) || mass[i] <= 0) mass[i] = 1.0;
        }

        _Apq = CreateMatrix.Dense<double>(3,dim,0);
        _Aqq = Operation.Aqq_Init(OriginalPosition, mass, originalCenterPos, clusterHeadTail_array, pointsID_array, cluster_num, dim);
        _Stretching = CreateMatrix.Dense<double>(3,3,0);
        Apq_Square = CreateMatrix.Dense<double>(3,3,0);

        added_velocity_array = new double[3 * mergedVertices_num];
        contact_weights = new float[mergedVertices_num];

        ShaderInit();      //initialize buffers for GPGPU
    }

    void OnCollisionStay(Collision other)
    {
        if (!other.collider.CompareTag("Tool")) return;
        if (other.contactCount <= 0) return;

        ApplyKinematicDeformation(other, 0f);

        Debug.Log($"[COL] tool={other.collider.name}, contacts={other.contactCount}, impulse={other.impulse.magnitude:F6}");
    }

    void ApplyKinematicDeformation(Collision collision, float collisionForceN)
    {
        if (CurrentPosition == null || added_velocity_array == null) return;
        var contacts = collision.contacts;
        if (contacts == null || contacts.Length == 0) return;

        SphereCollider sc = collision.collider as SphereCollider;
        if (sc == null) return;

        Vector3 s = sc.transform.lossyScale;
        float toolR = sc.radius * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
        Vector3 toolC = sc.transform.TransformPoint(sc.center);

        VelocityBuffer.GetData(velocity_array);

        int affected = 0;
        double sumAdd = 0.0;
        double sumH = 0.0;

        float totalW = 0f;
        float[] wCache = new float[mergedVertices_num];
        Vector3[] nCache = new Vector3[mergedVertices_num];
        float[] penCache = new float[mergedVertices_num];

        for (int v = 0; v < mergedVertices_num; v++)
        {
            Vector<double> p = CurrentPosition[v];
            Vector3 qWorld = transform.TransformPoint(new Vector3((float)p[0], (float)p[1], (float)p[2]));

            Vector3 dir = qWorld - toolC;
            float d = dir.magnitude;
            if (d < 1e-6f) continue;

            float penetration = Mathf.Max(0f, toolR - d);                   // 真穿透
            float gapToSurface = Mathf.Max(0f, d - toolR);                  // 與球面距離(外側)
            float proximity = Mathf.Max(0f, contactRadius - gapToSurface);  // 在接觸半徑內就有值
            if (penetration <= 0f) continue;
            float pen = penetration;
            pen = Mathf.Max(pen, 1e-5f);                                    // 防止完全0

            float shellDist = Mathf.Abs(d - toolR); // 到球殼的距離
            float influenceR = contactRadius * 3f;  // 測試放寬
            float bestW = 1f - Mathf.Clamp01(shellDist / influenceR);
            if (bestW <= 0f) continue;
            for (int c = 0; c < contacts.Length; c++)
            {
                float dist = Vector3.Distance(qWorld, contacts[c].point);
                if (dist > contactRadius *3f) continue;
                float w = 1f - dist / (contactRadius *3f);
                if (w > bestW) bestW = w;
            }
            if (bestW <= 0f) continue;

            // Push vertices away from tool center to avoid inverting the surface response
            Vector3 nPush = dir / d;

            wCache[v] = bestW;
            nCache[v] = nPush;
            penCache[v] = pen;
            totalW += bestW;
        }

        if (totalW <= 1e-6f) return;

        float remainBudget = maxTotalDvPerFrame;

        for (int v = 0; v < mergedVertices_num; v++)
        {
            float w = wCache[v];
            if (w <= 0f) continue;

            Vector3 nPush = nCache[v];
            float pen = penCache[v];

            Vector3 vLocal = new Vector3(
                (float)velocity_array[3 * v + 0],
                (float)velocity_array[3 * v + 1],
                (float)velocity_array[3 * v + 2]
            );
            Vector3 vWorld = transform.TransformDirection(vLocal);
            float vn = Vector3.Dot(vWorld - externalVelocity, nPush);

            float shapedW = Mathf.Pow(w, Mathf.Max(1f, contactFalloffPower));
            float dv = (pressKp * pen - pressKd * vn) * shapedW * Time.fixedDeltaTime;
            dv = Mathf.Clamp(dv, 0f, maxDvPerStep);

            // 總量限制（防爆）
            float share = w / totalW;
            float dvBudget = remainBudget * share;
            dv = Mathf.Min(dv, dvBudget);
            if (dv <= 0f) continue;

            Vector3 vAddWorld = nPush * dv;
            Vector3 vAddLocal = transform.InverseTransformDirection(vAddWorld);
            float projMag = Mathf.Min(maxPositionCorrectionPerStep, pen * 0.6f) * shapedW;
            ApplyPositionCorrection(v, nPush * projMag);

            added_velocity_array[3 * v + 0] += vAddLocal.x;
            added_velocity_array[3 * v + 1] += vAddLocal.y;
            added_velocity_array[3 * v + 2] += vAddLocal.z;

            if (shapedW > contact_weights[v]) contact_weights[v] = shapedW;

            double h = (pressKp * pen + Math.Max(0f, -vn) * pressKd) * shapedW * 4.0;
            pendingHapticForce += h;
            pendingReactionForce += (-nPush) * (float)h;
            sumH += h;

            sumAdd += vAddWorld.magnitude;
            affected++;

            remainBudget -= dv;
            if (remainBudget <= 1e-6f) break;
        }

        Debug.Log($"[SUSTAIN] contacts={contacts.Length}, affected={affected}, totalW={totalW:F6}, sumAdd={sumAdd:E3}, sumH={sumH:E3}");
    }
        
    //Update the mesh vertices computed by the algorithm in FixedUpdate()
    void mesh_update(){

        Vector<double> pos;
        for(int i = 0 ; i < vertices_num ; i++){
            pos = CurrentPosition[mergedVerticesTable[i]];
            vertices[i] = new Vector3((float)pos.At(0),(float)pos.At(1),(float)pos.At(2));
        }
        
        /*
        transform.position += new Vector3((float)(CurrentCenterPos.At(0)-LastCenterPos.At(0)),
                                          (float)(CurrentCenterPos.At(1)-LastCenterPos.At(1)),
                                          (float)(CurrentCenterPos.At(2)-LastCenterPos.At(2)));
        */
        
        mesh.SetVertices(vertices);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        colliderRefreshCounter++;
        if (colliderRefreshCounter >= colliderRefreshInterval)
        {
            colliderRefreshCounter = 0;
            mesh_collider.sharedMesh = null;
            mesh_collider.sharedMesh = mesh;
        }

    }

    //Initialize buffers for GPGPU
    void ShaderInit(){
        ShapeMatchingKernelIndex = DeformAlgorithm.FindKernel("ShapeMatching");
        transform_array = new double[dim*3*cluster_num];
        center_array = new double[(3+dim)*cluster_num];
        currentPos_array = new double[3*mergedVertices_num];
        originalPos_array = new double[dim*mergedVertices_num];
        velocity_array = Operation.DoubleArrayInit(3*mergedVertices_num, 0.0);      

        TransformBuffer = new ComputeBuffer(dim*3*cluster_num, sizeof(double));
        CenterBuffer = new ComputeBuffer((3+dim)*cluster_num, sizeof(double));
        CurrentPosBuffer = Operation.PositionBufferInit(ref currentPos_array, CurrentPosition);
        OriginalPosBuffer = Operation.PositionBufferInit(ref originalPos_array, OriginalPosition);
        VelocityBuffer = new ComputeBuffer(3*mergedVertices_num, sizeof(double));
        PointsHeadTail = new ComputeBuffer(2*vertices_num, sizeof(int));
        ClusterID = new ComputeBuffer(clusterID_array.Length, sizeof(int));
        AlphaBuffer = new ComputeBuffer(mergedVertices_num, sizeof(float));
        AlphaBuffer.SetData(alpha_array);

        List<int> PointsIndex = new List<int>();
        for(int i = 0; i < cluster_num; i++){
            
            for(int a = 0; a < dim*3; a++){
                if(a == 0 || a == dim + 1 || a == 2*dim + 2){
                    transform_array[i*9 + a] = 1;
                }
                else{
                    transform_array[i*9 + a] = 0;
                }
            }

            for(int b = 0; b < dim; b++){
                center_array[i*(3+dim) + b] = originalCenterPos[i].At(b);
                if(b < 3){
                    center_array[i*(3+dim) + b + dim] = currentCenterPos[i].At(b);
                }
            }

        }

        TransformBuffer.SetData(transform_array);
        CenterBuffer.SetData(center_array);
        VelocityBuffer.SetData(velocity_array);
        PointsHeadTail.SetData(pointsHeadTail_array);
        ClusterID.SetData(clusterID_array);
        
        DeformAlgorithm.SetInt("dim", dim);
        DeformAlgorithm.SetInt("PointsNum",mergedVertices_num);
        DeformAlgorithm.SetFloat("DeltaTime", Time.fixedDeltaTime);
        //DeformAlgorithm.SetFloat("alpha", Alpha);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "Alphas", AlphaBuffer);
        DeformAlgorithm.SetFloat("damp", Damp);
        DeformAlgorithm.SetFloat("mass", Mass);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "Transform", TransformBuffer);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "Center", CenterBuffer);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "CurrentPos", CurrentPosBuffer);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "OriginalPos", OriginalPosBuffer);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "Velocity", VelocityBuffer);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "PointsHeadTail", PointsHeadTail);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "ClusterIDs", ClusterID);
    }

    //Use "vertices" to generate "mergedVerticesTable" and "MergedVertices".
    void vertices_merge(Vector3[] vertices, List<Vector3> MergedVertices, int[] mergedVerticesTable){

        float displace;
        MergedVertices.Add(vertices[0]);
        mergedVerticesTable[0] = 0;
        for(int i = 1; i < vertices.Length; i++){
            for(int j = 0; j < MergedVertices.Count; j++){
                displace = (vertices[i] - MergedVertices[j]).magnitude;
                if(displace < 0.0001f){
                    mergedVerticesTable[i] = j;
                    break;
                }
                if(j == MergedVertices.Count - 1 && displace >= 0.0001f){
                    mergedVerticesTable[i] = j + 1;
                    MergedVertices.Add(vertices[i]);
                }
            }
        }
        mergedVertices_num = MergedVertices.Count;

    }
    /*
    //This is out of date
    void CurrentPositionUpdate(){

        int len;
        for(int i = 0; i < mergedVertices_num; i++){
            velocity[i] = velocity[i].Multiply(1-Damp*Time.fixedDeltaTime/mass[i]);
        }

        for(int i = 0; i < cluster_num; i++){
            len = ClusterParticle[i].VerticesNum;
            (int[] index, Vector<double>[] delta_velocity) = Operation.Deconstruct(ClusterParticle[i].velocity);
            for(int j = 0; j < len; j++){
                velocity[index[j]] += delta_velocity[j];
            }
        }

        for(int i = 0; i < mergedVertices_num; i++){
            CurrentPosition[i] += velocity[i]*Time.fixedDeltaTime;    
        }

        LastCenterPos = CurrentCenterPos;
        CurrentCenterPos = Operation.average(CurrentPosition);
        
    }
    */
    
    //OverlappingKMeans() contains two parts, including clustering vertices and calculating certain variables used for GPGPU.
    void OverlappingKMeans(int seed_num, float overlap, int max_iter){

        pointsHeadTail_array = new int[2*mergedVertices_num];
        clusterHeadTail_array = new int[2*cluster_num];  

        List<(int id, Vector<double> position)>[] teams = new List<(int id, Vector<double> position)>[seed_num];
        Vector<double>[] seeds = SeedGen(seed_num);
        Vector<double>[] new_seeds = Operation.HardCopyArray(seeds);
        (int id, double distance)[][] distance_matrix = new (int, double)[seed_num][];
        int iter = 0;

        //clustering vertices
        do{
            Clustering(new_seeds, CurrentPosition, teams);
            SeedReset(teams, seeds, new_seeds);

            iter += 1;
            if(iter == max_iter){
                Debug.Log("KMeans reach max iteration!");
            }

        }while(!new_seeds.SequenceEqual(seeds) && !(iter >= max_iter));

        if(overlap != 0){
            for(int i = 0; i < seed_num; i++){

                distance_matrix[i] = new (int id, double distance)[mergedVertices_num];
                for(int j = 0; j < mergedVertices_num; j++){
                    distance_matrix[i][j] = (j, (CurrentPosition[j] - new_seeds[i]).L2Norm());
                }
                Operation.Sort(distance_matrix[i], 0, mergedVertices_num - 1);

                int increase = (int)Math.Floor(overlap*teams[i].Count/(1-overlap));
                int added_num = 0;
                int matrix_index = 0;
                if(teams[i].Count == 0){
                    Debug.LogFormat("Cluster {0} dosen't have any point!", i);
                }
                while(added_num < increase && matrix_index < mergedVertices_num){
                    int point_index = distance_matrix[i][matrix_index].id;
                    if(!teams[i].Exists(x => x.id == point_index)){
                        teams[i].Add((point_index, CurrentPosition[point_index].Clone()));
                        added_num += 1;
                    }
                    matrix_index += 1;
                }
                if(matrix_index == mergedVertices_num){
                    Debug.LogFormat("Cluster {0} have all points in the object!", i);
                }
            }
        }

        int len = 0;
        int head = 0;
        int tail = 0;
        List<int> pointsID = new List<int>();
        List<int>[] clusterIDs = new List<int>[mergedVertices_num];
        List<int> clusterID = new List<int>();
        for(int i = 0; i < mergedVertices_num; i++){
            clusterIDs[i] = new List<int>();
        }
        
        //calculating certain variables used for GPGPU
        for(int i = 0; i < seed_num; i++){
            List<(int id, Vector<double> position)> team = teams[i];
            len = team.Count;

            tail = head + len - 1;
            clusterHeadTail_array[i*2] = head;
            clusterHeadTail_array[i*2 + 1] = tail;
            head = tail + 1;

            for(int j = 0; j < len; j++){
                pointsID.Add(team[j].Item1);
                clusterIDs[team[j].Item1].Add(i);
            }
        }

        len = 0; head = 0; tail = 0;
        for(int i = 0; i < mergedVertices_num; i++){
            len = clusterIDs[i].Count;
            clusterID = clusterID.Concat(clusterIDs[i]).ToList();

            tail = head + len - 1;
            pointsHeadTail_array[i*2] = head;
            pointsHeadTail_array[i*2 + 1] = tail;
            head = tail + 1;
        }
        pointsID_array = pointsID.ToArray();
        clusterID_array = clusterID.ToArray();

        head = 0; tail = 0;
        for(int i = 0; i < seed_num; i++){
            head = clusterHeadTail_array[i*2];
            tail = clusterHeadTail_array[i*2 + 1];
            Transform[i] = CreateMatrix.DenseIdentity<double>(3,dim);
            originalCenterPos[i] = Operation.average(OriginalPosition, pointsID_array, head, tail);
            currentCenterPos[i] = Operation.average(CurrentPosition, pointsID_array, head, tail);
        }        

        //The following commented code is for test clustering by colorful encoding
        /*
        Vector<double> cen = CreateVector.Dense<double>(new double[]{0.0, 0.5, 0.0});        
        for(int i = 0; i < 1; i++){
            foreach(var point in teams[i]){
                CurrentPosition[point.id] -= cen;
                CurrentPosition[point.id] *= 1.5;
                CurrentPosition[point.id] += cen;                
            }
        }
        */
        /*
        Color[] colors = new Color[vertices_num];
        int head_temp;
        int tail_temp;
        for(int i = 0; i < vertices_num; i++){
            int merged_index = mergedVerticesTable[i];
            head_temp = pointsHeadTail_array[2*merged_index];
            tail_temp = pointsHeadTail_array[2*merged_index + 1];
            if((tail_temp - head_temp) > 0){
                colors[i] = new Color(0.0f, 0.0f, 0.0f, 0.0f);
            }
            else if(teams[0].Exists(x => x.id == merged_index)){
                colors[i] = new Color(1.0f, 0.0f, 0.0f, 1.0f);
            }
            else if(teams[1].Exists(x => x.id == merged_index)){
                colors[i] = new Color(0.0f, 1.0f, 0.0f, 1.0f);
            }
            else if(teams[2].Exists(x => x.id == merged_index)){
                colors[i] = new Color(0.0f, 0.0f, 1.0f, 0.0f);
            }
            else if(teams[3].Exists(x => x.id == merged_index)){
                colors[i] = new Color(1.0f, 0.0f, 0.0f, 0.0f);
            }
            else if(teams[4].Exists(x => x.id == merged_index)){
                colors[i] = new Color(0.0f, 1.0f, 0.0f, 0.0f);
            }
            else if(teams[5].Exists(x => x.id == merged_index)){
                colors[i] = new Color(0.0f, 0.0f, 1.0f, 0.0f);
            }
            else if(teams[6].Exists(x => x.id == merged_index)){
                colors[i] = new Color(1.0f, 0.0f, 1.0f, 1.0f);
            }
            else if(teams[7].Exists(x => x.id == merged_index)){
                colors[i] = new Color(0.5f, 0.5f, 0.5f, 1.0f);
            }
        }
        mesh.SetColors(colors);     
        */
    }

    //Clustering() is used in OverlappingKMeans(), it is used to cluster vertices according to the distance between vertices and seeds(centroids).
    void Clustering(in Vector<double>[] seed, in Vector<double>[] position, List<(int id, Vector<double> position)>[] teams){

        int seed_num = seed.Length;
        int point_num = position.Length;
        double min_dis = 99999999.0;
        double distance;
        int nearest_seed = -1;
        Array.Clear(teams, 0, seed_num);
        for(int i = 0; i < seed_num; i++){
            teams[i] = new List<(int id, Vector<double> position)>();
        }

        for(int i = 0; i < point_num; i++){
            for(int j = 0; j < seed_num; j++){
                distance = (position[i] - seed[j]).L2Norm();
                if(distance < min_dis){
                    min_dis = distance;
                    nearest_seed = j;
                }
            }
            teams[nearest_seed].Add((i, position[i].Clone()));
            min_dis = 99999999.0;
        }

    }

    //SeedReset() is used in OverlappingKMeans(), it is used to calculate seeds(centroids) by averaging within each cluster after each clustering()
    void SeedReset(List<(int id, Vector<double> position)>[] teams, Vector<double>[] seeds, Vector<double>[] new_seeds){

        for (int i = 0; i < seeds.Length; i++)
        {
            seeds[i] = new_seeds[i].Clone();
        }

        int seeds_num = teams.Length;
        for (int i = 0; i < seeds_num; i++)
        {
            if (teams[i].Count != 0)
            {
                Vector<double> center = CreateVector.Dense<double>(new double[] { 0, 0, 0 });
                foreach (var point in teams[i]) center += point.position;
                center /= teams[i].Count;
                new_seeds[i] = center;
            }
        }
    }

    //Initialize the seeds(centroids) in OverlappingKMeans()
    Vector<double>[] SeedGen(int seed_num){

        int sample = (int)Math.Floor((double)mergedVertices_num/seed_num);
        Vector<double>[] arr = new Vector<double>[seed_num];
        
        for(int i = 0; i < seed_num; i++){
            arr[i] = CurrentPosition[i*sample];
        }
    
        return arr;
    }

    //Calculate the optimal transformation matrix, linearly superimpose it with rotation matrix decomposed from the optimal transformation matrix, and finally scale the determinant to 1.
    //After the above processing, the transformation matrix of a cluster is computed.
    void TransformGen(int group_index){ //input is vertices, return matrix

        //Calculate the optimal transformation matrix
        int head = clusterHeadTail_array[group_index*2];
        int tail = clusterHeadTail_array[group_index*2 + 1];
        int vertices_num = tail - head + 1;
        int point_index;
        double det;
        float time5 = Time.realtimeSinceStartup;
        _Apq.Clear();
        for(int i = 0; i < vertices_num; i++){
            point_index = pointsID_array[head + i];
            _Apq += ((CurrentPosition[point_index] - currentCenterPos[group_index])).Multiply(mass[point_index]).OuterProduct((OriginalPosition[point_index] - originalCenterPos[group_index]));
            //_Aqq += ((OriginalPosition[point_index] - originalCenterPos[group_index])).Multiply(mass[i]).OuterProduct((OriginalPosition[point_index] - originalCenterPos[group_index]));
        }

        for (int d = 0; d < 3; d++)
        {
            _Apq[d, d] += 0.1;
        }

        float time6 = Time.realtimeSinceStartup;
        //Debug.LogFormat("The ApqAqq take {0} ms", (time6 - time5)*1000);
        //_A = _Apq * _Aqq.Inverse();
        _A = _Apq * _Aqq[group_index];

        //Linearly superimpose it with rotation matrix decomposed from the optimal transformation matrix
        if (quadratic){
            Apq_Square = _Apq.SubMatrix(0,3,0,3);
            SquareRootMatrixCal(Apq_Square.Transpose()*Apq_Square, _Stretching);

            for (int d = 0; d < 3; d++) _Stretching[d, d] += 1e-5;

            _Rotation = (Apq_Square * _Stretching.Inverse()).Append(padding);
            // Transform[group_index] = Beta*_A + (1 - Beta)*_Rotation; 整個物體使用同一個Beta
            double currentBeta = Beta;
            Transform[group_index] = currentBeta * _A + (1 - currentBeta) * _Rotation;
            det = Transform[group_index].SubMatrix(0,3,0,3).Determinant();
        }
        else{
            SquareRootMatrixCal(_Apq.Transpose()*_Apq, _Stretching);

            for (int d = 0; d < 3; d++) _Stretching[d, d] += 1e-5;

            _Rotation = _Apq * _Stretching.Inverse();
            Transform[group_index] = Beta*_A + (1 - Beta)*_Rotation;
            det = Transform[group_index].Determinant();
        }

        //Scale the determinant to 1
        if (double.IsNaN(det) || double.IsInfinity(det))
        {
            Transform[group_index] = CreateMatrix.DenseIdentity<double>(3, dim);
            return;
        }

        det = Math.Sign(det) * Math.Pow(Math.Abs(det), 1.0 / 3.0);
        if (double.IsNaN(det) || double.IsInfinity(det) || Math.Abs(det) < 1e-5)
        {
            det = (det >= 0 || double.IsNaN(det)) ? 1e-5 : -1e-5;
        }
        Transform[group_index] = Transform[group_index].Divide(det);

        for (int r = 0; r < 3; r++)
        {
            for (int c = 0; c < dim; c++)
            {
                double v = Transform[group_index][r, c];
                if (double.IsNaN(v) || double.IsInfinity(v))
                {
                    Transform[group_index] = CreateMatrix.DenseIdentity<double>(3, dim);
                    return;
                }
            }
        }
        //Debuger.MatrixWrite(_Apq);
        //Debuger.MatrixWrite(_Aqq);
        //Debuger.MatrixWrite(_A);
        //Debuger.MatrixWrite(_Rotation);
        //Debuger.MatrixWrite(Transform[group_index]);  

    }

    //Calculate the square root of a square matrix
    void SquareRootMatrixCal(Matrix<double> matrix, Matrix<double> ans){
        _EvdStorage = matrix.Evd();
        _EigenValue = _EvdStorage.D;
        _EigenVectors = _EvdStorage.EigenVectors;

        for (int i = 0; i < _EigenValue.RowCount; i++)
        {
            if (_EigenValue[i, i] < 0)
            {
                _EigenValue[i, i] = 0;
            }
        }

        _EigenValue = Matrix<double>.Sqrt(_EigenValue);
        double[] t = {_EigenValue[0,0],_EigenValue[1,1],_EigenValue[2,2]};
        double value;
        for(int i = 0; i < 3; i++){
            for(int j = 0; j < 3; j++){
                value = 0;
                for(int k = 0; k < 3; k++){
                    value += t[k]*_EigenVectors[i,k]*_EigenVectors[j,k];
                }
                ans.At(i,j,value);
            }
        }
    }

    //Set the transformation matrices to buffer and run the computer shader via Dispatch()
    void TransformSet(){
        for(int i = 0; i < cluster_num; i++){
            for(int row = 0; row < 3; row ++){
                for(int col = 0; col < dim; col ++){
                    transform_array[i*3*dim + row*dim + col] = Transform[i].At(row,col);
                }
            }
        }
        TransformBuffer.SetData(transform_array);
        DeformAlgorithm.SetBuffer(ShapeMatchingKernelIndex, "Transform", TransformBuffer);
        DeformAlgorithm.Dispatch(ShapeMatchingKernelIndex, mergedVertices_num/64 + 1, 1, 1); //run the computer shader via Dispatch()
    }

    void CenterUpdate(){
        int head = 0;
        int tail = 0;
        for(int i = 0; i < cluster_num; i++){
            head = clusterHeadTail_array[i*2];
            tail = clusterHeadTail_array[i*2 + 1];
            currentCenterPos[i] = Operation.average(CurrentPosition, pointsID_array, head, tail);

            double returnForce = 0.15;
            // 產生一個帶有拉力的假重心傳給 GPU
            Vector<double> gpuCenter = currentCenterPos[i] * (1.0 - returnForce) + originalCenterPos[i] * returnForce;

            for (int j = 0; j < 3; j++){
                center_array[i*(dim+3) + dim + j] = gpuCenter.At(j);
            }
        }
        CenterBuffer.SetData(center_array);
    }
}

//This is out of date
class ShapeMatching{
    private int _VerticesNum;
    private int[] _IndexTable;
    private double[] _mass;
    private float _alpha;
    private float _beta;
    private float _damp;
    private Vector<double>[] _OriginalPosition; // This is the original position of MergedVertices
    private Vector<double>[] _CurrentPosition;  // This is the orcurrent position of MergedVertices
    private Vector<double>[] _GoalPosition;     // This is the goal position of MergedVertices, and it is local coordinate
    private Vector<double>[] _velocity;
    private Vector<double> _OriginalCenterPos;  //This is local coordinate
    private Vector<double> _CurrentCenterPos;   //This is local coordinate

    private Matrix<double> _Apq;
    private Matrix<double> _Aqq;
    private Matrix<double> _A;
    private Matrix<double> _Rotation;
    private Matrix<double> _Stretching;
    private Matrix<double> _Transformation;
    private Evd<double> _EvdStorage;
    private Matrix<double> _EigenValue;
    private Matrix<double> _EigenVectors;

    private double[] transform;
    private ComputeBuffer TransformBuffer;
    private double[] center;
    private ComputeBuffer CenterBuffer;
    private ComputeShader Shader;
    private int ShapeMatchingKernelIndex;

    public int[] PointsIndex{
        get{
            return _IndexTable;
        }
    }

    public Vector<double> CurrentCenter{
        get{
            return _CurrentCenterPos;
        }
    }

    public Vector<double> OriginalCenter{
        get{
            return _OriginalCenterPos;
        }
    }

    public (int, Vector<double>)[] velocity{
        get{
            return Operation.Construct<int, Vector<double>>(_IndexTable, _velocity);
        }
    }

    public int VerticesNum{
        get{
            return _VerticesNum;
        }
    }

    public Vector<double>[] CurrentPosition{
        set{
            _CurrentPosition = value;
        }
    }

    public ShapeMatching((int id, Vector<double> position)[] Points, float Mass, float Alpha, float Beta, float Damp, ComputeShader shader, int KernelIndex){
        
        var point_tuple = Operation.Deconstruct<int, Vector<double>>(Points);
        this._VerticesNum = Points.Length;
        this._alpha = Alpha;
        this._beta = Beta;
        this._damp = Damp;
        this._mass = Operation.DoubleArrayInit(this.VerticesNum, Mass);
        this._IndexTable = point_tuple.Item1;
        this._OriginalPosition = point_tuple.Item2;
        this._CurrentPosition = Operation.CopyArray(this._OriginalPosition);//it seems not necessary, because it will be cover by "DetectPosition"
        this._GoalPosition = new Vector<double>[this._VerticesNum];
        this._velocity = Operation.VectorArrayInit(this._VerticesNum, 3, 0);
        this._OriginalCenterPos = Operation.average(this._OriginalPosition);
        this._CurrentCenterPos = this._OriginalCenterPos;

        this._Apq = CreateMatrix.Dense<double>(3,3,0);
        this._Aqq = CreateMatrix.Dense<double>(3,3,0);
        this._Stretching = CreateMatrix.Dense<double>(3,3,0);
        this._Transformation = CreateMatrix.Dense<double>(3,3,0);

        this.transform = new double[9];
        //this.TransformBuffer = new ComputeBuffer(9, sizeof(double));
        this.center = new double[]{this._OriginalCenterPos[0],
                                   this._OriginalCenterPos[1],
                                   this._OriginalCenterPos[2],
                                   this._CurrentCenterPos[0],
                                   this._CurrentCenterPos[1],
                                   this._CurrentCenterPos[2]};
        //this.CenterBuffer = new ComputeBuffer(2, 3*sizeof(double));
        this.Shader = shader;
        this.ShapeMatchingKernelIndex = KernelIndex;

        this.CenterBuffer.SetData(this.center);
        this.Shader.SetBuffer(this.ShapeMatchingKernelIndex, "Center", CenterBuffer);
   }

    public void Execute(){
        float time1 = Time.realtimeSinceStartup;
        TransformGen();
        float time2 = Time.realtimeSinceStartup;
        TransformSet();
        /*
        CalculateGoal();
        float time3 = Time.realtimeSinceStartup;
        Integration();
        float time4 = Time.realtimeSinceStartup;
        */
        //Debug.LogFormat("The TransformGen take {0} ms", (time2 - time1)*1000.0);
        //Debug.LogFormat("The CalculateGoal take {0} ms", (time3 - time2)*1000);
        //Debug.LogFormat("The Integration take {0} ms", (time4 - time3)*1000);
    } 

    public void DetectPosition(Vector<double>[] position){
        for(int i = 0; i < _VerticesNum; i++){
            _CurrentPosition[i] = position[_IndexTable[i]];
        }
        _CurrentCenterPos = Operation.average(_CurrentPosition);
        
        for(int i = 0; i < 3; i++){
            center[3 + i] = _CurrentCenterPos[i];
        }
        CenterBuffer.SetData(center);
        //Debug.Log(_OriginalCenterPos);
    }

    void TransformGen(){ //input is vertices, return matrix

        double det;
        float time5 = Time.realtimeSinceStartup;
        //_Apq.Clear(); _Aqq.Clear(); //it will not converge if this line work
        for(int i = 0; i < _VerticesNum; i++){
            _Apq += ((_CurrentPosition[i] - _CurrentCenterPos)).Multiply(_mass[i]).OuterProduct((_OriginalPosition[i] - _OriginalCenterPos));
            _Aqq += ((_OriginalPosition[i] - _OriginalCenterPos)).Multiply(_mass[i]).OuterProduct((_OriginalPosition[i] - _OriginalCenterPos));
            //Debug.Log(_OriginalPosition[i]);
            //Debug.Log(_CurrentPosition[i]);
        }
        //Debug.Log("-----");
        float time6 = Time.realtimeSinceStartup;
        //Debug.LogFormat("The ApqAqq take {0} ms", (time6 - time5)*1000);
        _A = _Apq * _Aqq.Inverse();
        SquareRootMatrixCal(_Apq.Transpose()*_Apq, _Stretching);
        _Rotation = _Apq * _Stretching.Inverse();
        _Transformation = _beta*_A + (1 - _beta)*_Rotation;
        det = _Transformation.Determinant();
        det = Math.Pow(det,1.0/3);
        _Transformation = _Transformation.Divide(det);
        //Debuger.MatrixWrite(_Transformation);
    }

    void SquareRootMatrixCal(Matrix<double> matrix, Matrix<double> ans){
        _EvdStorage = matrix.Evd();
        _EigenValue = _EvdStorage.D;
        _EigenVectors = _EvdStorage.EigenVectors;
        _EigenValue = Matrix<double>.Sqrt(_EigenValue);
        double[] t = {_EigenValue[0,0],_EigenValue[1,1],_EigenValue[2,2]};
        double value;
        for(int i = 0; i < 3; i++){
            for(int j = 0; j < 3; j++){
                value = 0;
                for(int k = 0; k < 3; k++){
                    value += t[k]*_EigenVectors[i,k]*_EigenVectors[j,k];
                }
                ans.At(i,j,value);
            }
        }
    }

    void TransformSet(){
        for(int row = 0; row < 3; row ++){
            for(int col = 0; col < 3; col ++){
                transform[row*3 + col] = _Transformation.At(row,col);
            }
        }

        TransformBuffer.SetData(transform);
        Shader.SetBuffer(ShapeMatchingKernelIndex, "Transform", TransformBuffer);
        Shader.Dispatch(ShapeMatchingKernelIndex, 1, 1, 1);
    }

    void CalculateGoal(){
        for(int i = 0; i < _VerticesNum; i++){
            _GoalPosition[i] = _Transformation*(_OriginalPosition[i] - _OriginalCenterPos);
        }
    }

    void Integration(){
        for(int i = 0; i < _VerticesNum; i++){
            _velocity[i] = _alpha*(_GoalPosition[i] - _CurrentPosition[i] + _CurrentCenterPos);
        }
    }

    double[] mass_Gen(int len, double M){
        _mass = new double[len];
        for(int i = 0; i < len; i++){
            _mass[i] = M;
        }
        return _mass;
    }
}

//This class includes several funcitons for debugging.
public static class Debuger{

    public static void MatrixWrite(Matrix<Double> A){
        int rows = A.RowCount;
        int cols = A.ColumnCount;
        var info = new StringBuilder();
        info.Append("\n");
        for(int i = 0; i < rows; i++){ 
            for(int j = 0; j < cols; j++){
                info.Append(A[i,j]);
                info.Append(" ");
            }
            info.Append("\n");
        }        
        Debug.Log(info);
    }

    public static void CommandPosition_Gen(Vector<double>[] CurrentPosition, Vector<double> CurrentCenterPos){
        //CurrentPosition[0] += CreateVector.Dense<double>(new double[]{-0.5,-0.5,-0.5});
        CurrentPosition[0] *= 1.5f;
        //CurrentPosition[0] = (CurrentPosition[0] - CreateVector.Dense<double>(new double[]{0.0,0.5,0.0}))*1.5 + CreateVector.Dense<double>(new double[]{0.0,0.5,0.0});
        CurrentCenterPos = Operation.average(CurrentPosition);
    }

    public static void CommandPosition_Gen(Vector<double>[] CurrentPosition, int num, float probability, float amp){
        if(UnityEngine.Random.Range(0.0f,1.0f) <= probability){
            CurrentPosition[UnityEngine.Random.Range(0,num-1)] += CreateVector.Dense<double>(new double[]{UnityEngine.Random.Range(-amp,amp),
                                                                                                          UnityEngine.Random.Range(-amp,amp),
                                                                                                          UnityEngine.Random.Range(-amp,amp)});
        }       
    }

    public static void CommandPosition_Gen(Vector<double>[] CurrentPosition, double[] currentPos_array, ComputeBuffer CurrentPosBuffer){
        CurrentPosition[0] *= 1.5f;
        for(int i = 0; i < 3; i++){
            currentPos_array[i] *= 1.5f;
        }
        CurrentPosBuffer.SetData(currentPos_array);
    }

    public static void VectorArrayCheck(Vector<double>[] Position){
        int len = Position.Length;
        for(int i = 0; i < len; i++){
            Debug.LogFormat("Vector {0} is {1}", i, Position[i]);
        }
        Debug.Log("--------------");
    }

    public static void VectorArrayCheck(Vector3[] Position){
        int len = Position.Length;
        for(int i = 0; i < len; i++){
            Debug.LogFormat("Vector {0} is {1}", i, Position[i]);
        }
        Debug.Log("--------------");
    }
}

//This class includes some general math calculations and type transformation.
public static class Operation{

    public static Matrix<double>[] Aqq_Init(Vector<double>[] OriginalPosition, double[] mass, Vector<double>[] originalCenterPos, int[] clusterHeadTail_array, int[] pointsID_array, int cluster_num, int dim){
        Matrix<double>[] _Aqq = new Matrix<double>[cluster_num];
        for(int group_index = 0; group_index < cluster_num; group_index++){
            int head = clusterHeadTail_array[group_index*2];
            int tail = clusterHeadTail_array[group_index*2 + 1];
            int vertices_num = tail - head + 1;
            int point_index;
            _Aqq[group_index] = CreateMatrix.Dense<double>(dim,dim,0);
            for(int j = 0; j < vertices_num; j++){
                point_index = pointsID_array[head + j];
                _Aqq[group_index] += ((OriginalPosition[point_index] - originalCenterPos[group_index])).Multiply(mass[point_index]).OuterProduct((OriginalPosition[point_index] - originalCenterPos[group_index]));
            }

            // 加入微小偏移量防止除以零
            for (int d = 0; d < dim; d++)
            {
                _Aqq[group_index][d, d] += 0.1;
            }

            _Aqq[group_index] = _Aqq[group_index].Inverse();
        }
        return _Aqq;
    }

    public static Vector<double>[] QuadraticMapping(Vector<double>[] vectors, double[] coefs){
        int len = vectors.Length;
        Vector<double>[] ans = new Vector<double>[len];
        double x; double y; double z;
        for(int i = 0; i < len; i++){
            x = vectors[i].At(0);
            y = vectors[i].At(1);
            z = vectors[i].At(2);
            ans[i] = CreateVector.Dense<double>(new double[]{coefs[0]*x, coefs[1]*y, coefs[2]*z, 
                                                             coefs[3]*x*x, coefs[4]*y*y, coefs[5]*z*z, 
                                                             coefs[6]*x*y, coefs[7]*y*z, coefs[8]*z*x});
        }
        return ans;
    }

    public static Vector<double> QuadraticMapping(Vector<double> vectors, double[] coefs){
        double x; double y; double z;
        x = vectors.At(0);
        y = vectors.At(1);
        z = vectors.At(2);
        return CreateVector.Dense<double>(new double[]{coefs[0]*x, coefs[1]*y, coefs[2]*z, 
                                                             coefs[3]*x*x, coefs[4]*y*y, coefs[5]*z*z, 
                                                             coefs[6]*x*y, coefs[7]*y*z, coefs[8]*z*x});
    }

    public static (T1[], T2[]) Deconstruct<T1,T2>((T1 item1, T2 item2)[] arr){
        int len = arr.Length;
        T1[] arr1 = new T1[len];
        T2[] arr2 = new T2[len];

        for(int i = 0; i < len; i++){
            arr1[i] = arr[i].item1;
            arr2[i] = arr[i].item2;
        }
        return (arr1, arr2);
    }

    public static (T1, T2)[] Construct<T1, T2>(T1[] arr1, T2[] arr2){
        int len = arr1.Length;
        (T1, T2)[] arr = new (T1, T2)[len];
        for(int i = 0; i < len; i++){
            arr[i] = (arr1[i], arr2[i]);
        }
        return arr;
    }

    public static double[] DoubleArrayInit(int len, double M){
        double[] arr = new double[len];
        for(int i = 0; i < len; i++){
            arr[i] = M;
        }
        return arr;
    }

    public static int[] IncrementIntArrayInit(int len, int first_value, int step){
        int[] arr = new int[len];
        int value = first_value;
        for(int i = 0; i < len; i++){
            arr[i] = value;
            value += step;
        }
        return arr;
    }
    
    public static Vector3[] add(this Vector3[] arr1, Vector3[] arr2){
        int len = arr1.Length;
        if(arr2.Length != len){
            throw new System.ArgumentException("Two array are not the same length!");
        }
        else{
            for(int i = 0; i < len; i++){
                arr1[i] = arr1[i] + arr2[i];
            }
        }        
        return arr1;
    }
    
    /*
    public static void Vector3toVector(Vector3 origin, ref Vector ans){
        ans.x = (double)origin.x;
        ans.y = (double)origin.y;
        ans.z = (double)origin.z;
    }

    public static Vector DoubleVector2Vector(Vector<double> input){
        Vector ans = new Vector();
        ans.x = input.At(0);
        ans.y = input.At(1);
        ans.z = input.At(2);
        return ans;
    }

    public static Vector[] StructVectorArrayInit(int len, double value){
        Vector[] ans = new Vector[len];
        for(int i = 0; i < len; i++){
            Vector temp = new Vector();
            temp.x = value;
            temp.y = value;
            temp.z = value;
            ans[i] = temp;
        }
        return ans;
    }
    */
    public static void DoubleArray2DoubleVectorArray(ref Vector<double>[] output, double[] input){
        int len = output.Length;
        for(int i = 0; i < len; i++){
            for(int j = 0; j < 3; j++){
                output[i].At(j, input[i*3 + j]);
            }
        }
    }

    public static void DoubleVectorArray2DoubleArray(ref double[] output, Vector<double>[] input){
        int len = input.Length;
        for(int i = 0; i < len; i++){
            for(int j = 0; j < 3; j++){
                output[3*i + j] = input[i].At(j);
            }
        }
    }
    
    public static ComputeBuffer PositionBufferInit(Vector<double>[] PosArray){
        int len = PosArray.Length;
        ComputeBuffer ans = new ComputeBuffer(3*len, sizeof(double));
        double[] ans_array = new double[3*len];

        for(int i = 0; i < len; i ++){
            for(int j = 0; j < 3; j ++){
                ans_array[i*3 + j] = PosArray[i].At(j);
            }
        }
        ans.SetData(ans_array);
        return ans;
    }

    public static ComputeBuffer PositionBufferInit(ref double[] PosArray, Vector<double>[] PosVector){
        int len = PosVector.Length;
        int dim = PosVector[0].Count;
        ComputeBuffer ans = new ComputeBuffer(dim*len, sizeof(double));

        for(int i = 0; i < len; i ++){
            for(int j = 0; j < dim; j ++){
                PosArray[i*dim + j] = PosVector[i].At(j);
            }
        }
        ans.SetData(PosArray);
        return ans;
    }
    
    public static Vector<double>[] List2Array(List<Vector3> list){
        int len = list.Count;
        Vector<double>[] ans = new Vector<double>[len];
        for(int i = 0; i < len; i++){
            ans[i] = CreateVector.Dense<double>(new double[]{list[i].x, list[i].y, list[i].z});
        }
        return ans;
    }

    public static Vector<double>[] CopyArray(Vector<double>[] arr){//This function and the below function seems very dangerous, maybe they should be modified
        int len = arr.Length;
        Vector<double>[] CopyArr = new Vector<double>[len];
        for(int i = 0; i < len; i++){
            CopyArr[i] = arr[i];
        }        
        
        return CopyArr;
    }

    public static Vector<double>[] HardCopyArray(Vector<double>[] arr){
        int len = arr.Length;
        Vector<double>[] CopyArr = new Vector<double>[len];
        for(int i = 0; i < len; i++){
            CopyArr[i] = arr[i].Clone();
        }
        return CopyArr;
    }

    public static void CopyArray(this Vector<double>[] arr_output, Vector<double>[] arr_input){
        int len = arr_input.Length;
        for(int i = 0; i < len; i++){
            arr_output[i] = arr_input[i];
        }
    }
    
    public static Vector<double>[] VectorArrayInit(int len, int dimension, double value){
        Vector<double>[] ans = new Vector<double>[len];
        for(int i = 0; i < len; i++){
            ans[i] = CreateVector.Dense<double>(dimension,value);
        }
        return ans;
    }

    public static Vector3[] Vector3ArrayInit(int len, int dimension, float value){
        Vector3[] ans = new Vector3[len];
        for(int i = 0; i < len; i++){
            ans[i] = new Vector3(value,value,value);
        }
        return ans;
    }

    public static Vector3[] DoubleVectorArray2FloatVectorArray(Vector<double>[] arr){
        int len = arr.Length;
        Vector3[] ans = new Vector3[len];
        for(int i = 0; i < len; i++){
            ans[i] = new Vector3((float)arr[i].At(0), (float)arr[i].At(1), (float)arr[i].At(2));
        }
        return ans;
    }

    public static Vector<double>[] FloatVectorArray2DoubleVectorArray(Vector3[] arr){
        int len = arr.Length;
        Vector<double>[] ans = new Vector<double>[len];
        for(int i = 0; i < len; i++){
            ans[i] = CreateVector.Dense<double>(new double[]{arr[i].x, arr[i].y, arr[i].z});
        }
        return ans;
    }

    public static Vector<double> FloatVector2DoubleVector(Vector3 vector){
        Vector<double> ans = CreateVector.Dense<double>(new double[]{vector.x, vector.y, vector.z});
        return ans;
    }

    public static Vector3 DoubleVector2FloatVector(){
        Vector3 ans = new Vector3(0,0,0);
        return ans;
    }

    //Sort() and Merge() together are a merge sort algorithm. In this script, it has two different overloads.
    public static void Sort((int index, double distance)[] arr, int left, int right){ //sort MergedVertices, from small to big
        if(left < right){
            int mid = (left + right)/2;
            Sort(arr, left, mid);
            Sort(arr, mid + 1, right);
            Merge(arr, left, mid, right);
        }
    }

    public static void Merge((int index, double distance)[] arr, int left, int mid, int right){
        int left_len = mid - left + 1;
        int right_len = right - mid;
        (int index, double distance)[] LeftTemp = new (int index, double distance)[left_len];
        (int index, double distance)[] RightTemp = new (int index, double distance)[right_len];
        for(int t = 0; t < left_len; t++){
            LeftTemp[t] = arr[left + t];
        }
        for(int t = 0; t < right_len; t++){
            RightTemp[t] = arr[mid + 1 + t];
        }

        int i = 0; int j = 0; int k = left;
        while(i < left_len && j < right_len){
            if(LeftTemp[i].distance <= RightTemp[j].distance){ 
                arr[k++] = LeftTemp[i++];
            }
            else{
                arr[k++] = RightTemp[j++];
            }
        }
        while(i < left_len){
            arr[k++] = LeftTemp[i++];
        }
        while(j < right_len){
            arr[k++] = RightTemp[j++];
        }
    }

    public static void Sort(Vector<double>[] arr, int left, int right, int coordinate){ //sort MergedVertices, from small to big
        if(left < right){
            int mid = (left + right)/2;
            Sort(arr, left, mid, coordinate);
            Sort(arr, mid + 1, right, coordinate);
            Merge(arr, left, mid, right, coordinate);
        }
    }

    public static void Merge(Vector<double>[] arr, int left, int mid, int right, int coordinate){
        int left_len = mid - left + 1;
        int right_len = right - mid;
        Vector<double>[] LeftTemp = new Vector<double>[left_len];
        Vector<double>[] RightTemp = new Vector<double>[right_len];
        for(int t = 0; t < left_len; t++){
            LeftTemp[t] = arr[left + t];
        }
        for(int t = 0; t < right_len; t++){
            RightTemp[t] = arr[mid + 1 + t];
        }

        int i = 0; int j = 0; int k = left;
        while(i < left_len && j < right_len){
            if(LeftTemp[i][coordinate] <= RightTemp[j][coordinate]){ 
                arr[k++] = LeftTemp[i++];
            }
            else{
                arr[k++] = RightTemp[j++];
            }
        }
        while(i < left_len){
            arr[k++] = LeftTemp[i++];
        }
        while(j < right_len){
            arr[k++] = RightTemp[j++];
        }
    }

    public static Vector<double> average(Vector<double>[] a, int[] label, int head, int tail){
        int len = tail - head + 1;
        int dim = a[0].Count;
        Vector<double> ans = CreateVector.Dense<double>(dim,0);
        for(int i = 0; i < len; i++){
            ans += a[label[head + i]];
        }
        ans /= len;
        return ans;
    }

    public static Vector<double> average(Vector<double>[] a){
        if (a == null || a.Length == 0)
        {
            return CreateVector.Dense<double>(3, 0.0);
        }
        int len = a.Length;
        Vector<double> ans = CreateVector.Dense<double>(3,0);
        for(int i = 0; i < len ; i++){
            ans += a[i];
        }
        ans /= len;
        return ans;
    }

    public static void average(Vector<double>[] a, ref Vector<double> ans){ 
        int len = a.Length;
        ans.Clear();
        for(int i = 0; i < len; i++){
            ans += a[i];
        }
        ans /= len;
    }
}

//This class is used to check memory usage
class MemoryWatch{

    private long _lastTotalMemory = 0;
    private long _MemorySizeChange = 0;
    private bool _forceGC = false;

    public MemoryWatch(bool forceGC){
        _forceGC = forceGC;
    }
    
    public MemoryWatch() : this(false) { }

    public void Start(){
        _lastTotalMemory = GC.GetTotalMemory(_forceGC);
    }

    public void Stop(){
        _MemorySizeChange = GC.GetTotalMemory(_forceGC) - _lastTotalMemory;
    }

    public string MemorySizeChangeInKB{
        get{
            return string.Format("{0:N0}KB", _MemorySizeChange / 1024.0);
        }
    }

    public string MemorySizeChangeInMB{
        get{
            return string.Format("{0:N0}MB", _MemorySizeChange / 1024.0 / 1024.0);
        }
    }

    public string MemorySizeChangeInByte{
        get{
            return string.Format("{0:N0}Byte", _MemorySizeChange);
        }
    }
}