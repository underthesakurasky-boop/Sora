using UnityEngine;

public class PoleAgent : MonoBehaviour
{
    public string AgentName;
    public TeamId Team;
    public AgentRole Role;
    public AgentAction CurrentAction;
    public string AbilityNote = "normal";
    public InstructionCompliance Compliance = InstructionCompliance.Full;
    public int LeaderDirectivesReceived { get; private set; }
    public int LeaderDirectivesFollowed { get; private set; }
    public int LeaderDirectivesDeclined { get; private set; }
    public float MoveSpeed = 2.8f;
    public float PushForceNewtons = 72f;
    public float StabilizeForceNewtons = 42f;
    public float GuardRadius = 5f;
    public float PersonalSpace = 1.15f;
    public float AttackReach = 2.25f;
    public float DefenseReach = 2.05f;

    private PoleBattleGameManager gameManager;
    private BattlePole ownPole;
    private BattlePole enemyPole;
    private Vector3 spawnPoint;
    private Vector3 currentTarget;
    private float nextRetargetTime;
    private Renderer cachedRenderer;
    private TextMesh label;
    private GameObject leaderMarker;
    private Renderer leaderMarkerRenderer;
    private Color baseColor;
    private bool isLeader;
    private int leaderDirectiveCount;

    public void Initialize(
        PoleBattleGameManager manager,
        string agentName,
        TeamId team,
        BattlePole own,
        BattlePole enemy,
        Color bodyColor)
    {
        gameManager = manager;
        AgentName = agentName;
        Team = team;
        ownPole = own;
        enemyPole = enemy;
        spawnPoint = transform.position;
        Role = AgentRole.Attacker;
        CurrentAction = AgentAction.MoveToEnemyPole;
        cachedRenderer = GetComponent<Renderer>();
        baseColor = bodyColor;

        if (cachedRenderer != null)
        {
            cachedRenderer.material.color = GetRoleColor();
        }

        CreateNameLabel();
        CreateLeaderMarker();
        UpdateNameLabel();
    }

    private void Update()
    {
        UpdateLabelFacingCamera();

        if (gameManager == null || gameManager.BattleFinished || !gameManager.BattleStarted)
        {
            return;
        }

        if (gameManager.IsTeamEliminated(Team))
        {
            MoveToward(spawnPoint);
            return;
        }

        if (gameManager.UseTugOfWarRules)
        {
            UpdateTugOfWarMovement();
            return;
        }

        if (Time.time >= nextRetargetTime)
        {
            currentTarget = ChooseTarget();
            nextRetargetTime = Time.time + Random.Range(0.25f, 0.75f);
        }

        MoveToward(currentTarget);
        ApplyPolePressureIfClose();
    }

    private void UpdateTugOfWarMovement()
    {
        TugPole tugPole = gameManager.GetAssignedTugPole(this);
        if (tugPole == null)
        {
            MoveToward(spawnPoint);
            return;
        }

        if (Time.time >= nextRetargetTime)
        {
            currentTarget = AvoidBlocker(OffsetAround(tugPole.transform.position, 1.25f));

            nextRetargetTime = Time.time + Random.Range(0.25f, 0.65f);
        }

        MoveToward(currentTarget);
        Vector3 a = transform.position;
        Vector3 b = tugPole.transform.position;
        a.y = 0f;
        b.y = 0f;
        if (Vector3.Distance(a, b) <= tugPole.PullRadius)
        {
            tugPole.ApplyPull(Team, PushForceNewtons);
            gameManager.RecordTugPull(this, tugPole);
        }
    }

    public void ApplyDecision(AgentDecision decision)
    {
        Role = decision.Role;
        CurrentAction = decision.Action;
        currentTarget = ChooseTarget();
        UpdateBodyColor();
        UpdateNameLabel();
    }

    public bool ApplyLeaderDecision(AgentDecision decision)
    {
        leaderDirectiveCount++;
        LeaderDirectivesReceived++;
        bool follows = Compliance == InstructionCompliance.Full ||
            (Compliance == InstructionCompliance.Half && (leaderDirectiveCount + ExtractAgentNumber()) % 2 == 0);
        if (!follows)
        {
            LeaderDirectivesDeclined++;
            return false;
        }

        LeaderDirectivesFollowed++;
        ApplyDecision(decision);
        return true;
    }

    public void SetLeaderVisual(bool leader)
    {
        isLeader = leader;
        UpdateBodyColor();
        UpdateNameLabel();
    }

    public void SetAbilityProfile(string abilityNote, float forceMultiplier, float speedMultiplier)
    {
        AbilityNote = abilityNote;
        PushForceNewtons *= forceMultiplier;
        StabilizeForceNewtons *= forceMultiplier;
        MoveSpeed *= speedMultiplier;
    }

    public string GetShortState()
    {
        return AgentName + "," + Team + "," + Role + "," + CurrentAction + ",ability=" + AbilityNote + ",compliance=" + Compliance + "," + transform.position.ToString("F1");
    }

