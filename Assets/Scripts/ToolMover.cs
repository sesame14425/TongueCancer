using UnityEngine;

public class ToolMover : MonoBehaviour
{
    public float moveSpeed = 0.5f;
    public Vector3 reactionForce;
    [Range(0.1f, 10f)] public float stiffness = 1.0f;
    [Range(0f, 10f)] public float logicBackdrive = 1.5f;
    public float maxBackdriveSpeed = 1.0f;

    public SphereCollider toolCollider;
    public Collider tissueCollider;
    public int projectionIterations = 3;
    public float projectionSlop = 0.0005f;
    public float commandFollow = 18f;

    private const float MinDeltaTime = 1e-6f;
    private Vector3 commandedPosition;
    private Vector3 logicPosition;
    private Vector3 lastLogicPosition;
    private Vector3 logicVelocity;

    private void Start()
    {
        logicPosition = transform.position;
        commandedPosition = logicPosition;
        lastLogicPosition = logicPosition;
        logicVelocity = Vector3.zero;
    }

    void Update()
    {
        HandleInput();

        ApplyLogicBackdrive();
        ResolvePenetrationProjection();

        float dt = Mathf.Max(MinDeltaTime, Time.deltaTime);
        logicVelocity = (logicPosition - lastLogicPosition) / dt;
        lastLogicPosition = logicPosition;

        transform.position = logicPosition;
    }

    private void HandleInput()
    {
        float h = Input.GetAxis("Horizontal"); // A/D
        float v = Input.GetAxis("Vertical");   // W/S
        float up = 0f;
        if (Input.GetKey(KeyCode.E)) up += 1f;
        if (Input.GetKey(KeyCode.Q)) up -= 1f;

        Vector3 dir = new Vector3(h, up, v);
        commandedPosition += dir * moveSpeed * Time.deltaTime;

        float followT = 1f - Mathf.Exp(-commandFollow * Time.deltaTime);
        logicPosition = Vector3.Lerp(logicPosition, commandedPosition, followT);
    }

    private void ApplyLogicBackdrive()
    {
        if (stiffness <= 1e-5f) return;
        if (reactionForce.sqrMagnitude <= 1e-8f) return;

        Vector3 backdriveVelocity = reactionForce * (logicBackdrive / stiffness);
        if (maxBackdriveSpeed > 0f)
            backdriveVelocity = Vector3.ClampMagnitude(backdriveVelocity, maxBackdriveSpeed);

        logicPosition += backdriveVelocity * Time.deltaTime;
    }

    private void ResolvePenetrationProjection()
    {
        if (toolCollider == null || tissueCollider == null) return;

        for (int it = 0; it < projectionIterations; it++)
        {
            Vector3 dir;
            float dist;

            bool overlapped = Physics.ComputePenetration(
                toolCollider, logicPosition, toolCollider.transform.rotation,
                tissueCollider, tissueCollider.transform.position, tissueCollider.transform.rotation,
                out dir, out dist
            );

            if (!overlapped) break;

            // Push slightly outward to keep a small clearance beyond the surface.
            Vector3 correction = dir * (dist + projectionSlop);
            logicPosition += correction;
            commandedPosition += correction;
        }
    }

    public void SetReactionForce(Vector3 force)
    {
        reactionForce = force;
    }
}
