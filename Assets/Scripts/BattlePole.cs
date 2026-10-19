using UnityEngine;

public class BattlePole : MonoBehaviour
{
    public TeamId Team;
    public float MaxHealth = 100f;
    public float MassKg = 120f;
    public float PushHeightMeters = 3.6f;
    public float BaseResistanceTorque = 6800f;
    public float FallAngleDegrees = 72f;
    public float RecoveryTorque = 5200f;
    public float AngularDamping = 52.0f;
    public float MaxPoleAngularVelocity = 0.95f;
    public float SmallTiltExtraDamping = 0.38f;
    public float GroupPushBonusPerExtraPusher = 0.20f;
    public float MaxGroupPushMultiplier = 2.8f;
    public float TiltAngleDegrees { get { return CalculateTiltAngle(); } }
    public Vector3 LeanDirection { get { return CalculateLeanDirection(); } }
    public float LastTorque { get { return lastTorque; } }
    public int LastPusherCount { get { return lastPusherCount; } }
    public float LastNetForce { get { return lastNetForce; } }
    public float Health { get { return Stability; } }
    public float Stability { get { return hasFallen ? 0f : Mathf.Clamp01(1f - TiltAngleDegrees / FallAngleDegrees) * 100f; } }
    public bool IsDown { get { return hasFallen || TiltAngleDegrees >= FallAngleDegrees; } }

    private Renderer cachedRenderer;
    private Quaternion uprightRotation;
    private Rigidbody body;
    private Rigidbody anchorBody;
    private ConfigurableJoint joint;
    private Vector3 basePosition;
    private float lastTorque;
    private int pusherCount;
    private int lastPusherCount;
    private bool hasFallen;
    private Vector3 pendingForce;
    private float pendingTorque;
    private Vector3 currentNetForce;
    private float lastNetForce;
    private readonly float[] currentTeamForces = new float[4];
    private readonly float[] lastTeamForces = new float[4];

    private void Awake()
    {
        cachedRenderer = GetComponent<Renderer>();
        uprightRotation = transform.rotation;
        basePosition = transform.position - transform.up * (transform.localScale.y);
        SetupPhysics();
        UpdateVisual();
    }

    private void FixedUpdate()
    {
        if (hasFallen || body == null)
        {
            pendingForce = Vector3.zero;
            pendingTorque = 0f;
            return;
        }

        if (pendingForce.sqrMagnitude > 0.01f)
        {
            Vector3 forcePoint = basePosition + Vector3.up * PushHeightMeters;
            body.AddForceAtPosition(pendingForce, forcePoint, ForceMode.Force);
            lastTorque = Mathf.Max(lastTorque, pendingTorque);
        }

        body.angularVelocity = Vector3.ClampMagnitude(body.angularVelocity, MaxPoleAngularVelocity);
        if (TiltAngleDegrees < 8f)
        {
            body.angularVelocity *= SmallTiltExtraDamping;
        }

        pendingForce = Vector3.zero;
        pendingTorque = 0f;
    }

    private void LateUpdate()
    {
        if (!hasFallen && TiltAngleDegrees >= FallAngleDegrees)
        {
            MarkFallen();
        }

        UpdateVisual();

        lastNetForce = currentNetForce.magnitude;
        for (int i = 0; i < lastTeamForces.Length; i++)
        {
            lastTeamForces[i] = currentTeamForces[i];
            currentTeamForces[i] = 0f;
        }

        currentNetForce = Vector3.zero;
        lastPusherCount = pusherCount;
        pusherCount = 0;
    }

    public void ApplyPush(Vector3 pusherPosition, float forceNewtons, TeamId sourceTeam)
    {
        // Attack pressure from the pole's own team is never valid. Defenders use
        // ApplyStabilizingPush instead, so this also protects four-team matches
        // from stale or incorrect target assignments.
        if (IsDown || sourceTeam == Team)
        {
            return;
        }

        Vector3 polePosition = transform.position;
        polePosition.y = 0f;
        pusherPosition.y = 0f;

        Vector3 pushDirection = polePosition - pusherPosition;
        if (pushDirection.sqrMagnitude < 0.01f)
        {
            return;
        }

        pushDirection.Normalize();
        float groupMultiplier = Mathf.Min(MaxGroupPushMultiplier, 1f + pusherCount * GroupPushBonusPerExtraPusher);
        float effectiveForce = forceNewtons * groupMultiplier;
        float torque = effectiveForce * PushHeightMeters;
        Vector3 forceVector = pushDirection * effectiveForce;
        pendingForce += forceVector;
        RecordForceTelemetry(sourceTeam, forceVector, effectiveForce);
        pendingTorque = Mathf.Max(pendingTorque, torque);
        pusherCount++;
    }

    public void ApplyStabilizingPush(float forceNewtons, TeamId sourceTeam)
    {
        if (IsDown || TiltAngleDegrees < 1.5f)
        {
            return;
        }

        Vector3 leanDirection = LeanDirection;
        if (leanDirection.sqrMagnitude < 0.01f)
        {
            return;
        }

        Vector3 stabilizingDirection = -leanDirection.normalized;
        Vector3 forceVector = stabilizingDirection * forceNewtons;
        pendingForce += forceVector;
        RecordForceTelemetry(sourceTeam, forceVector, forceNewtons);
        pendingTorque = Mathf.Max(pendingTorque, forceNewtons * PushHeightMeters);
        pusherCount++;
    }

