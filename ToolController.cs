using UnityEngine;

public class ToolController : MonoBehaviour
{
    [Header("Tool Settings")]
    public float radius = 0.01f;
    public float moveSpeed = 0.2f;

    [Header("Reference")]
    public DeformableBody deformable; // 指向舌頭

    [Header("Haptic Feedback")]
    public Vector3 reactionForce; // 從舌頭回來的力

    private Vector3 velocity;

    void Update()
    {
        HandleInput();
        SendToDeformable();
        ApplyVisualFeedback();
    }

    void HandleInput()
    {
        float h = Input.GetAxis("Horizontal");
        float v = Input.GetAxis("Vertical");
        float up = 0f;

        if (Input.GetKey(KeyCode.E)) up += 1f;
        if (Input.GetKey(KeyCode.Q)) up -= 1f;

        Vector3 input = new Vector3(h, up, v);

        velocity = input * moveSpeed;
        transform.position += velocity * Time.deltaTime;
    }

    void SendToDeformable()
    {
        if (deformable == null) return;

        deformable.externalPos = transform.position;
        deformable.externalRadius = radius;
        deformable.externalVelocity = velocity;
    }

    void ApplyVisualFeedback()
    {
        // 小小視覺反饋（可選）
        transform.localScale = Vector3.one * (1f + reactionForce.magnitude * 0.05f);
    }

    // 給 DeformableBody 呼叫
    public void SetReactionForce(Vector3 force)
    {
        reactionForce = force;
    }
}