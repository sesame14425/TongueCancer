using UnityEngine;

public class ToolMover : MonoBehaviour
{
    public float moveSpeed = 0.5f;
    public Vector3 reactionForce;

    void Update()
    {
        float h = Input.GetAxis("Horizontal"); // A/D
        float v = Input.GetAxis("Vertical");   // W/S
        float up = 0f;
        if (Input.GetKey(KeyCode.E)) up += 1f;
        if (Input.GetKey(KeyCode.Q)) up -= 1f;

        Vector3 dir = new Vector3(h, up, v);
        transform.position += dir * moveSpeed * Time.deltaTime;
    }

    public void SetReactionForce(Vector3 force)
    {
        reactionForce = force;
    }
}