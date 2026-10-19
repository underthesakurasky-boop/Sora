using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class ExperimentLogger : MonoBehaviour
{
    public string FolderName = "PoleBattleLogs";
    public string ResetFileName = "ResetExperimentData.txt";

    private string logPath;
    private string conversationPath;
    private string learningPath;
    private string discussionPath;
    private string forcePath;
    private string matchSummaryPath;
    private string agentSummaryPath;
    private string tugPath;
    private string allMatchSummaryPath;
    private string allAgentSummaryPath;
    private string allDiscussionPath;
    private string llmRequestPath;
    private string allLLMRequestPath;
    private readonly List<string> pendingLines = new List<string>();
    private readonly List<string> pendingConversationLines = new List<string>();
    private readonly List<string> pendingLearningLines = new List<string>();
    private readonly List<string> pendingDiscussionLines = new List<string>();
    private readonly List<string> pendingForceLines = new List<string>();
    private readonly List<string> pendingMatchSummaryLines = new List<string>();
    private readonly List<string> pendingAgentSummaryLines = new List<string>();
    private readonly List<string> pendingTugLines = new List<string>();
    private readonly List<string> pendingLLMRequestLines = new List<string>();
    private float nextFlushTime;

    public string LogPath { get { return logPath; } }
    public string ConversationPath { get { return conversationPath; } }
    public string LearningPath { get { return learningPath; } }
    public string DiscussionPath { get { return discussionPath; } }
    public string ForcePath { get { return forcePath; } }
    public string MatchSummaryPath { get { return matchSummaryPath; } }
    public string AgentSummaryPath { get { return agentSummaryPath; } }
    public string AllMatchSummaryPath { get { return allMatchSummaryPath; } }
    public string AllAgentSummaryPath { get { return allAgentSummaryPath; } }
    public string AllDiscussionPath { get { return allDiscussionPath; } }

    private void Awake()
    {
        string folder = Path.Combine(Application.persistentDataPath, FolderName);
        Directory.CreateDirectory(folder);
        ApplyExperimentResetIfRequested(folder);
        string stamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        logPath = Path.Combine(folder, "pole_battle_" + stamp + ".csv");
        conversationPath = Path.Combine(folder, "conversation_" + stamp + ".txt");
        learningPath = Path.Combine(folder, "learning_" + stamp + ".txt");
        discussionPath = Path.Combine(folder, "discussion_round_" + stamp + ".csv");
        forcePath = Path.Combine(folder, "force_" + stamp + ".csv");
        matchSummaryPath = Path.Combine(folder, "match_summary_" + stamp + ".csv");
        agentSummaryPath = Path.Combine(folder, "agent_summary_" + stamp + ".csv");
        tugPath = Path.Combine(folder, "tug_poles_" + stamp + ".csv");
        llmRequestPath = Path.Combine(folder, "llm_requests_" + stamp + ".csv");
        allMatchSummaryPath = Path.Combine(folder, "all_match_summary_v2.csv");
        allAgentSummaryPath = Path.Combine(folder, "all_agent_summary_v2.csv");
        allDiscussionPath = Path.Combine(folder, "all_discussion_rounds.csv");
        allLLMRequestPath = Path.Combine(folder, "all_llm_requests.csv");
        string discussionHeader = "time,venue,match,phase,round,team,agent,preferred_role,preferred_action,leader_vote,confidence,final_leader,result,message\n";
        string matchSummaryHeader = "time,venue,match,seed,winner,result,durationSeconds,redActivePoles,blueActivePoles,greenActivePoles,yellowActivePoles,redStability,blueStability,greenStability,yellowStability,redRoles,blueRoles,greenRoles,yellowRoles,redLeader,blueLeader,greenLeader,yellowLeader\n";
        string agentSummaryHeader = "time,venue,match,team,agent,personality,compliance,ability,won,finalRole,finalAction,leaderSelections,directivesReceived,directivesFollowed,directivesDeclined,attackerAssignments,defenderAssignments,supporterAssignments,pushSamples,estimatedForceSum,avgBaseForceWhenPushing\n";
        string llmRequestHeader = "time,venue,match,provider,requestType,team,agent,phase,attempt,outcome,durationSeconds,error,fallbackUsed\n";
        File.WriteAllText(logPath, "time,event,team,agent,role,action,leader,message,redStability,blueStability,posX,posZ\n");
        File.WriteAllText(conversationPath, "Conversation log\n");
        File.WriteAllText(learningPath, "Learning log\n");
        File.WriteAllText(discussionPath, discussionHeader);
        File.WriteAllText(forcePath, "time,venue,match,poleTeam,poleName,stability,tilt,pushers,redForce,blueForce,greenForce,yellowForce,netForce\n");
        File.WriteAllText(matchSummaryPath, matchSummaryHeader);
        File.WriteAllText(agentSummaryPath, agentSummaryHeader);
        File.WriteAllText(tugPath, "time,venue,match,poleNumber,posX,posZ,capturedBy,redPullers,bluePullers,redForce,blueForce\n");
        File.WriteAllText(llmRequestPath, llmRequestHeader);
        EnsureCsvWithHeader(allMatchSummaryPath, matchSummaryHeader);
        EnsureCsvWithHeader(allAgentSummaryPath, agentSummaryHeader);
        EnsureCsvWithHeader(allDiscussionPath, discussionHeader);
        EnsureCsvWithHeader(allLLMRequestPath, llmRequestHeader);
        Debug.Log("Pole battle log: " + logPath);
        Debug.Log("Pole battle discussion log: " + discussionPath);
        Debug.Log("Pole battle force log: " + forcePath);
        Debug.Log("Pole battle match summary: " + matchSummaryPath);
        Debug.Log("Pole battle agent summary: " + agentSummaryPath);
        Debug.Log("Pole battle cumulative match summary: " + allMatchSummaryPath);
        Debug.Log("Pole battle cumulative agent summary: " + allAgentSummaryPath);
    }

    private void Update()
    {
        if (Time.time >= nextFlushTime)
        {
            Flush();
            nextFlushTime = Time.time + 2f;
        }
    }

    private void OnDestroy()
    {
        Flush();
    }

    public void WriteEvent(
        string eventName,
        TeamId team,
        string agentName,
        AgentRole role,
        AgentAction action,
        string leader,
        string message,
        float redPoleHp,
        float bluePoleHp,
        Vector3 position)
    {
        string line = string.Join(",",
            Time.time.ToString("F2"),
            Escape(eventName),
            team.ToString(),
            Escape(agentName),
            role.ToString(),
            action.ToString(),
            Escape(leader),
            Escape(message),
            redPoleHp.ToString("F1"),
            bluePoleHp.ToString("F1"),
            position.x.ToString("F2"),
            position.z.ToString("F2"));

        pendingLines.Add(line);

        if (eventName == "conversation" || eventName == "role" || eventName == "leader" || eventName == "learning")
        {
            pendingConversationLines.Add(
                Time.time.ToString("F2") + " [" + team + "] " + agentName + " " + eventName + " " + message);
        }
    }

    public void WriteConversationLine(string line)
    {
        pendingConversationLines.Add(Time.time.ToString("F2") + " " + line);
    }

    public void WriteLearningLine(string line)
    {
        pendingLearningLines.Add(Time.time.ToString("F2") + " " + line);
    }

    public void WriteDiscussionRound(
        string venueId,
        int matchNumber,
        string phase,
        int roundNumber,
        TeamId team,
        string agentName,
        string preferredRole,
        string preferredAction,
        string leaderVote,
        float confidence,
        string finalLeader,
        string result,
        string message)
    {
        string line = string.Join(",",
            Time.time.ToString("F2"),
            Escape(venueId),
            FormatMatchId(venueId, matchNumber),
            Escape(phase),
            roundNumber.ToString(),
            team.ToString(),
            Escape(agentName),
            Escape(preferredRole),
            Escape(preferredAction),
            Escape(leaderVote),
            confidence.ToString("F2"),
            Escape(finalLeader),
            Escape(result),
            Escape(message));

        pendingDiscussionLines.Add(line);
    }

    public void WriteForceSnapshot(
        string venueId,
        int matchNumber,
        BattlePole pole)
    {
        if (pole == null)
        {
            return;
        }

        string line = string.Join(",",
            Time.time.ToString("F2"),
            Escape(venueId),
            FormatMatchId(venueId, matchNumber),
            pole.Team.ToString(),
            Escape(pole.name),
            pole.Stability.ToString("F1"),
            pole.TiltAngleDegrees.ToString("F1"),
            pole.LastPusherCount.ToString(),
            pole.GetLastForceFromTeam(TeamId.Red).ToString("F1"),
            pole.GetLastForceFromTeam(TeamId.Blue).ToString("F1"),
            pole.GetLastForceFromTeam(TeamId.Green).ToString("F1"),
            pole.GetLastForceFromTeam(TeamId.Yellow).ToString("F1"),
            pole.LastNetForce.ToString("F1"));

        pendingForceLines.Add(line);
    }

    public void WriteTugSnapshot(string venueId, int matchNumber, TugPole pole)
    {
        if (pole == null)
        {
            return;
        }

        string capturedBy = pole.CapturedTeam.HasValue ? pole.CapturedTeam.Value.ToString() : "";
        pendingTugLines.Add(string.Join(",",
            Time.time.ToString("F2"),
            Escape(venueId),
            FormatMatchId(venueId, matchNumber),
            pole.PoleNumber.ToString(),
            pole.transform.position.x.ToString("F2"),
            pole.transform.position.z.ToString("F2"),
            Escape(capturedBy),
            pole.LastRedPullers.ToString(),
            pole.LastBluePullers.ToString(),
            pole.LastRedForce.ToString("F1"),
            pole.LastBlueForce.ToString("F1")));
    }

    public void WriteMatchSummary(
        string venueId,
        int matchNumber,
        int randomSeed,
        string winner,
        string result,
        float durationSeconds,
        int redActivePoles,
        int blueActivePoles,
        int greenActivePoles,
        int yellowActivePoles,
        float redStability,
        float blueStability,
        float greenStability,
        float yellowStability,
        string redRoles,
        string blueRoles,
        string greenRoles,
        string yellowRoles,
        string redLeader,
        string blueLeader,
        string greenLeader,
        string yellowLeader)
    {
        string line = string.Join(",",
            Time.time.ToString("F2"),
            Escape(venueId),
            FormatMatchId(venueId, matchNumber),
            randomSeed.ToString(),
            Escape(winner),
            Escape(result),
            durationSeconds.ToString("F2"),
            redActivePoles.ToString(),
            blueActivePoles.ToString(),
            greenActivePoles.ToString(),
            yellowActivePoles.ToString(),
            redStability.ToString("F1"),
            blueStability.ToString("F1"),
            greenStability.ToString("F1"),
            yellowStability.ToString("F1"),
            Escape(redRoles),
            Escape(blueRoles),
            Escape(greenRoles),
            Escape(yellowRoles),
            Escape(redLeader),
            Escape(blueLeader),
            Escape(greenLeader),
            Escape(yellowLeader));

        pendingMatchSummaryLines.Add(line);
    }

    public void WriteAgentSummary(
        string venueId,
        int matchNumber,
        TeamId team,
        string agentName,
        string personality,
        string compliance,
        string ability,
        bool won,
        AgentRole finalRole,
        AgentAction finalAction,
        int leaderSelections,
        int directivesReceived,
        int directivesFollowed,
        int directivesDeclined,
        int attackerAssignments,
        int defenderAssignments,
        int supporterAssignments,
        int pushSamples,
        float estimatedForceSum,
        float avgBaseForceWhenPushing)
    {
        string line = string.Join(",",
            Time.time.ToString("F2"),
            Escape(venueId),
            FormatMatchId(venueId, matchNumber),
            team.ToString(),
            Escape(agentName),
            Escape(personality),
            Escape(compliance),
            Escape(ability),
            won ? "1" : "0",
            finalRole.ToString(),
            finalAction.ToString(),
            leaderSelections.ToString(),
            directivesReceived.ToString(),
            directivesFollowed.ToString(),
            directivesDeclined.ToString(),
            attackerAssignments.ToString(),
            defenderAssignments.ToString(),
            supporterAssignments.ToString(),
            pushSamples.ToString(),
            estimatedForceSum.ToString("F1"),
            avgBaseForceWhenPushing.ToString("F1"));

        pendingAgentSummaryLines.Add(line);
    }

    public void WriteLLMRequest(
        string venueId,
        int matchNumber,
        string provider,
        string requestType,
        TeamId team,
        string agentName,
        string phase,
        int attempt,
        string outcome,
        float durationSeconds,
        string error,
        bool fallbackUsed)
    {
        pendingLLMRequestLines.Add(string.Join(",",
            Time.time.ToString("F2"),
            Escape(venueId),
            FormatMatchId(venueId, matchNumber),
            Escape(provider),
            Escape(requestType),
            team.ToString(),
            Escape(agentName),
            Escape(phase),
            attempt.ToString(),
            Escape(outcome),
            durationSeconds.ToString("F3"),
            Escape(error),
            fallbackUsed ? "1" : "0"));
    }

    public void Flush()
    {
        if (pendingLines.Count == 0 || string.IsNullOrEmpty(logPath))
        {
            FlushTextLogs();
            return;
        }

        SafeAppendAllLines(logPath, pendingLines.ToArray());
        pendingLines.Clear();
        FlushTextLogs();
    }

    private void FlushTextLogs()
    {
        if (pendingConversationLines.Count > 0 && !string.IsNullOrEmpty(conversationPath))
        {
            SafeAppendAllLines(conversationPath, pendingConversationLines.ToArray());
            pendingConversationLines.Clear();
        }

        if (pendingLearningLines.Count > 0 && !string.IsNullOrEmpty(learningPath))
        {
            SafeAppendAllLines(learningPath, pendingLearningLines.ToArray());
            pendingLearningLines.Clear();
        }

        if (pendingDiscussionLines.Count > 0 && !string.IsNullOrEmpty(discussionPath))
        {
            string[] lines = pendingDiscussionLines.ToArray();
            SafeAppendAllLines(discussionPath, lines);
            if (!string.IsNullOrEmpty(allDiscussionPath))
            {
                SafeAppendAllLines(allDiscussionPath, lines);
            }
            pendingDiscussionLines.Clear();
        }

        if (pendingForceLines.Count > 0 && !string.IsNullOrEmpty(forcePath))
        {
            SafeAppendAllLines(forcePath, pendingForceLines.ToArray());
            pendingForceLines.Clear();
        }

        if (pendingTugLines.Count > 0 && !string.IsNullOrEmpty(tugPath))
        {
            SafeAppendAllLines(tugPath, pendingTugLines.ToArray());
            pendingTugLines.Clear();
        }

        if (pendingLLMRequestLines.Count > 0 && !string.IsNullOrEmpty(llmRequestPath))
        {
            string[] lines = pendingLLMRequestLines.ToArray();
            SafeAppendAllLines(llmRequestPath, lines);
            SafeAppendAllLines(allLLMRequestPath, lines);
            pendingLLMRequestLines.Clear();
        }

        if (pendingMatchSummaryLines.Count > 0 && !string.IsNullOrEmpty(matchSummaryPath))
        {
            string[] lines = pendingMatchSummaryLines.ToArray();
            SafeAppendAllLines(matchSummaryPath, lines);
            if (!string.IsNullOrEmpty(allMatchSummaryPath))
            {
                SafeAppendAllLines(allMatchSummaryPath, lines);
            }

            pendingMatchSummaryLines.Clear();
        }

        if (pendingAgentSummaryLines.Count > 0 && !string.IsNullOrEmpty(agentSummaryPath))
        {
            string[] lines = pendingAgentSummaryLines.ToArray();
            SafeAppendAllLines(agentSummaryPath, lines);
            if (!string.IsNullOrEmpty(allAgentSummaryPath))
            {
                SafeAppendAllLines(allAgentSummaryPath, lines);
            }

            pendingAgentSummaryLines.Clear();
        }
    }

    private void SafeAppendAllLines(string path, string[] lines)
    {
        try
        {
            File.AppendAllLines(path, lines);
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("Could not write pole battle log file: " + path + " (" + ex.Message + ")");
        }
    }

    private void EnsureCsvWithHeader(string path, string header)
    {
        if (string.IsNullOrEmpty(path) || File.Exists(path))
        {
            return;
        }

        File.WriteAllText(path, header);
    }

    private void ApplyExperimentResetIfRequested(string folder)
    {
        string resetPath = Path.Combine(Application.dataPath, ResetFileName);
        if (!File.Exists(resetPath))
        {
            return;
        }

        string text = File.ReadAllText(resetPath).Trim();
        if (text != "1")
        {
            return;
        }

        DeleteFiles(folder, "team_learning*.json");
        DeleteFiles(folder, "all_match_summary.csv");
        DeleteFiles(folder, "all_agent_summary.csv");
        DeleteFiles(folder, "all_match_summary_v2.csv");
        DeleteFiles(folder, "all_agent_summary_v2.csv");
        DeleteFiles(folder, "all_discussion_rounds.csv");
        DeleteFiles(folder, "all_llm_requests.csv");
        File.WriteAllText(resetPath, "0");
        Debug.Log("Pole battle learning memory and cumulative summaries were reset.");
    }

    private void DeleteFiles(string folder, string pattern)
    {
        string[] files = Directory.GetFiles(folder, pattern);
        for (int i = 0; i < files.Length; i++)
        {
            File.Delete(files[i]);
        }
    }

    private string Escape(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        string escaped = value.Replace("\"", "\"\"");
        return "\"" + escaped + "\"";
    }

    private string FormatMatchId(string venueId, int matchNumber)
    {
        bool practice = !string.IsNullOrEmpty(venueId) && venueId.EndsWith("_PRACTICE");
        return (practice ? "P" : "M") + matchNumber.ToString(practice ? "00" : "000");
    }
}
