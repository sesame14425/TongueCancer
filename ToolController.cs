using UnityEngine;

public class ToolController : MonoBehaviour
{
    [Header("Tool Settings")]
    public float radius = 0.05f; // 稍微加大半徑，變形會更明顯
    public float moveSpeed = 2.0f;

    [Header("Reference")]
    public DeformableBody deformable;
    public Transform visualMesh; // 請在 Unity 中將工具的顯示模型拉到這

    [Header("Haptic & Proxy Settings")]
    public Vector3 reactionForce; // 來自 DeformableBody 的反饋力
    [Range(0.1f, 10f)] public float stiffness = 1.0f; // 視覺抗穿透強度

    private Vector3 logicPosition; // 這是玩家控制的 "God" (HIP)
    private Vector3 velocity;

    void Start()
    {
        // 初始時，邏輯位置等於實體位置
        logicPosition = transform.position;
    }

    void Update()
    {
        HandleInput();
        
        // 核心邏輯：將邏輯位置傳給變形系統，獲取反饋
        SendToDeformable();
        
        // 視覺修正：根據反饋力，計算不穿透的視覺位置
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
        velocity = inputDir * moveSpeed;

        // 玩家控制的是 logicPosition，它會「穿進去」組織
        logicPosition += velocity * Time.deltaTime;
        
        // 為了方便 Debug，我們可以讓腳本所在的 GameObject 跟隨 logicPosition
        transform.position = logicPosition;
    }

    void SendToDeformable()
    {
        if (deformable == null) return;

        // 傳遞 logicPosition 進去計算穿透深度
        deformable.externalPos = logicPosition;
        deformable.externalRadius = radius;
        deformable.externalVelocity = velocity;
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