    public void ApplyPressure(float amount)
    {
        ApplyPush(transform.position - transform.forward, amount * 10f, Team);
    }

    public float GetLastForceFromTeam(TeamId team)
    {
        int index = (int)team;
        if (index < 0 || index >= lastTeamForces.Length)
        {
            return 0f;
        }

        return lastTeamForces[index];
    }

    public string GetForceSummary()
    {
        return "R " + GetLastForceFromTeam(TeamId.Red).ToString("F0")
            + "N / B " + GetLastForceFromTeam(TeamId.Blue).ToString("F0")
            + "N / G " + GetLastForceFromTeam(TeamId.Green).ToString("F0")
            + "N / Y " + GetLastForceFromTeam(TeamId.Yellow).ToString("F0")
            + "N / net " + LastNetForce.ToString("F0") + "N";
    }

    public void ResetPole()
    {
        hasFallen = false;
        body.isKinematic = false;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;
        transform.rotation = uprightRotation;
        transform.position = basePosition + Vector3.up * transform.localScale.y;
        UpdateVisual();
    }

    private void SetupPhysics()
    {
        body = GetComponent<Rigidbody>();
        if (body == null)
        {
            body = gameObject.AddComponent<Rigidbody>();
        }

        body.mass = MassKg;
        body.useGravity = true;
        body.linearDamping = 1.2f;
        body.angularDamping = AngularDamping;
        body.maxAngularVelocity = MaxPoleAngularVelocity;
        body.solverIterations = 24;
        body.solverVelocityIterations = 24;
        body.inertiaTensor = new Vector3(38f, 8f, 38f);
        body.inertiaTensorRotation = Quaternion.identity;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.Continuous;

        GameObject anchorObject = new GameObject(name + " Base Anchor");
        anchorObject.transform.position = basePosition;
        anchorBody = anchorObject.AddComponent<Rigidbody>();
        anchorBody.isKinematic = true;

        joint = gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = anchorBody;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = Vector3.down;
        joint.connectedAnchor = Vector3.zero;
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = ConfigurableJointMotion.Limited;
        joint.angularYMotion = ConfigurableJointMotion.Locked;
        joint.angularZMotion = ConfigurableJointMotion.Limited;
        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = 0.002f;
        joint.projectionAngle = 0.25f;

        SoftJointLimit lowXLimit = new SoftJointLimit();
        lowXLimit.limit = -FallAngleDegrees;
        joint.lowAngularXLimit = lowXLimit;

        SoftJointLimit highXLimit = new SoftJointLimit();
        highXLimit.limit = FallAngleDegrees;
        joint.highAngularXLimit = highXLimit;

        SoftJointLimit zLimit = new SoftJointLimit();
        zLimit.limit = FallAngleDegrees;
        joint.angularZLimit = zLimit;

        JointDrive drive = new JointDrive();
        drive.positionSpring = BaseResistanceTorque;
        drive.positionDamper = RecoveryTorque;
        drive.maximumForce = BaseResistanceTorque * 6.0f;
        joint.slerpDrive = drive;
    }

    private void MarkFallen()
    {
        hasFallen = true;
        if (joint != null)
        {
            Destroy(joint);
            joint = null;
        }

        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;
        }
    }

    private float CalculateTiltAngle()
    {
        return Vector3.Angle(transform.up, Vector3.up);
    }

    private Vector3 CalculateLeanDirection()
    {
        Vector3 lean = Vector3.ProjectOnPlane(transform.up, Vector3.up);
        if (lean.sqrMagnitude < 0.0001f)
        {
            return Vector3.zero;
        }

        return lean.normalized;
    }

    private void RecordForceTelemetry(TeamId sourceTeam, Vector3 forceVector, float forceMagnitude)
    {
        int index = (int)sourceTeam;
        if (index >= 0 && index < currentTeamForces.Length)
        {
            currentTeamForces[index] += forceMagnitude;
        }

        currentNetForce += forceVector;
    }

    private void OnDestroy()
    {
        if (anchorBody != null)
        {
            Destroy(anchorBody.gameObject);
        }
    }

    private void UpdateVisual()
    {
        if (cachedRenderer != null)
        {
            Color baseColor = GetTeamColor(Team);
            cachedRenderer.material.color = hasFallen ? Color.gray : Color.Lerp(Color.gray, baseColor, Stability / 100f);
        }

        lastTorque = Mathf.Lerp(lastTorque, 0f, Time.deltaTime * 3f);
    }

    private Color GetTeamColor(TeamId team)
    {
        switch (team)
        {
            case TeamId.Red:
                return new Color(0.9f, 0.16f, 0.14f);
            case TeamId.Blue:
                return new Color(0.1f, 0.35f, 0.95f);
            case TeamId.Green:
                return new Color(0.1f, 0.75f, 0.3f);
            case TeamId.Yellow:
                return new Color(1f, 0.8f, 0.12f);
            default:
                return Color.white;
        }
    }
}