    private void CreateNameLabel()
    {
        GameObject labelObject = new GameObject("Agent Label");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 1.55f, 0f);

        label = labelObject.AddComponent<TextMesh>();
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.characterSize = 0.24f;
        label.fontSize = 48;
        label.color = Color.white;
        labelObject.SetActive(false);
    }

    private void UpdateNameLabel()
    {
        if (label == null)
        {
            return;
        }

        label.gameObject.SetActive(isLeader);
        label.text = Team.ToString().ToUpperInvariant() + " LEADER\n" + AgentName;
        label.color = GetTeamLabelColor(Team);
    }

    private void UpdateBodyColor()
    {
        if (cachedRenderer == null)
        {
            return;
        }

        cachedRenderer.material.color = GetRoleColor();
        if (leaderMarker != null)
        {
            leaderMarker.SetActive(isLeader);
        }
    }

    private void CreateLeaderMarker()
    {
        leaderMarker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        leaderMarker.name = "Leader Marker";
        leaderMarker.transform.SetParent(transform, false);
        leaderMarker.transform.localPosition = new Vector3(0f, 1.95f, 0f);
        leaderMarker.transform.localScale = new Vector3(0.35f, 0.35f, 0.35f);

        Collider markerCollider = leaderMarker.GetComponent<Collider>();
        if (markerCollider != null)
        {
            Object.Destroy(markerCollider);
        }

        leaderMarkerRenderer = leaderMarker.GetComponent<Renderer>();
        if (leaderMarkerRenderer != null)
        {
            leaderMarkerRenderer.material.color = Color.white;
        }

        leaderMarker.SetActive(false);
    }

    private Color GetRoleColor()
    {
        switch (Role)
        {
            case AgentRole.Defender:
                return Color.Lerp(baseColor, Color.black, 0.45f);
            case AgentRole.Supporter:
                return Color.Lerp(baseColor, Color.white, 0.45f);
            default:
                return baseColor;
        }
    }

    private void UpdateLabelFacingCamera()
    {
        if (label == null || Camera.main == null)
        {
            return;
        }

        label.transform.rotation = Quaternion.LookRotation(label.transform.position - Camera.main.transform.position);
    }

    private Vector3 ChooseTarget()
    {
        RefreshActivePoleTargets();
        if (enemyPole == null || ownPole == null)
        {
            return spawnPoint;
        }

        switch (CurrentAction)
        {
            case AgentAction.MoveToEnemyPole:
                if (!gameManager.CanAgentPushPole(this, enemyPole))
                {
                    return AvoidBlocker(TacticalPointAroundPole(enemyPole, 4.5f, GetAttackSideSign()));
                }
                return AvoidBlocker(TacticalPointAroundPole(enemyPole, 1.35f, GetAttackSideSign()));
            case AgentAction.GuardOwnPole:
                return TacticalPointAroundPole(ownPole, 1.55f, GetDefenseSideSign());
            case AgentAction.SupportNearestAttacker:
                if (!gameManager.CanAgentPushPole(this, enemyPole))
                {
                    return AvoidBlocker(TacticalPointAroundPole(enemyPole, 4.8f, GetAttackSideSign()));
                }
                PoleAgent attacker = gameManager.FindNearestAllyWithRole(this, AgentRole.Attacker);
                return AvoidBlocker(attacker != null ? OffsetAround(attacker.transform.position, 1.15f) : TacticalPointAroundPole(enemyPole, 2.1f, GetAttackSideSign()));
            case AgentAction.ChaseEnemy:
                PoleAgent enemy = gameManager.FindNearestEnemy(this);
                return enemy != null ? enemy.transform.position : OffsetAround(ownPole.transform.position, GuardRadius);
            case AgentAction.Celebrate:
                return spawnPoint;
            default:
                return transform.position;
        }
    }

    private void MoveToward(Vector3 target)
    {
        Vector3 flatTarget = new Vector3(target.x, transform.position.y, target.z);
        Vector3 delta = flatTarget - transform.position;
        delta.y = 0f;

        if (delta.magnitude < 0.08f)
        {
            return;
        }

        Vector3 direction = delta.normalized;
        Vector3 avoidance = gameManager.ComputeSeparation(this, PersonalSpace);
        Vector3 finalDirection = (direction + avoidance * 2.4f).normalized;
        Vector3 nextPosition = transform.position + finalDirection * MoveSpeed * Time.deltaTime;

        if (!gameManager.IsPositionClear(this, nextPosition, PersonalSpace * 0.82f))
        {
            if (avoidance.sqrMagnitude < 0.001f)
            {
                return;
            }

            finalDirection = avoidance.normalized;
            nextPosition = transform.position + finalDirection * MoveSpeed * 0.65f * Time.deltaTime;
            if (!gameManager.IsPositionClear(this, nextPosition, PersonalSpace * 0.72f))
            {
                return;
            }
        }

        nextPosition = gameManager.ClampAgentToField(nextPosition);
        transform.position = nextPosition;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(finalDirection), Time.deltaTime * 10f);
    }

    private void ApplyPolePressureIfClose()
    {
        RefreshActivePoleTargets();
        if (enemyPole == null || ownPole == null)
        {
            return;
        }

        if (CurrentAction == AgentAction.GuardOwnPole)
        {
            ApplyStabilizingPressureIfClose();
            return;
        }

        if (CurrentAction != AgentAction.MoveToEnemyPole && CurrentAction != AgentAction.SupportNearestAttacker)
        {
            return;
        }

        if (enemyPole == null || enemyPole.IsDown || !gameManager.CanAgentPushPole(this, enemyPole))
        {
            return;
        }

        Vector3 agentPosition = transform.position;
        Vector3 polePosition = enemyPole.transform.position;
        agentPosition.y = 0f;
        polePosition.y = 0f;

        float distance = Vector3.Distance(agentPosition, polePosition);
        if (distance <= AttackReach)
        {
            enemyPole.ApplyPush(transform.position, PushForceNewtons, Team);
            gameManager.RecordPressure(this, enemyPole);
        }
    }

    private void ApplyStabilizingPressureIfClose()
    {
        if (ownPole == null || ownPole.IsDown)
        {
            return;
        }

        Vector3 agentPosition = transform.position;
        Vector3 polePosition = ownPole.transform.position;
        agentPosition.y = 0f;
        polePosition.y = 0f;

        float distance = Vector3.Distance(agentPosition, polePosition);
        if (distance <= DefenseReach)
        {
            ownPole.ApplyStabilizingPush(StabilizeForceNewtons, Team);
            gameManager.RecordPressure(this, ownPole);
        }
    }

    private void RefreshActivePoleTargets()
    {
        if (gameManager == null)
        {
            return;
        }

        BattlePole activeOwnPole = gameManager.GetNearestActiveOwnPole(Team, transform.position);
        BattlePole activeEnemyPole = gameManager.GetAssignedActiveEnemyPole(this);
        if (activeOwnPole != null)
        {
            ownPole = activeOwnPole;
        }

        if (activeEnemyPole != null)
        {
            enemyPole = activeEnemyPole;
        }
    }

    private Vector3 TacticalPointAroundPole(BattlePole pole, float radius, float sideSign)
    {
        float lane = GetLaneOffset();
        Vector3 point = pole.transform.position + new Vector3(sideSign * radius, 0f, lane);
        point.y = transform.position.y;
        return point;
    }

    private Vector3 AvoidBlocker(Vector3 intendedTarget)
    {
        PoleAgent blocker = gameManager.FindNearestEnemy(this);
        if (blocker == null)
        {
            return intendedTarget;
        }

        Vector3 toTarget = intendedTarget - transform.position;
        Vector3 toBlocker = blocker.transform.position - transform.position;
        toTarget.y = 0f;
        toBlocker.y = 0f;
        if (toTarget.sqrMagnitude < 0.01f || toBlocker.magnitude > 3.2f || Vector3.Dot(toTarget.normalized, toBlocker.normalized) < 0.55f)
        {
            return intendedTarget;
        }

        Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized);
        float sideSign = Mathf.Abs(AgentName.GetHashCode()) % 2 == 0 ? 1f : -1f;
        Vector3 detour = blocker.transform.position + side * sideSign * 2.1f;
        detour.y = transform.position.y;
        return gameManager.ClampAgentToField(detour);
    }

    private int ExtractAgentNumber()
    {
        int separator = AgentName != null ? AgentName.LastIndexOf('-') : -1;
        int number;
        return separator >= 0 && int.TryParse(AgentName.Substring(separator + 1), out number) ? Mathf.Max(1, number) : 1;
    }

    private float GetAttackSideSign()
    {
        return transform.position.x <= 0f ? -1f : 1f;
    }

    private float GetDefenseSideSign()
    {
        return -GetAttackSideSign();
    }

    private float GetLaneOffset()
    {
        int hash = Mathf.Abs(AgentName.GetHashCode());
        int lane = hash % 3;
        return (lane - 1) * 0.85f;
    }

    private Vector3 OffsetAround(Vector3 center, float radius)
    {
        float teamSign = transform.position.x <= 0f ? -1f : 1f;
        float angle = (AgentName.GetHashCode() % 360) * Mathf.Deg2Rad + Time.time * 0.15f;
        Vector3 offset = new Vector3(Mathf.Cos(angle) * radius * teamSign, 0f, Mathf.Sin(angle) * radius);
        return center + offset;
    }

    private Color GetTeamLabelColor(TeamId team)
    {
        switch (team)
        {
            case TeamId.Red:
                return new Color(1f, 0.25f, 0.2f);
            case TeamId.Blue:
                return new Color(0.25f, 0.55f, 1f);
            case TeamId.Green:
                return new Color(0.25f, 1f, 0.45f);
            case TeamId.Yellow:
                return new Color(1f, 0.65f, 0.05f);
            default:
                return Color.white;
        }
    }
}
