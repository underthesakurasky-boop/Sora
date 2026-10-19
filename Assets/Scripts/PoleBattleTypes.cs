using UnityEngine;

public enum TeamId
{
    Red,
    Blue,
    Green,
    Yellow
}

public enum AgentRole
{
    Attacker,
    Defender,
    Supporter
}

public enum AgentAction
{
    MoveToEnemyPole,
    GuardOwnPole,
    SupportNearestAttacker,
    ChaseEnemy,
    Celebrate,
    Idle
}

public enum InstructionCompliance
{
    Full,
    Half,
    None
}

[System.Serializable]
public class AgentDecision
{
    public AgentRole Role;
    public AgentAction Action;
    public string Message;

    public AgentDecision(AgentRole role, AgentAction action, string message)
    {
        Role = role;
        Action = action;
        Message = message;
    }
}
