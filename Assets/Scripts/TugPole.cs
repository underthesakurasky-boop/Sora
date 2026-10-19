using UnityEngine;

public class TugPole : MonoBehaviour
{
    public int PoleNumber;
    public Vector3 RedHome;
    public Vector3 BlueHome;
    public float CaptureBoundaryX = 22f;
    public float MassKg = 420f;
    public float LinearDamping = 2.8f;
    public float MaxSpeed = 3.4f;
    public float PullRadius = 2.8f;

    public bool IsCaptured { get { return capturedTeam.HasValue; } }
    public TeamId? CapturedTeam { get { return capturedTeam; } }
    public float LastRedForce { get; private set; }
    public float LastBlueForce { get; private set; }
    public int LastRedPullers { get; private set; }
    public int LastBluePullers { get; private set; }

    private TeamId? capturedTeam;
    private float pendingRedForce;
    private float pendingBlueForce;
    private int pendingRedPullers;
    private int pendingBluePullers;
    private Vector3 velocity;
    private Renderer cachedRenderer;

    private void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        UpdateColor();
    }

    public void ApplyPull(TeamId team, float forceNewtons)
    {
        if (IsCaptured)
        {
            return;
        }

        if (team == TeamId.Red)
        {
            pendingRedForce += Mathf.Max(0f, forceNewtons);
            pendingRedPullers++;
        }
        else if (team == TeamId.Blue)
        {
            pendingBlueForce += Mathf.Max(0f, forceNewtons);
            pendingBluePullers++;
        }
    }

    private void FixedUpdate()
    {
        LastRedForce = pendingRedForce;
        LastBlueForce = pendingBlueForce;
        LastRedPullers = pendingRedPullers;
        LastBluePullers = pendingBluePullers;

        if (!IsCaptured)
        {
            Vector3 redDirection = RedHome - transform.position;
            Vector3 blueDirection = BlueHome - transform.position;
            redDirection.y = 0f;
            blueDirection.y = 0f;
            Vector3 netForce = redDirection.normalized * pendingRedForce + blueDirection.normalized * pendingBlueForce;
            velocity += netForce / Mathf.Max(1f, MassKg) * Time.fixedDeltaTime;
            velocity *= 1f / (1f + LinearDamping * Time.fixedDeltaTime);
            velocity = Vector3.ClampMagnitude(velocity, MaxSpeed);
            transform.position += velocity * Time.fixedDeltaTime;

            if (transform.position.x <= -CaptureBoundaryX)
            {
                Capture(TeamId.Red);
            }
            else if (transform.position.x >= CaptureBoundaryX)
            {
                Capture(TeamId.Blue);
            }
        }

        pendingRedForce = 0f;
        pendingBlueForce = 0f;
        pendingRedPullers = 0;
        pendingBluePullers = 0;
    }

    private void Capture(TeamId team)
    {
        capturedTeam = team;
        velocity = Vector3.zero;
        Vector3 home = team == TeamId.Red ? RedHome : BlueHome;
        transform.position = new Vector3(home.x, transform.position.y, transform.position.z);
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (cachedRenderer == null)
        {
            return;
        }

        if (!capturedTeam.HasValue)
        {
            cachedRenderer.material.color = new Color(0.82f, 0.82f, 0.76f);
        }
        else
        {
            cachedRenderer.material.color = capturedTeam.Value == TeamId.Red
                ? new Color(0.95f, 0.18f, 0.14f)
                : new Color(0.12f, 0.38f, 1f);
        }
    }
}
