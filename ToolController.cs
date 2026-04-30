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
    private Vector3 velocity;
    public float commandFollow = 18f;

    void Start()
    {
        logicPosition = transform.position;
        commandedPosition = logicPosition;
    }

    void Update()
    {
        HandleInput();

        SendToDeformable();

        // 讓邏輯控制點也受到反作用，避免僅視覺彈開
        ApplyLogicBackdrive();

        // 回寫修正後位置
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
        velocity = inputDir * moveSpeed;

        commandedPosition += velocity * Time.deltaTime;

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

    void SendToDeformable()
    {
        if (deformable == null) return;

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