using System.Collections.Generic;
using UnityEngine;

public class TeamBrain
{
    private readonly TeamId team;
    private readonly List<PoleAgent> members;
    private readonly PoleBattleGameManager gameManager;
    private int discussionCount;
    private string currentLeaderName;
    private string currentLeaderReason;

    public TeamBrain(TeamId team, List<PoleAgent> members, PoleBattleGameManager gameManager)
    {
        this.team = team;
        this.members = members;
        this.gameManager = gameManager;
    }

    public void HoldDiscussion()
    {
        if (members.Count == 0)
        {
            return;
        }

        discussionCount++;
        PoleAgent leader = ChooseLeader();
        currentLeaderName = leader.AgentName;
        gameManager.RecordLeaderChoice(team, currentLeaderName, currentLeaderReason);

        float ownStability = gameManager.GetOwnPole(team).Stability;
        float enemyStability = gameManager.GetEnemyPole(team).Stability;
        bool underThreat = ownStability < enemyStability || gameManager.CountEnemiesNearOwnPole(team) >= 2;
        bool closeToWin = enemyStability <= 35f;

        gameManager.LogConversation(team, leader.AgentName, BuildLeaderMessage(leader, ownStability, enemyStability, underThreat, closeToWin));

        for (int i = 0; i < members.Count; i++)
        {
            PoleAgent agent = members[i];
            AgentDecision decision = DecideForAgent(agent, i, underThreat, closeToWin);
            agent.ApplyDecision(decision);
            gameManager.LogConversation(team, agent.AgentName, decision.Message);
            gameManager.LogRole(agent, currentLeaderName);
        }
    }

    private PoleAgent ChooseLeader()
    {
        TeamLearningState learning = gameManager.GetLearningState(team);
        PoleAgent best = members[0];
        float bestScore = float.MinValue;
        string bestReason = "";

        for (int i = 0; i < members.Count; i++)
        {
            PoleAgent agent = members[i];
            float centerScore = -Mathf.Abs(agent.transform.position.x);
            float roleScore = agent.Role == AgentRole.Supporter ? 1.8f : 0f;
            float pressureScore = gameManager.GetRecentPressureScore(agent) * 0.18f;
            float learnedLeaderScore = learning.GetLeaderScore(agent.AgentName);
            float noise = Random.Range(0f, 1.2f);
            float score = centerScore + roleScore + pressureScore + learnedLeaderScore + noise;

            if (score > bestScore)
            {
                bestScore = score;
                best = agent;
                bestReason =
                    "center=" + centerScore.ToString("F2") +
                    ", supportRole=" + roleScore.ToString("F2") +
                    ", recentPush=" + pressureScore.ToString("F2") +
                    ", learned=" + learnedLeaderScore.ToString("F2") +
                    ", random=" + noise.ToString("F2") +
                    ", total=" + score.ToString("F2");
            }
        }

        currentLeaderReason = bestReason;
        return best;
    }

    private string BuildLeaderMessage(PoleAgent leader, float ownStability, float enemyStability, bool underThreat, bool closeToWin)
    {
        string stance = "balanced";
        if (underThreat)
        {
            stance = "defensive";
        }
        else if (closeToWin)
        {
            stance = "all-out attack";
        }

        return "Leader proposal: " + stance + " plan. Own stability " + ownStability.ToString("F0") + ", enemy stability " + enemyStability.ToString("F0") + ".";
    }

    private AgentDecision DecideForAgent(PoleAgent agent, int index, bool underThreat, bool closeToWin)
    {
        TeamLearningState learning = gameManager.GetLearningState(team);
        int defenderCount;
        int supporterCount;

        if (closeToWin)
        {
            defenderCount = 1;
            supporterCount = 1;

            if (index < defenderCount)
            {
                return new AgentDecision(AgentRole.Defender, AgentAction.GuardOwnPole, "I will leave one defender at our pole while the team finishes the attack.");
            }

            if (index < defenderCount + supporterCount)
            {
                return new AgentDecision(AgentRole.Supporter, AgentAction.SupportNearestAttacker, "I will support the final push and keep attackers grouped.");
            }

            return new AgentDecision(AgentRole.Attacker, AgentAction.MoveToEnemyPole, "I will join the main attack group and push the enemy pole.");
        }

        if (underThreat)
        {
            defenderCount = Mathf.Clamp(Mathf.RoundToInt(members.Count * (0.45f + learning.DefenseBias)), 3, 4);
            supporterCount = 1;

            if (index < defenderCount)
            {
                return new AgentDecision(AgentRole.Defender, AgentAction.GuardOwnPole, "I will defend our pole as part of the clear defense group. Learned defense bias is " + learning.DefenseBias.ToString("F2") + ".");
            }

            if (index < defenderCount + supporterCount)
            {
                return new AgentDecision(AgentRole.Supporter, AgentAction.SupportNearestAttacker, "I will support the counterattack while defenders hold.");
            }

            return new AgentDecision(AgentRole.Attacker, AgentAction.MoveToEnemyPole, "I will counterattack as part of the attack group.");
        }

        defenderCount = learning.DefenseBias > learning.AttackBias ? 3 : 2;
        supporterCount = learning.SupportBias > 0.10f ? 2 : 1;

        if (index < defenderCount)
        {
            return new AgentDecision(AgentRole.Defender, AgentAction.GuardOwnPole, "I will stay in the defense group near our pole.");
        }

        if (index < defenderCount + supporterCount)
        {
            return new AgentDecision(AgentRole.Supporter, AgentAction.SupportNearestAttacker, "I will stay in the support group and help the nearest attacker.");
        }

        return new AgentDecision(AgentRole.Attacker, AgentAction.MoveToEnemyPole, "I will stay in the attack group and push the enemy pole. Learned attack bias is " + learning.AttackBias.ToString("F2") + ".");
    }
}
