using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

[Serializable]
public class TeamLearningState
{
    public TeamId Team;
    public int GamesPlayed;
    public int Wins;
    public int Losses;
    public int PracticeGames;
    public int PracticeWins;
    public int PracticeLosses;
    public float AttackBias;
    public float DefenseBias;
    public float SupportBias;
    public string LastReflectionSummary;
    public List<AgentLearningRecord> AgentRecords = new List<AgentLearningRecord>();

    public TeamLearningState()
    {
    }

    public TeamLearningState(TeamId team)
    {
        Team = team;
    }

    public float GetLeaderScore(string agentName)
    {
        AgentLearningRecord record = GetOrCreateAgent(agentName);
        return record.LeaderScore;
    }

    public void RecordLeader(string agentName)
    {
        AgentLearningRecord record = GetOrCreateAgent(agentName);
        EnsureProfile(record);
        record.LeaderCount++;
        record.LeaderScore = Mathf.Clamp(record.LeaderScore + 0.12f, 0f, 3f);
    }

    public void RecordAgentRole(string agentName, AgentRole role, bool won)
    {
        AgentLearningRecord record = GetOrCreateAgent(agentName);
        EnsureProfile(record);
        record.LastRole = role.ToString();

        switch (role)
        {
            case AgentRole.Attacker:
                record.AttackerCount++;
                break;
            case AgentRole.Defender:
                record.DefenderCount++;
                break;
            case AgentRole.Supporter:
                record.SupporterCount++;
                break;
        }

        if (won)
        {
            record.SuccessScore = Mathf.Clamp(record.SuccessScore + 0.08f, 0f, 3f);
        }
        else
        {
            record.SuccessScore = Mathf.Clamp(record.SuccessScore * 0.96f, 0f, 3f);
        }
    }

    public string GetAgentSummary(string agentName)
    {
        AgentLearningRecord record = GetOrCreateAgent(agentName);
        EnsureProfile(record);
        return "personality=" + record.Personality +
            ", preferredRole=" + record.PreferredRole +
            ", lastRole=" + EmptyToDash(record.LastRole) +
            ", leaderCount=" + record.LeaderCount +
            ", leaderScore=" + record.LeaderScore.ToString("F2") +
            ", successScore=" + record.SuccessScore.ToString("F2") +
            ", roleCounts=A" + record.AttackerCount + "/D" + record.DefenderCount + "/S" + record.SupporterCount +
            ", latestSelfReflection=" + EmptyToDash(record.LastReflectionSummary);
    }

    public string GetPersonality(string agentName)
    {
        AgentLearningRecord record = GetOrCreateAgent(agentName);
        EnsureProfile(record);
        return record.Personality;
    }

    public void RecordPracticeResult(bool won)
    {
        PracticeGames++;
        if (won)
        {
            PracticeWins++;
            AttackBias = Mathf.Clamp(AttackBias + 0.06f, 0f, 0.30f);
            DefenseBias = Mathf.Clamp(DefenseBias - 0.02f, 0f, 0.30f);
            SupportBias = Mathf.Clamp(SupportBias + 0.02f, 0f, 0.20f);
        }
        else
        {
            PracticeLosses++;
            DefenseBias = Mathf.Clamp(DefenseBias + 0.08f, 0f, 0.35f);
            AttackBias = Mathf.Clamp(AttackBias - 0.03f, 0f, 0.30f);
            SupportBias = Mathf.Clamp(SupportBias + 0.03f, 0f, 0.20f);
        }
    }

    public void RecordMatchResult(bool won)
    {
        GamesPlayed++;

        if (won)
        {
            Wins++;
            AttackBias = Mathf.Clamp(AttackBias + 0.06f, 0f, 0.30f);
            DefenseBias = Mathf.Clamp(DefenseBias - 0.02f, 0f, 0.30f);
            SupportBias = Mathf.Clamp(SupportBias + 0.02f, 0f, 0.20f);
        }
        else
        {
            Losses++;
            DefenseBias = Mathf.Clamp(DefenseBias + 0.08f, 0f, 0.35f);
            AttackBias = Mathf.Clamp(AttackBias - 0.03f, 0f, 0.30f);
            SupportBias = Mathf.Clamp(SupportBias + 0.03f, 0f, 0.20f);
        }

        for (int i = 0; i < AgentRecords.Count; i++)
        {
            AgentRecords[i].LeaderScore = Mathf.Clamp(AgentRecords[i].LeaderScore * 0.94f, 0f, 3f);
        }
    }

    public string GetSummary()
    {
        return "officialGames=" + GamesPlayed +
            ", wins=" + Wins +
            ", losses=" + Losses +
            ", practiceGames=" + PracticeGames +
            ", practiceWins=" + PracticeWins +
            ", practiceLosses=" + PracticeLosses +
            ", attackBias=" + AttackBias.ToString("F2") +
            ", defenseBias=" + DefenseBias.ToString("F2") +
            ", supportBias=" + SupportBias.ToString("F2") +
            ", latestReflection=" + EmptyToDash(LastReflectionSummary);
    }

    public void RecordReflection(string summary)
    {
        summary = (summary ?? "").Trim();
        LastReflectionSummary = summary.Length <= 1200 ? summary : summary.Substring(0, 1200);
    }

    public void RecordAgentReflection(string agentName, string summary)
    {
        AgentLearningRecord record = GetOrCreateAgent(agentName);
        summary = (summary ?? "").Trim();
        record.LastReflectionSummary = summary.Length <= 600 ? summary : summary.Substring(0, 600);
    }

    private AgentLearningRecord GetOrCreateAgent(string agentName)
    {
        for (int i = 0; i < AgentRecords.Count; i++)
        {
            if (AgentRecords[i].AgentName == agentName)
            {
                return AgentRecords[i];
            }
        }

        AgentLearningRecord record = new AgentLearningRecord();
        record.AgentName = agentName;
        EnsureProfile(record);
        AgentRecords.Add(record);
        return record;
    }

    private void EnsureProfile(AgentLearningRecord record)
    {
        string[] personalities = { "assertive", "cautious", "cooperative", "analytical", "adaptive", "risk-taking" };
        string[] roles = { "Attacker", "Defender", "Supporter", "Attacker", "Supporter", "Defender" };
        int agentNumber = ExtractAgentNumber(record.AgentName);
        int profileIndex = Mathf.Abs(agentNumber - 1) % personalities.Length;
        record.Personality = personalities[profileIndex];
        record.PreferredRole = roles[profileIndex];
    }

    private int ExtractAgentNumber(string agentName)
    {
        Match match = Regex.Match(agentName ?? "", "(\\d+)$");
        int value;
        return match.Success && int.TryParse(match.Groups[1].Value, out value) ? Mathf.Max(1, value) : 1;
    }

    private string EmptyToDash(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value;
    }
}

[Serializable]
public class AgentLearningRecord
{
    public string AgentName;
    public string Personality;
    public string PreferredRole;
    public string LastRole;
    public int LeaderCount;
    public int AttackerCount;
    public int DefenderCount;
    public int SupporterCount;
    public float LeaderScore;
    public float SuccessScore;
    public string LastReflectionSummary;
}

[Serializable]
public class LearningSaveData
{
    public TeamLearningState Red = new TeamLearningState(TeamId.Red);
    public TeamLearningState Blue = new TeamLearningState(TeamId.Blue);
    public TeamLearningState Green = new TeamLearningState(TeamId.Green);
    public TeamLearningState Yellow = new TeamLearningState(TeamId.Yellow);
}
