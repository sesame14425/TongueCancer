using UnityEngine;

public class ToolController : MonoBehaviour
{
    [Header("Tool Settings")]
    public float radius = 0.05f;
    public float moveSpeed = 2.0f;

    [Header("Reference")]
    public DeformableBody deformable;
    public Transform visualMesh;

    [Header("Haptic & Proxy Settings")]
    public Vector3 reactionForce;
    [Range(0.1f, 10f)] public float stiffness = 1.0f;
    [Range(0f, 10f)] public float logicBackdrive = 1.5f;
    public float maxBackdriveSpeed = 1.0f;

    private Vector3 logicPosition;
    private Vector3 commandedPosition;
    private Vector3 inputVelocity;
    private Vector3 logicVelocity;
    private Vector3 lastLogicPosition;
    public float commandFollow = 18f;

    public SphereCollider toolCollider;   // 指到自己的 SphereCollider
    public Collider tissueCollider;       // 指到組織的 MeshCollider
    public int projectionIterations = 3;  // 多次投影更穩
    public float projectionSlop = 0.0005f;

    void Start()
    {
        logicPosition = transform.position;
        commandedPosition = logicPosition;
        lastLogicPosition = logicPosition;
        inputVelocity = Vector3.zero;
        logicVelocity = Vector3.zero;
    }

    void Update()
    {
        HandleInput();

        // 讓邏輯控制點也受到反作用，避免僅視覺彈開
        ApplyLogicBackdrive();

        // 以實際邏輯點位移計算速度，避免只用輸入速度導致接觸估算失真
        float dt = Mathf.Max(1e-6f, Time.deltaTime);
        logicVelocity = (logicPosition - lastLogicPosition) / dt;
        lastLogicPosition = logicPosition;

        ResolvePenetrationProjection();

        // 回寫修正後位置與速度
        SendToDeformable();

        UpdateVisualProxy();
    }

    void HandleInput()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float up = 0f;
        if (Input.GetKey(KeyCode.E)) up += 1f;
        if (Input.GetKey(KeyCode.Q)) up -= 1f;

        Vector3 inputDir = new Vector3(h, up, v);
        inputVelocity = inputDir * moveSpeed;

        commandedPosition += inputVelocity * Time.deltaTime;

        float followT = 1f - Mathf.Exp(-commandFollow * Time.deltaTime);
        logicPosition = Vector3.Lerp(logicPosition, commandedPosition, followT);
        transform.position = logicPosition;
    }

    void ApplyLogicBackdrive()
    {
        if (stiffness <= 1e-5f) return;
        if (reactionForce.sqrMagnitude <= 1e-8f) return;

        Vector3 backdriveVelocity = reactionForce * (logicBackdrive / stiffness);
        if (maxBackdriveSpeed > 0f)
            backdriveVelocity = Vector3.ClampMagnitude(backdriveVelocity, maxBackdriveSpeed);

        logicPosition += backdriveVelocity * Time.deltaTime;
        transform.position = logicPosition;
    }

    void ResolvePenetrationProjection()
    {
        if (toolCollider == null || tissueCollider == null) return;

        // 多迭代幾次，避免一次推不乾淨
        for (int it = 0; it < projectionIterations; it++)
        {
            Vector3 dir;
            float dist;

            bool overlapped = Physics.ComputePenetration(
                toolCollider, toolCollider.transform.position, toolCollider.transform.rotation,
                tissueCollider, tissueCollider.transform.position, tissueCollider.transform.rotation,
                out dir, out dist
            );

            if (!overlapped) break;

            // 把工具往外推出去（dir 是“把 tool 推離 tissue”的方向）
            Vector3 correction = dir * (dist + projectionSlop);

            logicPosition += correction;
            commandedPosition += correction; // 很重要：不然下一幀 commanded 又把你拉回穿透
            transform.position = logicPosition;
        }
    }

    void SendToDeformable()
    {
        if (deformable == null) return;

        deformable.externalPos = logicPosition;
        deformable.externalRadius = radius;
        deformable.externalVelocity = logicVelocity;
    }

    void UpdateVisualProxy()
    {
        if (visualMesh == null) return;

        Vector3 rawOffset = reactionForce * (1.0f / stiffness);

        float maxDist = radius * 2.0f;
        Vector3 clampedOffset = Vector3.ClampMagnitude(rawOffset, maxDist);

        visualMesh.position = Vector3.Lerp(visualMesh.position, logicPosition + clampedOffset, 0.2f);
    }

    public void SetReactionForce(Vector3 force)
    {
        reactionForce = force;
    }
}
