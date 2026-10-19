using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

public class PoleBattleGameManager : MonoBehaviour
{
    private const string LeaderDefinition = "Leader = the agent elected once before an official match; the leader remains fixed until that match ends and issues tactical directives during play.";
    private const string PersonalityDefinitions = "Personality definitions: assertive proposes decisive attacks; cautious prioritizes risk reduction and defense; cooperative coordinates with teammates; analytical compares state and force vectors; adaptive changes plans when the situation changes; risk-taking accepts danger for a high payoff.";
    private const int CurriculumCheckMatches = 10;
    private const int CurriculumTrainingMatches = 100;
    private const int CurriculumEvaluationMatches = 20;
    private const int CurriculumMatchesPerStage = CurriculumCheckMatches + CurriculumTrainingMatches + CurriculumEvaluationMatches;
    private static readonly string[] CurriculumGoals =
    {
        "Move accurately to assigned destinations, remain inside the field, and avoid overlapping teammates.",
        "Approach the active target pole efficiently and choose an uncongested contact position.",
        "Apply force in the correct vector and avoid cancelling teammate force.",
        "Improve one-agent attack positioning and adjust to the pole's changing lean.",
        "Coordinate group attacks from compatible directions without crowding one point.",
        "Defend the own pole by prioritizing danger and applying stabilizing force opposite its lean.",
        "Support teammates by filling shortages in attack or defense without obstructing opponents.",
        "Form a balanced attacker, defender, and supporter allocation for the current state.",
        "Share information: propose, compare, criticize, and revise strategies using teammate evidence.",
        "Practice leader basics: specify target, direction, and team allocation clearly.",
        "Practice responding to leader directives according to each avatar's fixed compliance setting.",
        "Adapt leader directives when pole danger, force balance, or team allocation changes.",
        "Prioritize among multiple objectives and stop targeting inactive objectives.",
        "Integrate movement, force, roles, discussion, leadership, and adaptation in a full battle."
    };
    private static int globalMatchNumber = 1;
    private static bool curriculumSelectionConfirmed;
    private static readonly Dictionary<int, InstructionCompliance> sessionCompliance = new Dictionary<int, InstructionCompliance>();
    private static readonly Dictionary<string, InstructionCompliance> teamSessionCompliance = new Dictionary<string, InstructionCompliance>(System.StringComparer.OrdinalIgnoreCase);
    private static bool sessionComplianceLoaded;

    public enum LLMProvider
    {
        OpenAI,
        OllamaLocal
    }

    public BattlePole RedPole;
    public BattlePole BluePole;
    public float DiscussionInterval = 45f;
    public float LeaderInstructionInterval = 10f;
    public int PracticeMatchCount = 10;
    public float MaxBattleSeconds = 180f;
    public bool UseLLMBrain = true;
    public bool UseIndividualLLMAgents = true;
    public bool RequireLLMOpeningPlan = true;
    public string OpenAIModel = "gpt-4.1-mini";
    public string OpenAIApiKey = "";
    public string OpenAIKeyFileName = "OpenAIKey.txt";
    public string RunCountFileName = "RunCount.txt";
    public string InstructionComplianceFileName = "InstructionCompliance.txt";
    public LLMProvider Provider = LLMProvider.OpenAI;
    public string LocalLLMModel = "llama3.2:latest";
    public string OllamaGenerateUrl = "http://localhost:11434/api/generate";
    public int MaxLLMOutputTokens = 650;
    public int LLMRetryCount = 2;
    public bool UseFallbackOpinionOnLLMFailure = true;
    public bool AutoRestartBattles = true;
    public float RestartDelaySeconds = 4f;
    public float ForceLogInterval = 0.5f;
    public float LLMRequestTimeoutSeconds = 45f;
    public float OpeningLLMRetrySeconds = 3f;
    public int MinimumLLMAgentOpinionsToStart = 6;
    public float IndividualAgentThinkingDelaySeconds = 0.15f;
    public int MaxLLMSpeakersPerTeam;
    public int MaxActivePushersPerPole;
    public string VenueId = "A";
    public string VenueName = "Initial 6v6 individual LLM";
    public bool RequireAllPolesDownToLose;
    public bool RequireLastPoleStanding;
    public bool UseTugOfWarRules;
    public bool BattleStarted { get; private set; }
    public bool BattleFinished { get; private set; }

    private readonly List<PoleAgent> redAgents = new List<PoleAgent>();
    private readonly List<PoleAgent> blueAgents = new List<PoleAgent>();
    private readonly List<PoleAgent> greenAgents = new List<PoleAgent>();
    private readonly List<PoleAgent> yellowAgents = new List<PoleAgent>();
    private readonly List<BattlePole> redPoles = new List<BattlePole>();
    private readonly List<BattlePole> bluePoles = new List<BattlePole>();
    private readonly List<BattlePole> greenPoles = new List<BattlePole>();
    private readonly List<BattlePole> yellowPoles = new List<BattlePole>();
    private readonly List<TugPole> tugPoles = new List<TugPole>();
    private readonly Dictionary<PoleAgent, int> tugPoleAssignments = new Dictionary<PoleAgent, int>();
    private readonly Dictionary<PoleAgent, float> recentPressure = new Dictionary<PoleAgent, float>();
    private readonly Dictionary<PoleAgent, float> nextPushLogTime = new Dictionary<PoleAgent, float>();
    private readonly Dictionary<PoleAgent, int> leaderSelectionsThisMatch = new Dictionary<PoleAgent, int>();
    private readonly Dictionary<PoleAgent, int> attackerAssignmentsThisMatch = new Dictionary<PoleAgent, int>();
    private readonly Dictionary<PoleAgent, int> defenderAssignmentsThisMatch = new Dictionary<PoleAgent, int>();
    private readonly Dictionary<PoleAgent, int> supporterAssignmentsThisMatch = new Dictionary<PoleAgent, int>();
    private readonly Dictionary<PoleAgent, int> pushSamplesThisMatch = new Dictionary<PoleAgent, int>();
    private readonly Dictionary<PoleAgent, float> estimatedForceSumThisMatch = new Dictionary<PoleAgent, float>();
    private readonly List<string> conversationRows = new List<string>();
    private readonly List<string> pushRows = new List<string>();
    private readonly List<string> forceRows = new List<string>();
    private readonly List<string> leaderRows = new List<string>();
    private TeamBrain redBrain;
    private TeamBrain blueBrain;
    private TeamBrain greenBrain;
    private TeamBrain yellowBrain;
    private ExperimentLogger logger;
    private float nextLeaderInstructionTime;
    private float nextForceLogTime;
    private float finishedBattleDurationSeconds = -1f;
    private int configuredMatchLimit;
    private bool redLLMInFlight;
    private bool blueLLMInFlight;
    private bool greenLLMInFlight;
    private bool yellowLLMInFlight;
    private bool redOpeningPlanReady;
    private bool blueOpeningPlanReady;
    private bool greenOpeningPlanReady;
    private bool yellowOpeningPlanReady;
    private float battleStartTime;
    private string winnerText = "";
    private string llmStatus = "LLM: starting";
    private LearningSaveData learningData;
    private string learningSavePath;
    private int matchNumber;
    private int sessionMatchIndex;
    private bool isPracticeMatch;
    private int experimentSeed;
    private string currentRedLeader = "";
    private string currentBlueLeader = "";
    private string currentGreenLeader = "";
    private string currentYellowLeader = "";
    private Vector3 redTugHome;
    private Vector3 blueTugHome;
    private Vector2 fieldHalfExtents = new Vector2(16f, 10f);
    private CurriculumControlData curriculumControl;
    private string curriculumControlPath;
    private int pendingCurriculumMode = 1;
    private const int CurriculumTargetMatches = 1000;
    private const int CurriculumLearningMatches = 900;
    private const int CurriculumHeldOutEvaluationMatches = 100;
    private bool stopAfterCurrentMatch;
    private bool resetStageConfirmation;
    private bool showCurriculumProgress;
    private bool curriculumMatchFailed;
    private bool curriculumReflectionStarted;
    private string curriculumFailureReason = "";
    private float curriculumPeakNearTargetRatio;
    private float curriculumPeakEnemyTilt;
    private float curriculumPeakNetForce;
    private float curriculumMinimumOwnStability = 100f;

    private void Awake()
    {
        sessionMatchIndex = globalMatchNumber;
        isPracticeMatch = sessionMatchIndex <= PracticeMatchCount;
        matchNumber = isPracticeMatch ? sessionMatchIndex : sessionMatchIndex - PracticeMatchCount;
        logger = GetComponent<ExperimentLogger>();
        if (logger == null)
        {
            logger = gameObject.AddComponent<ExperimentLogger>();
        }

        LoadApiKey();
        LoadRunLimit();
        LoadInstructionCompliance();
    }

    public static void ResetMatchCounter()
    {
        globalMatchNumber = 1;
        sessionComplianceLoaded = false;
        sessionCompliance.Clear();
        teamSessionCompliance.Clear();
    }

    public void ConfigureSchedule(int practiceMatches, float instructionIntervalSeconds)
    {
        LeaderInstructionInterval = Mathf.Max(1f, instructionIntervalSeconds);
        sessionMatchIndex = globalMatchNumber;
        if (VenueId == "A")
        {
            LoadCurriculumControl();
            pendingCurriculumMode = curriculumControl.SelectedMode;
            if (curriculumControl.SelectedMode <= CurriculumGoals.Length)
            {
                PracticeMatchCount = CurriculumTargetMatches;
                isPracticeMatch = true;
                matchNumber = GetSelectedCurriculumCompletedCount() + 1;
            }
            else
            {
                PracticeMatchCount = 0;
                isPracticeMatch = false;
                matchNumber = sessionMatchIndex;
            }
        }
        else
        {
            PracticeMatchCount = Mathf.Max(0, practiceMatches);
            isPracticeMatch = sessionMatchIndex <= PracticeMatchCount;
            matchNumber = isPracticeMatch ? sessionMatchIndex : sessionMatchIndex - PracticeMatchCount;
        }
    }

    private bool IsVenueOneCurriculum
    {
        get { return VenueId == "A" && isPracticeMatch && curriculumControl != null && curriculumControl.SelectedMode <= CurriculumGoals.Length; }
    }

    private int GetCurriculumStageIndex()
    {
        if (curriculumControl != null && curriculumControl.SelectedMode >= 1 && curriculumControl.SelectedMode <= CurriculumGoals.Length)
        {
            return curriculumControl.SelectedMode - 1;
        }
        return Mathf.Clamp((sessionMatchIndex - 1) / CurriculumMatchesPerStage, 0, CurriculumGoals.Length - 1);
    }

    private int GetCurriculumStageMatchIndex()
    {
        if (IsVenueOneCurriculum)
        {
            return Mathf.Min(CurriculumTargetMatches, GetSelectedCurriculumCompletedCount() + 1);
        }
        return ((Mathf.Max(1, sessionMatchIndex) - 1) % CurriculumMatchesPerStage) + 1;
    }

    private string GetCurriculumPhaseCode()
    {
        if (IsVenueOneCurriculum)
        {
            return GetSelectedCurriculumCompletedCount() < CurriculumLearningMatches ? "T" : "E";
        }
        int index = GetCurriculumStageMatchIndex();
        if (index <= CurriculumCheckMatches)
        {
            return "D";
        }
        if (index <= CurriculumCheckMatches + CurriculumTrainingMatches)
        {
            return "T";
        }
        return "E";
    }

    private int GetCurriculumPhaseMatchNumber()
    {
        if (IsVenueOneCurriculum)
        {
            int completed = GetSelectedCurriculumCompletedCount();
            return completed < CurriculumLearningMatches
                ? completed + 1
                : completed - CurriculumLearningMatches + 1;
        }
        int index = GetCurriculumStageMatchIndex();
        if (index <= CurriculumCheckMatches)
        {
            return index;
        }
        if (index <= CurriculumCheckMatches + CurriculumTrainingMatches)
        {
            return index - CurriculumCheckMatches;
        }
        return index - CurriculumCheckMatches - CurriculumTrainingMatches;
    }

    private bool ShouldUpdateLearning()
    {
        return !IsVenueOneCurriculum || (!curriculumMatchFailed && GetSelectedCurriculumCompletedCount() < CurriculumLearningMatches);
    }

    private bool CurriculumUsesDiscussion()
    {
        return !IsVenueOneCurriculum || GetCurriculumStageIndex() >= 4;
    }

    private bool CurriculumUsesFullDiscussion()
    {
        return !IsVenueOneCurriculum || GetCurriculumStageIndex() >= 7;
    }

    private bool CurriculumUsesLLMRoles()
    {
        return !IsVenueOneCurriculum || GetCurriculumStageIndex() >= 7;
    }

    private bool ShouldElectLeader()
    {
        return !isPracticeMatch || (IsVenueOneCurriculum && GetCurriculumStageIndex() >= 9);
    }

    private void ApplyCurriculumFixedPlans()
    {
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            ApplyCurriculumFixedPlan(teams[i]);
            SetOpeningPlanReady(teams[i], true);
        }
    }

    private void ApplyCurriculumFixedPlan(TeamId team)
    {
        List<PoleAgent> members = GetMembers(team);
        int stage = GetCurriculumStageIndex() + 1;
        for (int i = 0; i < members.Count; i++)
        {
            AgentRole role = AgentRole.Attacker;
            AgentAction action = AgentAction.MoveToEnemyPole;
            if (stage == 6)
            {
                role = AgentRole.Defender;
                action = AgentAction.GuardOwnPole;
            }
            else if (stage == 7)
            {
                bool support = i % 2 == 1;
                role = support ? AgentRole.Supporter : AgentRole.Attacker;
                action = support ? AgentAction.SupportNearestAttacker : AgentAction.MoveToEnemyPole;
            }

            string message = "Curriculum stage " + stage + " fixed exercise; leadership disabled.";
            members[i].ApplyDecision(new AgentDecision(role, action, message));
            if (stage >= 5)
            {
                RecordRoleAssignment(members[i], role);
            }
            LogConversation(team, members[i].AgentName, message);
        }
    }

    private string GetCurriculumSummary()
    {
        if (!IsVenueOneCurriculum)
        {
            return "";
        }

        int stage = GetCurriculumStageIndex() + 1;
        string phase = GetCurriculumPhaseCode();
        int phaseTotal = IsVenueOneCurriculum
            ? (phase == "T" ? CurriculumLearningMatches : CurriculumHeldOutEvaluationMatches)
            : phase == "D" ? CurriculumCheckMatches
            : phase == "T" ? CurriculumTrainingMatches
            : CurriculumEvaluationMatches;
        string phaseName = phase == "D" ? "Check"
            : phase == "T" ? "Training"
            : "Evaluation";
        return "Curriculum Stage " + stage.ToString("00") + "/" + CurriculumGoals.Length.ToString("00")
            + " | " + phaseName + " " + phase + GetCurriculumPhaseMatchNumber().ToString("000") + "/" + phaseTotal.ToString("000")
            + " | saved " + GetSelectedCurriculumCompletedCount().ToString() + "/" + CurriculumTargetMatches.ToString()
            + " goal: " + CurriculumGoals[stage - 1];
    }

    private void LoadCurriculumControl()
    {
        string folder = Path.Combine(Application.persistentDataPath, "PoleBattleLogs");
        Directory.CreateDirectory(folder);
        curriculumControlPath = Path.Combine(folder, "curriculum_progress_A.json");
        curriculumControl = null;
        if (File.Exists(curriculumControlPath))
        {
            try
            {
                curriculumControl = JsonUtility.FromJson<CurriculumControlData>(File.ReadAllText(curriculumControlPath));
            }
            catch
            {
                curriculumControl = null;
            }
        }

        if (curriculumControl == null)
        {
            curriculumControl = new CurriculumControlData();
            RestoreCurriculumCountsFromCsv(folder);
        }
        curriculumControl.EnsureValid(CurriculumGoals.Length);
        SaveCurriculumControl();
    }

    private void RestoreCurriculumCountsFromCsv(string folder)
    {
        curriculumControl.EnsureValid(CurriculumGoals.Length);
        string[] files = Directory.GetFiles(folder, "match_summary_*.csv");
        for (int f = 0; f < files.Length; f++)
        {
            string[] lines;
            try
            {
                lines = File.ReadAllLines(files[f]);
            }
            catch
            {
                continue;
            }
            for (int line = 1; line < lines.Length; line++)
            {
                for (int stage = 1; stage <= CurriculumGoals.Length; stage++)
                {
                    string marker = ",A_S" + stage.ToString("00") + "_T,";
                    if (lines[line].Contains(marker))
                    {
                        curriculumControl.StageTrainingCounts[stage - 1]++;
                        break;
                    }
                }
            }
        }
        for (int i = 0; i < curriculumControl.StageTrainingCounts.Length; i++)
        {
            curriculumControl.StageTrainingCounts[i] = Mathf.Clamp(curriculumControl.StageTrainingCounts[i], 0, CurriculumTargetMatches);
        }
    }

    private void SaveCurriculumControl()
    {
        if (curriculumControl != null && !string.IsNullOrEmpty(curriculumControlPath))
        {
            File.WriteAllText(curriculumControlPath, JsonUtility.ToJson(curriculumControl, true));
        }
    }

    private int GetSelectedCurriculumCompletedCount()
    {
        if (curriculumControl == null || curriculumControl.SelectedMode < 1 || curriculumControl.SelectedMode > CurriculumGoals.Length)
        {
            return 0;
        }
        curriculumControl.EnsureValid(CurriculumGoals.Length);
        return curriculumControl.StageTrainingCounts[curriculumControl.SelectedMode - 1];
    }

    private void RecordCurriculumProgress(TeamId? winner)
    {
        if (!IsVenueOneCurriculum || curriculumControl.SelectedMode < 1 || curriculumControl.SelectedMode > CurriculumGoals.Length)
        {
            return;
        }
        int index = curriculumControl.SelectedMode - 1;
        curriculumControl.EnsureValid(CurriculumGoals.Length);
        bool evaluationMatch = curriculumControl.StageTrainingCounts[index] >= CurriculumLearningMatches;
        bool success = !curriculumMatchFailed && EvaluateCurriculumSuccess(winner);
        if (curriculumMatchFailed)
        {
            curriculumControl.StageFailedCounts[index]++;
            curriculumControl.CurrentBlockFailedCounts[index]++;
        }
        else
        {
            curriculumControl.StageTrainingCounts[index] = Mathf.Clamp(curriculumControl.StageTrainingCounts[index] + 1, 0, CurriculumTargetMatches);
            if (evaluationMatch)
            {
                curriculumControl.StageEvaluationCounts[index]++;
                curriculumControl.StageEvaluationSuccessCounts[index] += success ? 1 : 0;
            }
            else
            {
                curriculumControl.StageSuccessCounts[index] += success ? 1 : 0;
            }
            curriculumControl.StageTotalDuration[index] += Mathf.Max(0f, finishedBattleDurationSeconds);
            curriculumControl.CurrentBlockValidCounts[index]++;
            curriculumControl.CurrentBlockSuccessCounts[index] += success ? 1 : 0;
            curriculumControl.CurrentBlockDuration[index] += Mathf.Max(0f, finishedBattleDurationSeconds);
        }
        curriculumControl.LastProvider = Provider.ToString();
        curriculumControl.LastModel = Provider == LLMProvider.OpenAI ? OpenAIModel : LocalLLMModel;
        curriculumControl.LastSeed = experimentSeed;
        curriculumControl.LastCompliance = BuildComplianceConditionSummary();
        curriculumControl.LastPersonalities = BuildPersonalityConditionSummary();
        curriculumControl.LastUpdatedUtc = System.DateTime.UtcNow.ToString("o");
        WriteCurriculumMetricRow(winner, success);
        if (!curriculumMatchFailed && curriculumControl.StageTrainingCounts[index] > 0
            && curriculumControl.StageTrainingCounts[index] % 100 == 0)
        {
            WriteCurriculumCheckpoint(index);
        }
        SaveCurriculumControl();
    }

    private void WriteCurriculumCheckpoint(int index)
    {
        string path = Path.Combine(Path.GetDirectoryName(curriculumControlPath), "curriculum_checkpoints_A.csv");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "timestampUtc,stage,completed,phase,blockSuccessRate,blockAverageSeconds,blockFailed,teamRedMemory,teamBlueMemory,model,seed\n");
        }
        int valid = curriculumControl.CurrentBlockValidCounts[index];
        float rate = valid > 0 ? curriculumControl.CurrentBlockSuccessCounts[index] * 100f / valid : 0f;
        float average = valid > 0 ? curriculumControl.CurrentBlockDuration[index] / valid : 0f;
        System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
        string line = string.Join(",",
            EscapeCurriculumCsv(System.DateTime.UtcNow.ToString("o")),
            (index + 1).ToString(),
            curriculumControl.StageTrainingCounts[index].ToString(),
            curriculumControl.StageTrainingCounts[index] <= CurriculumLearningMatches ? "Training" : "Evaluation",
            rate.ToString("F2", culture),
            average.ToString("F3", culture),
            curriculumControl.CurrentBlockFailedCounts[index].ToString(),
            EscapeCurriculumCsv(GetLearningState(TeamId.Red).GetSummary()),
            EscapeCurriculumCsv(GetLearningState(TeamId.Blue).GetSummary()),
            EscapeCurriculumCsv(curriculumControl.LastModel),
            experimentSeed.ToString());
        File.AppendAllText(path, line + "\n");
        curriculumControl.CurrentBlockValidCounts[index] = 0;
        curriculumControl.CurrentBlockSuccessCounts[index] = 0;
        curriculumControl.CurrentBlockFailedCounts[index] = 0;
        curriculumControl.CurrentBlockDuration[index] = 0f;
    }

    private void WriteCurriculumMetricRow(TeamId? winner, bool success)
    {
        if (string.IsNullOrEmpty(curriculumControlPath)) return;
        string path = Path.Combine(Path.GetDirectoryName(curriculumControlPath), "curriculum_metrics_A_v2.csv");
        if (!File.Exists(path))
        {
            File.WriteAllText(path, "timestampUtc,stage,completed,phase,success,failed,failureReason,durationSeconds,nearTargetRatio,maxTilt,maxNetForce,minOwnStability,winner,provider,model,seed,compliance\n");
        }
        System.Globalization.CultureInfo culture = System.Globalization.CultureInfo.InvariantCulture;
        string line = string.Join(",",
            EscapeCurriculumCsv(System.DateTime.UtcNow.ToString("o")),
            (GetCurriculumStageIndex() + 1).ToString(),
            GetSelectedCurriculumCompletedCount().ToString(),
            GetSelectedCurriculumCompletedCount() <= CurriculumLearningMatches ? "Training" : "Evaluation",
            success ? "1" : "0",
            curriculumMatchFailed ? "1" : "0",
            EscapeCurriculumCsv(curriculumFailureReason),
            finishedBattleDurationSeconds.ToString("F3", culture),
            curriculumPeakNearTargetRatio.ToString("F3", culture),
            curriculumPeakEnemyTilt.ToString("F3", culture),
            curriculumPeakNetForce.ToString("F3", culture),
            curriculumMinimumOwnStability.ToString("F3", culture),
            winner.HasValue ? winner.Value.ToString() : "Draw",
            Provider.ToString(),
            EscapeCurriculumCsv(curriculumControl.LastModel),
            experimentSeed.ToString(),
            EscapeCurriculumCsv(curriculumControl.LastCompliance));
        File.AppendAllText(path, line + "\n");
    }

    private string EscapeCurriculumCsv(string value)
    {
        value = value ?? "";
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private void UpdateCurriculumMetrics()
    {
        if (!IsVenueOneCurriculum) return;
        int total = 0;
        int near = 0;
        TeamId[] teams = GetActiveTeams();
        for (int t = 0; t < teams.Length; t++)
        {
            BattlePole target = GetEnemyPole(teams[t]);
            List<PoleAgent> members = GetMembers(teams[t]);
            for (int i = 0; i < members.Count; i++)
            {
                Vector3 position = members[i].transform.position;
                if (float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z)
                    || float.IsInfinity(position.x) || float.IsInfinity(position.y) || float.IsInfinity(position.z))
                {
                    curriculumMatchFailed = true;
                    curriculumFailureReason = "invalid agent position";
                }
                total++;
                if (target != null && Vector3.Distance(position, target.transform.position) <= 3f) near++;
            }
            curriculumMinimumOwnStability = Mathf.Min(curriculumMinimumOwnStability, GetTeamAverageStability(teams[t]));
        }
        if (total > 0) curriculumPeakNearTargetRatio = Mathf.Max(curriculumPeakNearTargetRatio, (float)near / total);

        List<BattlePole> poles = GetAllPoles();
        for (int i = 0; i < poles.Count; i++)
        {
            if (poles[i] == null) continue;
            curriculumPeakEnemyTilt = Mathf.Max(curriculumPeakEnemyTilt, poles[i].TiltAngleDegrees);
            curriculumPeakNetForce = Mathf.Max(curriculumPeakNetForce, Mathf.Abs(poles[i].LastNetForce));
        }
    }

    private bool EvaluateCurriculumSuccess(TeamId? winner)
    {
        int stage = GetCurriculumStageIndex() + 1;
        if (stage <= 2) return curriculumPeakNearTargetRatio >= 0.70f;
        if (stage == 3) return curriculumPeakNetForce >= 40f;
        if (stage == 4) return curriculumPeakEnemyTilt >= 12f;
        if (stage == 5) return curriculumPeakNetForce >= 80f;
        if (stage == 6) return curriculumMinimumOwnStability >= 35f;
        if (stage == 7) return curriculumPeakNearTargetRatio >= 0.50f;
        if (stage == 8) return HasRoleDiversity(TeamId.Red) && HasRoleDiversity(TeamId.Blue);
        if (stage == 9) return !curriculumMatchFailed;
        if (stage == 10) return !string.IsNullOrEmpty(currentRedLeader) && !string.IsNullOrEmpty(currentBlueLeader);
        if (stage == 11) return CountLeaderDirectivesReceived() > 0;
        if (stage == 12) return CountLeaderDirectivesReceived() >= 4;
        return winner.HasValue;
    }

    private bool HasRoleDiversity(TeamId team)
    {
        bool attacker = false, defender = false, supporter = false;
        List<PoleAgent> members = GetMembers(team);
        for (int i = 0; i < members.Count; i++)
        {
            attacker |= members[i].Role == AgentRole.Attacker;
            defender |= members[i].Role == AgentRole.Defender;
            supporter |= members[i].Role == AgentRole.Supporter;
        }
        return attacker && defender && supporter;
    }

    private int CountLeaderDirectivesReceived()
    {
        int count = 0;
        TeamId[] teams = GetActiveTeams();
        for (int t = 0; t < teams.Length; t++)
        {
            List<PoleAgent> members = GetMembers(teams[t]);
            for (int i = 0; i < members.Count; i++) count += members[i].LeaderDirectivesReceived;
        }
        return count;
    }

    private string BuildComplianceConditionSummary()
    {
        StringBuilder builder = new StringBuilder();
        TeamId[] teams = GetActiveTeams();
        for (int t = 0; t < teams.Length; t++)
        {
            List<PoleAgent> members = GetMembers(teams[t]);
            for (int i = 0; i < members.Count; i++)
            {
                if (builder.Length > 0) builder.Append(";");
                builder.Append(members[i].AgentName).Append("=").Append(members[i].Compliance);
            }
        }
        return builder.ToString();
    }

    private string BuildPersonalityConditionSummary()
    {
        StringBuilder builder = new StringBuilder();
        TeamId[] teams = GetActiveTeams();
        for (int t = 0; t < teams.Length; t++)
        {
            List<PoleAgent> members = GetMembers(teams[t]);
            for (int i = 0; i < members.Count; i++)
            {
                if (builder.Length > 0) builder.Append(";");
                builder.Append(members[i].AgentName).Append("=")
                    .Append(GetLearningState(teams[t]).GetPersonality(members[i].AgentName));
            }
        }
        return builder.ToString();
    }

    private void Start()
    {
        Time.timeScale = 1f;
        experimentSeed = BuildExperimentSeed(VenueId, sessionMatchIndex);
        Random.InitState(experimentSeed);
        LoadLearningData();
        redBrain = new TeamBrain(TeamId.Red, redAgents, this);
        blueBrain = new TeamBrain(TeamId.Blue, blueAgents, this);
        greenBrain = new TeamBrain(TeamId.Green, greenAgents, this);
        yellowBrain = new TeamBrain(TeamId.Yellow, yellowAgents, this);
        llmStatus = IsLLMReady()
            ? "LLM: active via " + Provider
            : GetMissingLLMMessage();
        ValidateVenueConfiguration();
        if (VenueId == "A" && !curriculumSelectionConfirmed)
        {
            BattleStarted = false;
            BattleFinished = false;
            llmStatus = "Paused: choose Stage 1-14 or Match mode, then press Start / Apply";
            return;
        }
        if (IsVenueOneCurriculum && GetSelectedCurriculumCompletedCount() >= CurriculumTargetMatches)
        {
            BattleStarted = false;
            BattleFinished = true;
            winnerText = "Selected curriculum stage has completed 1000 training matches. Choose another stage or Match mode.";
            llmStatus = "Curriculum target reached";
            return;
        }
        StartCoroutine(BeginBattleAfterOpeningDiscussion());
    }

    private int BuildExperimentSeed(string venueId, int sessionIndex)
    {
        unchecked
        {
            int hash = 17;
            string value = string.IsNullOrEmpty(venueId) ? "A" : venueId;
            for (int i = 0; i < value.Length; i++)
            {
                hash = hash * 31 + value[i];
            }
            return hash * 397 ^ sessionIndex;
        }
    }

    private void ValidateVenueConfiguration()
    {
        if (!UseTugOfWarRules)
        {
            return;
        }

        bool valid = tugPoles.Count == 5 && redAgents.Count == 20 && blueAgents.Count == 20 && Mathf.Approximately(MaxBattleSeconds, 180f);
        string message = valid
            ? "Venue C validation passed: 5 central tug poles, 20v20, 180-second limit, additive pull force, captured poles locked."
            : "Venue C validation FAILED: poles=" + tugPoles.Count + ", red=" + redAgents.Count + ", blue=" + blueAgents.Count + ", limit=" + MaxBattleSeconds.ToString("F0");
        if (valid)
        {
            Debug.Log(message);
        }
        else
        {
            Debug.LogError(message);
        }
        if (logger != null)
        {
            logger.WriteLearningLine(message);
        }
    }

    private void Update()
    {
        if (BattleFinished)
        {
            return;
        }

        if (!BattleStarted)
        {
            return;
        }

        UpdateCurriculumMetrics();

        if (Time.time >= nextLeaderInstructionTime && !string.IsNullOrEmpty(currentRedLeader + currentBlueLeader + currentGreenLeader + currentYellowLeader))
        {
            IssueActiveLeaderDirectives();
            nextLeaderInstructionTime = Time.time + LeaderInstructionInterval;
            DecayPressureScores();
        }

        if (Time.time >= nextForceLogTime)
        {
            LogForceSnapshots();
            nextForceLogTime = Time.time + ForceLogInterval;
        }

        if (UseTugOfWarRules && AreAllTugPolesCaptured())
        {
            TeamId? tugWinner = ChooseTugWinner();
            FinishBattle(BuildTugResult(tugWinner, "all five poles captured"), tugWinner);
        }

        if (UseTugOfWarRules)
        {
            // Tug-of-war victory is decided by captured poles, not fallen poles.
        }
        else if (RequireLastPoleStanding)
        {
            TeamId? winner = GetLastStandingTeam();
            if (winner.HasValue)
            {
                FinishBattle(winner.Value + " wins: last pole standing.", winner);
            }
        }
        else if (RequireAllPolesDownToLose)
        {
            bool redAllDown = AreAllPolesDown(TeamId.Red);
            bool blueAllDown = AreAllPolesDown(TeamId.Blue);
            if (redAllDown && blueAllDown)
            {
                FinishBattle("Draw: all poles fell.", null);
            }
            else if (redAllDown)
            {
                FinishBattle("Blue wins: all red poles fell.", TeamId.Blue);
            }
            else if (blueAllDown)
            {
                FinishBattle("Red wins: all blue poles fell.", TeamId.Red);
            }
        }
        else if (RedPole != null && RedPole.IsDown)
        {
            FinishBattle("Blue wins: red pole fell.", TeamId.Blue);
        }
        else if (BluePole != null && BluePole.IsDown)
        {
            FinishBattle("Red wins: blue pole fell.", TeamId.Red);
        }

        if (BattleFinished)
        {
            return;
        }

        if (Time.time - battleStartTime >= MaxBattleSeconds)
        {
            TeamId? winner = UseTugOfWarRules ? ChooseTugWinner() : ChooseWinnerByAverageStability();
            string result = UseTugOfWarRules
                ? BuildTugResult(winner, "180 second time limit")
                : (winner.HasValue ? winner.Value + " wins by pole stability." : "Draw: equal pole stability.");
            FinishBattle(result, winner);
        }
    }

    public void RegisterPole(BattlePole pole)
    {
        switch (pole.Team)
        {
            case TeamId.Red:
                redPoles.Add(pole);
                if (RedPole == null)
                {
                    RedPole = pole;
                }
                break;
            case TeamId.Blue:
                bluePoles.Add(pole);
                if (BluePole == null)
                {
                    BluePole = pole;
                }
                break;
            case TeamId.Green:
                greenPoles.Add(pole);
                break;
            case TeamId.Yellow:
                yellowPoles.Add(pole);
                break;
        }
    }

    public void RegisterAgent(PoleAgent agent)
    {
        agent.Compliance = GetInstructionCompliance(agent.AgentName);
        switch (agent.Team)
        {
            case TeamId.Red:
                redAgents.Add(agent);
                break;
            case TeamId.Blue:
                blueAgents.Add(agent);
                break;
            case TeamId.Green:
                greenAgents.Add(agent);
                break;
            case TeamId.Yellow:
                yellowAgents.Add(agent);
                break;
        }

        if (!recentPressure.ContainsKey(agent))
        {
            recentPressure.Add(agent, 0f);
        }

        EnsureAgentSummaryTracking(agent);
    }

    public void ConfigureTugOfWar(Vector3 redHome, Vector3 blueHome)
    {
        redTugHome = redHome;
        blueTugHome = blueHome;
    }

    public void ConfigureFieldBounds(Vector2 fieldSize)
    {
        fieldHalfExtents = new Vector2(
            Mathf.Max(2f, fieldSize.x * 0.5f - 0.9f),
            Mathf.Max(2f, fieldSize.y * 0.5f - 0.9f));
    }

    public Vector3 ClampAgentToField(Vector3 position)
    {
        position.x = Mathf.Clamp(position.x, -fieldHalfExtents.x, fieldHalfExtents.x);
        position.z = Mathf.Clamp(position.z, -fieldHalfExtents.y, fieldHalfExtents.y);
        return position;
    }

    public void RegisterTugPole(TugPole pole)
    {
        if (pole != null && !tugPoles.Contains(pole))
        {
            tugPoles.Add(pole);
        }
    }

    public TugPole GetAssignedTugPole(PoleAgent agent)
    {
        if (agent == null || tugPoles.Count == 0)
        {
            return null;
        }

        int assignedNumber;
        if (tugPoleAssignments.TryGetValue(agent, out assignedNumber))
        {
            TugPole assigned = FindTugPole(assignedNumber);
            if (assigned != null && !assigned.IsCaptured)
            {
                return assigned;
            }
        }

        List<TugPole> available = new List<TugPole>();
        for (int i = 0; i < tugPoles.Count; i++)
        {
            if (tugPoles[i] != null && !tugPoles[i].IsCaptured)
            {
                available.Add(tugPoles[i]);
            }
        }

        if (available.Count == 0)
        {
            return null;
        }

        int index = (ExtractAgentNumber(agent.AgentName) - 1) % available.Count;
        tugPoleAssignments[agent] = available[index].PoleNumber;
        return available[index];
    }

    public void AssignAgentToTugPole(PoleAgent agent, int poleNumber)
    {
        TugPole pole = FindTugPole(poleNumber);
        if (agent != null && pole != null && !pole.IsCaptured)
        {
            tugPoleAssignments[agent] = poleNumber;
        }
    }

    private TugPole FindTugPole(int poleNumber)
    {
        for (int i = 0; i < tugPoles.Count; i++)
        {
            if (tugPoles[i] != null && tugPoles[i].PoleNumber == poleNumber)
            {
                return tugPoles[i];
            }
        }

        return null;
    }

    public BattlePole GetOwnPole(TeamId team)
    {
        return GetActivePole(team);
    }

    public BattlePole GetEnemyPole(TeamId team)
    {
        BattlePole ownPole = GetActivePole(team);
        Vector3 reference = ownPole != null ? ownPole.transform.position : Vector3.zero;
        return GetNearestActiveEnemyPole(team, reference);
    }

    public BattlePole GetNearestActiveEnemyPole(TeamId team, Vector3 position)
    {
        BattlePole best = null;
        float bestDistance = float.MaxValue;
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (teams[i] == team || IsTeamEliminated(teams[i]))
            {
                continue;
            }

            BattlePole candidate = GetNearestActivePole(teams[i], position);
            if (candidate == null)
            {
                continue;
            }

            float distance = Vector3.Distance(position, candidate.transform.position);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    public BattlePole GetAssignedActiveEnemyPole(PoleAgent agent)
    {
        if (agent == null || MaxActivePushersPerPole <= 0)
        {
            return agent == null ? null : GetNearestActiveEnemyPole(agent.Team, agent.transform.position);
        }

        List<BattlePole> candidates = new List<BattlePole>();
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (teams[i] == agent.Team || IsTeamEliminated(teams[i]))
            {
                continue;
            }

            List<BattlePole> poles = GetPoles(teams[i]);
            for (int p = 0; p < poles.Count; p++)
            {
                if (poles[p] != null && !poles[p].IsDown)
                {
                    candidates.Add(poles[p]);
                }
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        int agentIndex = Mathf.Max(0, ExtractAgentNumber(agent.AgentName) - 1);
        return candidates[agentIndex % candidates.Count];
    }

    public bool CanAgentPushPole(PoleAgent agent, BattlePole pole)
    {
        if (agent == null || pole == null || pole.IsDown || pole.Team == agent.Team || IsTeamEliminated(agent.Team))
        {
            return false;
        }

        if (MaxActivePushersPerPole <= 0)
        {
            return true;
        }

        int requesterNumber = ExtractAgentNumber(agent.AgentName);
        int eligibleBefore = 0;
        List<PoleAgent> members = GetMembers(agent.Team);
        for (int i = 0; i < members.Count; i++)
        {
            PoleAgent candidate = members[i];
            if (candidate == null
                || (candidate.CurrentAction != AgentAction.MoveToEnemyPole
                    && candidate.CurrentAction != AgentAction.SupportNearestAttacker)
                || GetAssignedActiveEnemyPole(candidate) != pole)
            {
                continue;
            }

            if (ExtractAgentNumber(candidate.AgentName) < requesterNumber)
            {
                eligibleBefore++;
            }
        }

        return eligibleBefore < MaxActivePushersPerPole;
    }

    public BattlePole GetNearestActiveOwnPole(TeamId team, Vector3 position)
    {
        return GetNearestActivePole(team, position);
    }

    public PoleAgent FindNearestEnemy(PoleAgent requester)
    {
        PoleAgent best = null;
        float bestDistance = float.MaxValue;
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (teams[i] == requester.Team || IsTeamEliminated(teams[i]))
            {
                continue;
            }

            PoleAgent candidate = FindNearest(requester.transform.position, GetMembers(teams[i]), requester);
            if (candidate == null)
            {
                continue;
            }

            float distance = Vector3.Distance(requester.transform.position, candidate.transform.position);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    public PoleAgent FindNearestAllyWithRole(PoleAgent requester, AgentRole role)
    {
        List<PoleAgent> allies = GetMembers(requester.Team);
        PoleAgent best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < allies.Count; i++)
        {
            PoleAgent candidate = allies[i];
            if (candidate == requester || candidate.Role != role)
            {
                continue;
            }

            float distance = Vector3.Distance(requester.transform.position, candidate.transform.position);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    public Vector3 ComputeSeparation(PoleAgent requester, float radius)
    {
        Vector3 push = Vector3.zero;
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            AddSeparationFromList(requester, GetMembers(teams[i]), radius, ref push);
        }
        return push;
    }

    public bool IsPositionClear(PoleAgent requester, Vector3 candidatePosition, float minDistance)
    {
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (!IsPositionClearFromList(requester, candidatePosition, GetMembers(teams[i]), minDistance))
            {
                return false;
            }
        }

        return true;
    }

    private BattlePole GetActivePole(TeamId team)
    {
        List<BattlePole> poles = GetPoles(team);
        for (int i = 0; i < poles.Count; i++)
        {
            if (poles[i] != null && !poles[i].IsDown)
            {
                return poles[i];
            }
        }

        return poles.Count > 0 ? poles[0] : null;
    }

    private BattlePole GetNearestActivePole(TeamId team, Vector3 position)
    {
        List<BattlePole> poles = GetPoles(team);
        BattlePole best = null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < poles.Count; i++)
        {
            BattlePole pole = poles[i];
            if (pole == null || pole.IsDown)
            {
                continue;
            }

            float distance = Vector3.Distance(position, pole.transform.position);
            if (distance < bestDistance)
            {
                best = pole;
                bestDistance = distance;
            }
        }

        return best != null ? best : GetActivePole(team);
    }

    private List<BattlePole> GetPoles(TeamId team)
    {
        switch (team)
        {
            case TeamId.Red:
                return redPoles;
            case TeamId.Blue:
                return bluePoles;
            case TeamId.Green:
                return greenPoles;
            case TeamId.Yellow:
                return yellowPoles;
            default:
                return redPoles;
        }
    }

    private TeamId[] GetActiveTeams()
    {
        if (RequireLastPoleStanding || greenAgents.Count > 0 || yellowAgents.Count > 0 || greenPoles.Count > 0 || yellowPoles.Count > 0)
        {
            return new TeamId[] { TeamId.Red, TeamId.Blue, TeamId.Green, TeamId.Yellow };
        }

        return new TeamId[] { TeamId.Red, TeamId.Blue };
    }

    public bool IsTeamEliminated(TeamId team)
    {
        List<BattlePole> poles = GetPoles(team);
        return poles.Count > 0 && AreAllPolesDown(team);
    }

    private TeamId? GetLastStandingTeam()
    {
        TeamId? standingTeam = null;
        int standingCount = 0;
        TeamId[] teams = GetActiveTeams();

        for (int i = 0; i < teams.Length; i++)
        {
            if (IsTeamEliminated(teams[i]))
            {
                continue;
            }

            standingTeam = teams[i];
            standingCount++;
        }

        return standingCount == 1 ? standingTeam : null;
    }

    private TeamId? ChooseWinnerByAverageStability()
    {
        TeamId? winner = null;
        float best = -1f;
        bool tied = false;
        TeamId[] teams = GetActiveTeams();

        for (int i = 0; i < teams.Length; i++)
        {
            if (IsTeamEliminated(teams[i]))
            {
                continue;
            }

            float stability = GetTeamAverageStability(teams[i]);
            if (stability > best + 0.1f)
            {
                winner = teams[i];
                best = stability;
                tied = false;
            }
            else if (Mathf.Abs(stability - best) <= 0.1f)
            {
                tied = true;
            }
        }

        return tied ? null : winner;
    }

    private bool AreAllPolesDown(TeamId team)
    {
        List<BattlePole> poles = GetPoles(team);
        if (poles.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < poles.Count; i++)
        {
            if (poles[i] != null && !poles[i].IsDown)
            {
                return false;
            }
        }

        return true;
    }

    private int CountActivePoles(TeamId team)
    {
        List<BattlePole> poles = GetPoles(team);
        int count = 0;
        for (int i = 0; i < poles.Count; i++)
        {
            if (poles[i] != null && !poles[i].IsDown)
            {
                count++;
            }
        }

        return count;
    }

    private float GetTeamAverageStability(TeamId team)
    {
        List<BattlePole> poles = GetPoles(team);
        if (poles.Count == 0)
        {
            return 0f;
        }

        float total = 0f;
        int counted = 0;
        for (int i = 0; i < poles.Count; i++)
        {
            if (poles[i] == null)
            {
                continue;
            }

            total += poles[i].Stability;
            counted++;
        }

        return counted > 0 ? total / counted : 0f;
    }

    private string GetTeamPoleSummary(TeamId team)
    {
        List<BattlePole> poles = GetPoles(team);
        return "active " + CountActivePoles(team) + "/" + poles.Count + " avg stability " + GetTeamAverageStability(team).ToString("F0");
    }

    private TeamId GetPrimaryOpponentTeam(TeamId team, Vector3 position)
    {
        TeamId[] teams = GetActiveTeams();
        TeamId fallback = team == TeamId.Red ? TeamId.Blue : TeamId.Red;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < teams.Length; i++)
        {
            if (teams[i] == team || IsTeamEliminated(teams[i]))
            {
                continue;
            }

            BattlePole pole = GetNearestActivePole(teams[i], position);
            if (pole == null)
            {
                continue;
            }

            float distance = Vector3.Distance(position, pole.transform.position);
            if (distance < bestDistance)
            {
                fallback = teams[i];
                bestDistance = distance;
            }
        }

        return fallback;
    }

    public int CountEnemiesNearOwnPole(TeamId team)
    {
        BattlePole ownPole = GetOwnPole(team);
        if (ownPole == null)
        {
            return 0;
        }

        int count = 0;
        TeamId[] teams = GetActiveTeams();
        for (int t = 0; t < teams.Length; t++)
        {
            if (teams[t] == team || IsTeamEliminated(teams[t]))
            {
                continue;
            }

            List<PoleAgent> enemies = GetMembers(teams[t]);
            for (int i = 0; i < enemies.Count; i++)
            {
                if (Vector3.Distance(enemies[i].transform.position, ownPole.transform.position) <= 5f)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public float GetRecentPressureScore(PoleAgent agent)
    {
        float value;
        return recentPressure.TryGetValue(agent, out value) ? value : 0f;
    }

    public TeamLearningState GetLearningState(TeamId team)
    {
        if (learningData == null)
        {
            learningData = new LearningSaveData();
        }

        switch (team)
        {
            case TeamId.Red:
                return learningData.Red;
            case TeamId.Blue:
                return learningData.Blue;
            case TeamId.Green:
                return learningData.Green;
            case TeamId.Yellow:
                return learningData.Yellow;
            default:
                return learningData.Red;
        }
    }

    public void RecordLeaderChoice(TeamId team, string leaderName, string reason)
    {
        if (!string.IsNullOrEmpty(GetCurrentLeader(team)))
        {
            return;
        }

        MarkCurrentLeader(team, leaderName);
        PoleAgent leaderAgent = FindAgentByName(team, leaderName);
        if (leaderAgent != null)
        {
            IncrementCounter(leaderSelectionsThisMatch, leaderAgent);
        }

        if (team == TeamId.Red)
        {
            currentRedLeader = leaderName;
        }
        else if (team == TeamId.Blue)
        {
            currentBlueLeader = leaderName;
        }
        else if (team == TeamId.Green)
        {
            currentGreenLeader = leaderName;
        }
        else if (team == TeamId.Yellow)
        {
            currentYellowLeader = leaderName;
        }

        TeamLearningState learning = GetLearningState(team);
        if (ShouldUpdateLearning())
        {
            learning.RecordLeader(leaderName);
        }

        string message = "leader=" + leaderName + "; definition=" + (isPracticeMatch ? "rotating provisional practice leader" : "fixed leader for this match") + "; reason: " + reason;
        AddLimitedRow(leaderRows, Time.time.ToString("F1") + " [" + team + "] " + message);
        if (logger != null)
        {
            logger.WriteLearningLine("[" + team + "] leader choice: " + message);
        }
    }

    public void RecordPressure(PoleAgent agent, BattlePole targetPole)
    {
        if (!recentPressure.ContainsKey(agent))
        {
            recentPressure.Add(agent, 0f);
        }

        recentPressure[agent] += Time.deltaTime * 10f;
        IncrementCounter(pushSamplesThisMatch, agent);
        AddFloat(estimatedForceSumThisMatch, agent, agent.PushForceNewtons);

        float nextTime;
        if (!nextPushLogTime.TryGetValue(agent, out nextTime) || Time.time >= nextTime)
        {
            string message = "pushed " + targetPole.Team + " pole; force=" + agent.PushForceNewtons.ToString("F0") + "N; torque=" + targetPole.LastTorque.ToString("F0") + "; pushers=" + targetPole.LastPusherCount;
            LogEvent("push", agent, "", message);
            AddLimitedRow(pushRows, Time.time.ToString("F1") + " " + agent.AgentName + " -> " + targetPole.Team + " | " + targetPole.GetForceSummary());
            nextPushLogTime[agent] = Time.time + 0.75f;
        }
    }

    public void RecordTugPull(PoleAgent agent, TugPole targetPole)
    {
        if (agent == null || targetPole == null)
        {
            return;
        }

        IncrementCounter(pushSamplesThisMatch, agent);
        AddFloat(estimatedForceSumThisMatch, agent, agent.PushForceNewtons);
    }

    private void LogForceSnapshots()
    {
        if (UseTugOfWarRules)
        {
            for (int i = 0; i < tugPoles.Count; i++)
            {
                TugPole tugPole = tugPoles[i];
                if (tugPole == null)
                {
                    continue;
                }

                string owner = tugPole.CapturedTeam.HasValue ? tugPole.CapturedTeam.Value.ToString() : "Neutral";
                AddLimitedRow(forceRows, Time.time.ToString("F1") + " Tug " + tugPole.PoleNumber
                    + ": R " + tugPole.LastRedPullers + "/" + tugPole.LastRedForce.ToString("F0") + "N"
                    + " B " + tugPole.LastBluePullers + "/" + tugPole.LastBlueForce.ToString("F0") + "N owner=" + owner);
                if (logger != null)
                {
                    logger.WriteTugSnapshot(GetLogVenueId(), matchNumber, tugPole);
                }
            }

            return;
        }

        List<BattlePole> poles = GetAllPoles();
        for (int i = 0; i < poles.Count; i++)
        {
            BattlePole pole = poles[i];
            if (pole == null)
            {
                continue;
            }

            string row = Time.time.ToString("F1") + " " + pole.name + ": " + pole.GetForceSummary();
            AddLimitedRow(forceRows, row);
            if (logger != null)
            {
                logger.WriteForceSnapshot(GetLogVenueId(), matchNumber, pole);
            }
        }
    }

    private List<BattlePole> GetAllPoles()
    {
        List<BattlePole> poles = new List<BattlePole>();
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            poles.AddRange(GetPoles(teams[i]));
        }

        return poles;
    }

    private bool AreAllTugPolesCaptured()
    {
        if (tugPoles.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < tugPoles.Count; i++)
        {
            if (tugPoles[i] != null && !tugPoles[i].IsCaptured)
            {
                return false;
            }
        }

        return true;
    }

    private int CountCapturedTugPoles(TeamId team)
    {
        int count = 0;
        for (int i = 0; i < tugPoles.Count; i++)
        {
            if (tugPoles[i] != null && tugPoles[i].CapturedTeam.HasValue && tugPoles[i].CapturedTeam.Value == team)
            {
                count++;
            }
        }

        return count;
    }

    private TeamId? ChooseTugWinner()
    {
        int redCaptured = CountCapturedTugPoles(TeamId.Red);
        int blueCaptured = CountCapturedTugPoles(TeamId.Blue);
        if (redCaptured != blueCaptured)
        {
            return redCaptured > blueCaptured ? TeamId.Red : TeamId.Blue;
        }

        float territory = 0f;
        for (int i = 0; i < tugPoles.Count; i++)
        {
            if (tugPoles[i] != null && !tugPoles[i].IsCaptured)
            {
                territory += tugPoles[i].transform.position.x;
            }
        }

        if (Mathf.Abs(territory) < 0.25f)
        {
            return null;
        }

        return territory < 0f ? TeamId.Red : TeamId.Blue;
    }

    private string BuildTugResult(TeamId? winner, string reason)
    {
        string score = "Red " + CountCapturedTugPoles(TeamId.Red) + " - Blue " + CountCapturedTugPoles(TeamId.Blue);
        return winner.HasValue
            ? winner.Value + " wins tug of war (" + score + ", " + reason + ")."
            : "Draw in tug of war (" + score + ", " + reason + ").";
    }

    private string BuildTugStatusSummary()
    {
        StringBuilder builder = new StringBuilder();
        for (int i = 0; i < tugPoles.Count; i++)
        {
            TugPole pole = tugPoles[i];
            if (pole == null)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append("pole ").Append(pole.PoleNumber)
                .Append(" x=").Append(pole.transform.position.x.ToString("F1"))
                .Append(" owner=").Append(pole.CapturedTeam.HasValue ? pole.CapturedTeam.Value.ToString() : "neutral");
        }

        return builder.ToString();
    }

    private string BuildTugAllocationSummary()
    {
        StringBuilder builder = new StringBuilder();
        TeamId[] teams = { TeamId.Red, TeamId.Blue };
        for (int t = 0; t < teams.Length; t++)
        {
            if (t > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(teams[t]).Append(" ");
            List<PoleAgent> members = GetMembers(teams[t]);
            for (int poleNumber = 1; poleNumber <= 5; poleNumber++)
            {
                int count = 0;
                for (int i = 0; i < members.Count; i++)
                {
                    int assigned;
                    if (tugPoleAssignments.TryGetValue(members[i], out assigned) && assigned == poleNumber)
                    {
                        count++;
                    }
                }

                if (poleNumber > 1)
                {
                    builder.Append(" ");
                }
                builder.Append("P").Append(poleNumber).Append(":").Append(count);
            }
        }

        return builder.ToString();
    }

    private float GetTugCapturePercent(TeamId team)
    {
        return tugPoles.Count > 0 ? CountCapturedTugPoles(team) * 100f / tugPoles.Count : 0f;
    }

    private void EnsureAgentSummaryTracking(PoleAgent agent)
    {
        EnsureCounter(leaderSelectionsThisMatch, agent);
        EnsureCounter(attackerAssignmentsThisMatch, agent);
        EnsureCounter(defenderAssignmentsThisMatch, agent);
        EnsureCounter(supporterAssignmentsThisMatch, agent);
        EnsureCounter(pushSamplesThisMatch, agent);
        if (!estimatedForceSumThisMatch.ContainsKey(agent))
        {
            estimatedForceSumThisMatch.Add(agent, 0f);
        }
    }

    private void EnsureCounter(Dictionary<PoleAgent, int> dictionary, PoleAgent agent)
    {
        if (agent != null && !dictionary.ContainsKey(agent))
        {
            dictionary.Add(agent, 0);
        }
    }

    private void IncrementCounter(Dictionary<PoleAgent, int> dictionary, PoleAgent agent)
    {
        if (agent == null)
        {
            return;
        }

        EnsureCounter(dictionary, agent);
        dictionary[agent]++;
    }

    private void AddFloat(Dictionary<PoleAgent, float> dictionary, PoleAgent agent, float value)
    {
        if (agent == null)
        {
            return;
        }

        if (!dictionary.ContainsKey(agent))
        {
            dictionary.Add(agent, 0f);
        }

        dictionary[agent] += value;
    }

    private int GetCounter(Dictionary<PoleAgent, int> dictionary, PoleAgent agent)
    {
        int value;
        return agent != null && dictionary.TryGetValue(agent, out value) ? value : 0;
    }

    private float GetFloat(Dictionary<PoleAgent, float> dictionary, PoleAgent agent)
    {
        float value;
        return agent != null && dictionary.TryGetValue(agent, out value) ? value : 0f;
    }

    private void RecordRoleAssignment(PoleAgent agent, AgentRole role)
    {
        EnsureAgentSummaryTracking(agent);
        switch (role)
        {
            case AgentRole.Attacker:
                IncrementCounter(attackerAssignmentsThisMatch, agent);
                break;
            case AgentRole.Defender:
                IncrementCounter(defenderAssignmentsThisMatch, agent);
                break;
            case AgentRole.Supporter:
                IncrementCounter(supporterAssignmentsThisMatch, agent);
                break;
        }
    }

    public void LogConversation(TeamId team, string speaker, string message)
    {
        PoleAgent agent = FindAgentByName(team, speaker);
        if (agent != null)
        {
            LogEvent("conversation", agent, "", message);
        }
        else if (logger != null)
        {
            logger.WriteEvent("conversation", team, speaker, AgentRole.Supporter, AgentAction.Idle, "", message, GetTeamAverageStability(TeamId.Red), GetTeamAverageStability(TeamId.Blue), Vector3.zero);
        }

        AddLimitedRow(conversationRows, Time.time.ToString("F1") + " [" + team + "] " + speaker + ": " + message);
        if (logger != null)
        {
            logger.WriteConversationLine("[" + team + "] " + speaker + ": " + message);
        }
        Debug.Log("[" + team + "] " + speaker + ": " + message);
    }

    private void LogDiscussionRound(TeamId team, PoleAgent agent, LLMAgentOpinion opinion, string finalLeader, string result)
    {
        if (logger == null || agent == null || opinion == null)
        {
            return;
        }

        logger.WriteDiscussionRound(
            GetLogVenueId(),
            matchNumber,
            opinion.phase,
            GetRoundNumber(opinion.phase),
            team,
            agent.AgentName,
            opinion.preferredRole,
            opinion.preferredAction,
            opinion.leaderVote,
            Mathf.Clamp01(opinion.confidence),
            finalLeader,
            result,
            opinion.message);
    }

    private void LogDiscussionDecision(TeamId team, PoleAgent agent, LLMAgentOpinion opinion, string finalLeader, string result, string message)
    {
        if (logger == null || agent == null)
        {
            return;
        }

        string vote = opinion != null ? opinion.leaderVote : "";
        float confidence = opinion != null ? Mathf.Clamp01(opinion.confidence) : 0f;
        logger.WriteDiscussionRound(
            GetLogVenueId(),
            matchNumber,
            string.IsNullOrEmpty(result) ? "final_decision" : "match_result",
            string.IsNullOrEmpty(result) ? 4 : 5,
            team,
            agent.AgentName,
            agent.Role.ToString(),
            agent.CurrentAction.ToString(),
            vote,
            confidence,
            finalLeader,
            result,
            message);
    }

    private int GetRoundNumber(string phase)
    {
        if (phase == "initial")
        {
            return 1;
        }

        if (phase == "response")
        {
            return 2;
        }

        if (phase == "final")
        {
            return 3;
        }

        if (phase == "reflection_initial" || phase == "reflection_self") return 1;
        if (phase == "reflection_revision") return 2;
        if (phase == "reflection_conclusion") return 3;

        return 0;
    }

    private void LogSystemConversation(string message)
    {
        AddLimitedRow(conversationRows, Time.time.ToString("F1") + " [System] " + message);
        if (logger != null)
        {
            logger.WriteConversationLine("[System] " + message);
        }

        Debug.Log("[System] " + message);
    }

    private void LogLLMRequest(
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
        if (logger == null)
        {
            return;
        }

        string provider = Provider == LLMProvider.OpenAI
            ? "OpenAI:" + OpenAIModel
            : "Ollama:" + LocalLLMModel;
        logger.WriteLLMRequest(
            GetLogVenueId(),
            matchNumber,
            provider,
            requestType,
            team,
            agentName,
            phase,
            attempt,
            outcome,
            durationSeconds,
            error,
            fallbackUsed);
    }

    private string ClassifyLLMFailure(UnityWebRequest request)
    {
        string error = request != null ? request.error : "";
        return !string.IsNullOrEmpty(error) && error.ToLowerInvariant().Contains("timeout")
            ? "timeout"
            : "transport_error";
    }

    public void LogRole(PoleAgent agent, string leader)
    {
        LogEvent("role", agent, leader, "role=" + agent.Role + "; action=" + agent.CurrentAction);
        if (agent.AgentName == leader)
        {
            AddLimitedRow(leaderRows, Time.time.ToString("F1") + " [" + agent.Team + "] leader: " + leader);
        }
    }

    private void HoldTeamDiscussion(TeamId team)
    {
        if (IsTeamEliminated(team))
        {
            return;
        }

        if (!UseLLMBrain || !IsLLMReady())
        {
            llmStatus = GetMissingLLMMessage();
            GetRuleBrain(team).HoldDiscussion();
            return;
        }

        if (GetLLMInFlight(team))
        {
            llmStatus = "LLM still discussing: " + team;
            return;
        }

        StartCoroutine(RequestLLMDiscussion(team, false));
    }

    private void HoldActiveTeamDiscussions()
    {
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (!IsTeamEliminated(teams[i]))
            {
                HoldTeamDiscussion(teams[i]);
            }
        }
    }

    private void IssueActiveLeaderDirectives()
    {
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            TeamId team = teams[i];
            if (IsTeamEliminated(team) || GetLLMInFlight(team) || string.IsNullOrEmpty(GetCurrentLeader(team)))
            {
                continue;
            }

            StartCoroutine(RequestLeaderDirective(team));
        }
    }

    private IEnumerator RequestLeaderDirective(TeamId team)
    {
        string leaderName = GetCurrentLeader(team);
        PoleAgent leader = FindAgentByName(team, leaderName);
        if (leader == null || !UseLLMBrain || !IsLLMReady())
        {
            yield break;
        }

        SetLLMInFlight(team, true);
        llmStatus = "Leader directive: " + leaderName + " thinking";
        UnityWebRequest request = CreateLLMRequest(BuildLeaderDirectivePrompt(team, leader));
        float requestStarted = Time.realtimeSinceStartup;
        yield return request.SendWebRequest();
        float requestDuration = Time.realtimeSinceStartup - requestStarted;

        bool applied = false;
        if (request.result == UnityWebRequest.Result.Success)
        {
            string responseText = ExtractLLMResponseText(request.downloadHandler.text);
            applied = ApplyLeaderDirective(team, leader, responseText);
            LogLLMRequest("leader_directive", team, leader.AgentName, "battle", 1, applied ? "success" : "parse_error", requestDuration, applied ? "" : "Unreadable directive JSON", false);
        }
        else
        {
            Debug.LogWarning("Leader directive failed for " + leaderName + ": " + request.error);
            LogLLMRequest("leader_directive", team, leader.AgentName, "battle", 1, ClassifyLLMFailure(request), requestDuration, request.error, false);
        }

        llmStatus = applied
            ? "Leader directive applied: " + leaderName
            : "Leader directive unavailable; current plan continues";
        SetLLMInFlight(team, false);
    }

    private string BuildLeaderDirectivePrompt(TeamId team, PoleAgent leader)
    {
        BattlePole ownPole = GetOwnPole(team);
        BattlePole enemyPole = GetEnemyPole(team);
        List<PoleAgent> members = GetMembers(team);
        StringBuilder builder = new StringBuilder();
        builder.Append("You are ").Append(leader.AgentName).Append(", the fixed leader of team ").Append(team).Append(" for this match. ");
        builder.Append("Do not elect or replace the leader. Observe the live state and issue one concise tactical directive. ");
        if (UseTugOfWarRules)
        {
            builder.Append("This is a five-pole tug-of-war without attacker, defender, or supporter roles. Allocate all team members across uncaptured poles by setting targetPole from 1 to 5 in every decision. The role and action JSON fields are compatibility placeholders and are ignored. More agents on a pole create more pulling force, but concentration leaves other poles uncontested. Captured poles are locked. Current state: ").Append(BuildTugStatusSummary()).Append(" ");
        }
        else
        {
            builder.Append("You may order a coordinated left/right attack, supporters to escort attackers, agents to interfere with opponents along their route, or a retreat to defend. ");
            builder.Append("Any role may briefly obstruct an opponent encountered along its route; obstruction is opportunistic and is not a separate role or action. ");
        }
        builder.Append("Return only JSON: {\"leader\":\"").Append(leader.AgentName).Append("\",\"message\":\"Concentrate on pole 2 and split the remainder.\",\"decisions\":[{\"agent\":\"").Append(leader.AgentName).Append("\",\"role\":\"Attacker\",\"action\":\"MoveToEnemyPole\",\"targetPole\":2,\"message\":\"Pull pole 2 toward our territory.\"}]}. ");
        builder.Append("Allowed roles: Attacker, Defender, Supporter. Allowed actions: MoveToEnemyPole, GuardOwnPole, SupportNearestAttacker, ChaseEnemy, Idle. ");
        builder.Append("Own stability ").Append(GetTeamAverageStability(team).ToString("F0")).Append(" and tilt ").Append(ownPole != null ? ownPole.TiltAngleDegrees.ToString("F1") : "-");
        builder.Append("; enemy stability ").Append(GetTeamAverageStability(GetPrimaryOpponentTeam(team, leader.transform.position)).ToString("F0")).Append(" and tilt ").Append(enemyPole != null ? enemyPole.TiltAngleDegrees.ToString("F1") : "-").Append(". ");
        builder.Append("Agents: ");
        for (int i = 0; i < members.Count; i++)
        {
            builder.Append(members[i].GetShortState()).Append("; ");
        }

        return builder.ToString();
    }

    private bool ApplyLeaderDirective(TeamId team, PoleAgent leader, string responseText)
    {
        string json = ExtractJsonObject(responseText);
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        LLMTeamPlan plan;
        try
        {
            plan = JsonUtility.FromJson<LLMTeamPlan>(json);
        }
        catch
        {
            return false;
        }

        if (plan == null || plan.decisions == null || plan.decisions.Length == 0)
        {
            return false;
        }

        string directive = string.IsNullOrEmpty(plan.message) ? "Update positions and continue the coordinated plan." : plan.message;
        LogConversation(team, leader.AgentName, "[Leader directive] " + directive);
        AddLimitedRow(leaderRows, Time.time.ToString("F1") + " [" + team + "] " + leader.AgentName + ": " + directive);

        for (int i = 0; i < plan.decisions.Length; i++)
        {
            LLMDecision raw = plan.decisions[i];
            PoleAgent agent = FindAgentByName(team, raw.agent);
            if (agent == null)
            {
                continue;
            }

            AgentRole role = UseTugOfWarRules ? agent.Role : ParseRole(raw.role, agent.Role);
            AgentAction action = UseTugOfWarRules ? agent.CurrentAction : AlignActionWithRole(role, ParseAction(raw.action, agent.CurrentAction));
            AgentDecision decision = new AgentDecision(role, action, raw.message);
            bool followed = agent.ApplyLeaderDecision(decision);
            if (!followed)
            {
                LogConversation(team, agent.AgentName, "[Leader directive declined: " + agent.Compliance + "] continuing own plan.");
                continue;
            }

            if (UseTugOfWarRules && raw.targetPole > 0)
            {
                AssignAgentToTugPole(agent, raw.targetPole);
            }
            if (!UseTugOfWarRules)
            {
                RecordRoleAssignment(agent, role);
                LogRole(agent, leader.AgentName);
            }
        }

        return true;
    }

    private void HoldActiveRuleDiscussions()
    {
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (!IsTeamEliminated(teams[i]))
            {
                GetRuleBrain(teams[i]).HoldDiscussion();
            }
        }
    }

    private void StartOpeningLLMDiscussions()
    {
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            StartCoroutine(RequestLLMDiscussion(teams[i], true));
        }
    }

    private bool GetLLMInFlight(TeamId team)
    {
        switch (team)
        {
            case TeamId.Red:
                return redLLMInFlight;
            case TeamId.Blue:
                return blueLLMInFlight;
            case TeamId.Green:
                return greenLLMInFlight;
            case TeamId.Yellow:
                return yellowLLMInFlight;
            default:
                return false;
        }
    }

    private void SetLLMInFlight(TeamId team, bool value)
    {
        switch (team)
        {
            case TeamId.Red:
                redLLMInFlight = value;
                break;
            case TeamId.Blue:
                blueLLMInFlight = value;
                break;
            case TeamId.Green:
                greenLLMInFlight = value;
                break;
            case TeamId.Yellow:
                yellowLLMInFlight = value;
                break;
        }
    }

    private void ResetOpeningPlanReady()
    {
        redOpeningPlanReady = false;
        blueOpeningPlanReady = false;
        greenOpeningPlanReady = false;
        yellowOpeningPlanReady = false;
    }

    private void SetOpeningPlanReady(TeamId team, bool value)
    {
        switch (team)
        {
            case TeamId.Red:
                redOpeningPlanReady = value;
                break;
            case TeamId.Blue:
                blueOpeningPlanReady = value;
                break;
            case TeamId.Green:
                greenOpeningPlanReady = value;
                break;
            case TeamId.Yellow:
                yellowOpeningPlanReady = value;
                break;
        }
    }

    private bool AnyLLMInFlight()
    {
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (GetLLMInFlight(teams[i]))
            {
                return true;
            }
        }

        return false;
    }

    private bool AreOpeningPlansReady()
    {
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (!GetOpeningPlanReady(teams[i]))
            {
                return false;
            }
        }

        return true;
    }

    private bool GetOpeningPlanReady(TeamId team)
    {
        switch (team)
        {
            case TeamId.Red:
                return redOpeningPlanReady;
            case TeamId.Blue:
                return blueOpeningPlanReady;
            case TeamId.Green:
                return greenOpeningPlanReady;
            case TeamId.Yellow:
                return yellowOpeningPlanReady;
            default:
                return false;
        }
    }

    private IEnumerator BeginBattleAfterOpeningDiscussion()
    {
        yield return null;

        BattleStarted = false;
        LogSystemConversation(IsVenueOneCurriculum
            ? GetCurriculumSummary()
            : isPracticeMatch
            ? "Practice match " + matchNumber + "/" + PracticeMatchCount + ": teams discuss strategies, then a rotating provisional leader directs the trial."
            : "Official match " + matchNumber + ": game is paused until leaders are elected and opening plans finish.");

        if (IsVenueOneCurriculum && !CurriculumUsesDiscussion())
        {
            ResetOpeningPlanReady();
            ApplyCurriculumFixedPlans();
            llmStatus = "Curriculum: fixed basic exercise, no discussion/leader/roles";
        }
        else
        {

            while (true)
            {
                LoadApiKey();
                ResetOpeningPlanReady();

                if (!UseLLMBrain || !IsLLMReady())
                {
                    if (RequireLLMOpeningPlan)
                    {
                        llmStatus = GetMissingLLMMessage();
                        yield return new WaitForSeconds(OpeningLLMRetrySeconds);
                        continue;
                    }

                    llmStatus = "Opening plan: rule-based fallback";
                    HoldActiveRuleDiscussions();
                    break;
                }

                llmStatus = "Opening plan: waiting for LLM";
                StartOpeningLLMDiscussions();

                while (AnyLLMInFlight())
                {
                    yield return null;
                }

                if (!RequireLLMOpeningPlan || AreOpeningPlansReady())
                {
                    break;
                }

                llmStatus = "Opening LLM failed; retrying in " + OpeningLLMRetrySeconds.ToString("F0") + " sec";
                LogSystemConversation("Opening LLM plan failed. Battle remains paused and will retry.");
                yield return new WaitForSeconds(OpeningLLMRetrySeconds);
            }
        }

        BattleStarted = true;
        battleStartTime = Time.time;
        nextLeaderInstructionTime = Time.time;
        nextForceLogTime = Time.time + ForceLogInterval;
        llmStatus = IsLLMReady()
            ? (isPracticeMatch ? "LLM: practice strategy selected via " : "LLM: leader elected and plan selected via ") + Provider
            : "LLM: off, rule battle started";
        LogSystemConversation(IsVenueOneCurriculum && !ShouldElectLeader()
            ? "Curriculum plan ready. This stage has no leader directives."
            : isPracticeMatch
            ? "Practice discussion complete. The curriculum leader will direct immediately and every " + LeaderInstructionInterval.ToString("F0") + " seconds."
            : "Opening discussion complete. Fixed leaders will now direct the battle.");
    }

    private IEnumerator RequestLLMDiscussion(TeamId team, bool openingPlan)
    {
        SetLLMInFlight(team, true);

        if (UseIndividualLLMAgents)
        {
            yield return StartCoroutine(RequestIndividualAgentDiscussion(team, openingPlan));

            SetLLMInFlight(team, false);

            yield break;
        }

        llmStatus = "LLM: requesting " + team + " via " + Provider;
        string prompt = BuildLLMPrompt(team);
        UnityWebRequest request = CreateLLMRequest(prompt);
        float requestStarted = Time.realtimeSinceStartup;
        yield return request.SendWebRequest();
        float requestDuration = Time.realtimeSinceStartup - requestStarted;

        bool appliedPlan = false;
        if (request.result != UnityWebRequest.Result.Success)
        {
            LogLLMRequest("team_plan", team, "team", openingPlan ? "opening" : "battle", 1, ClassifyLLMFailure(request), requestDuration, request.error, !openingPlan || !RequireLLMOpeningPlan);
            llmStatus = openingPlan && RequireLLMOpeningPlan ? "LLM failed before start: " + request.error : "LLM failed: rule fallback";
            Debug.LogWarning("LLM request failed: " + request.error + " " + request.downloadHandler.text);
            if (!openingPlan || !RequireLLMOpeningPlan)
            {
                GetRuleBrain(team).HoldDiscussion();
            }
        }
        else
        {
            string responseText = ExtractLLMResponseText(request.downloadHandler.text);
            appliedPlan = ApplyLLMPlan(team, responseText);
            LogLLMRequest("team_plan", team, "team", openingPlan ? "opening" : "battle", 1, appliedPlan ? "success" : "parse_error", requestDuration, appliedPlan ? "" : "Unreadable team plan JSON", !appliedPlan && (!openingPlan || !RequireLLMOpeningPlan));
            if (!appliedPlan)
            {
                llmStatus = openingPlan && RequireLLMOpeningPlan ? "LLM parse failed before start" : "LLM parse failed: rule fallback";
                Debug.LogWarning("LLM parse failed. Raw text: " + responseText);
                if (!openingPlan || !RequireLLMOpeningPlan)
                {
                    GetRuleBrain(team).HoldDiscussion();
                }
            }
            else
            {
                llmStatus = "LLM: active via " + Provider;
            }
        }

        if (openingPlan && appliedPlan)
        {
            SetOpeningPlanReady(team, true);
        }

        SetLLMInFlight(team, false);
    }

    private IEnumerator RequestIndividualAgentDiscussion(TeamId team, bool openingPlan)
    {
        List<PoleAgent> members = GetMembers(team);
        List<PoleAgent> speakers = GetDiscussionSpeakers(members);
        List<LLMAgentOpinion> initialOpinions = new List<LLMAgentOpinion>();
        List<LLMAgentOpinion> responseOpinions = new List<LLMAgentOpinion>();
        List<LLMAgentOpinion> finalOpinions = new List<LLMAgentOpinion>();

        llmStatus = "Round 1: " + team + " initial opinions";
        LogSystemConversation("[" + team + "] Round 1: " + speakers.Count + " selected agents give initial opinions.");
        for (int i = 0; i < speakers.Count; i++)
        {
            PoleAgent agent = speakers[i];
            llmStatus = "Round 1: " + agent.AgentName + " thinking";
            yield return StartCoroutine(RequestLLMAgentOpinion(team, agent, "initial", initialOpinions, new List<LLMAgentOpinion>()));

            if (IndividualAgentThinkingDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(IndividualAgentThinkingDelaySeconds);
            }
        }

        if (IsVenueOneCurriculum && !CurriculumUsesFullDiscussion())
        {
            bool appliedSimplePlan = ApplyIndividualAgentOpinions(team, initialOpinions, openingPlan);
            if (openingPlan && appliedSimplePlan)
            {
                SetOpeningPlanReady(team, true);
            }
            llmStatus = appliedSimplePlan ? "Curriculum: simple consultation complete" : "Curriculum consultation failed";
            yield break;
        }

        llmStatus = "Round 2: " + team + " responses";
        LogSystemConversation("[" + team + "] Round 2: selected agents react to teammate opinions.");
        for (int i = 0; i < speakers.Count; i++)
        {
            PoleAgent agent = speakers[i];
            llmStatus = "Round 2: " + agent.AgentName + " responding";
            yield return StartCoroutine(RequestLLMAgentOpinion(team, agent, "response", responseOpinions, initialOpinions));

            if (IndividualAgentThinkingDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(IndividualAgentThinkingDelaySeconds);
            }
        }

        List<LLMAgentOpinion> discussionContext = new List<LLMAgentOpinion>();
        discussionContext.AddRange(initialOpinions);
        discussionContext.AddRange(responseOpinions);

        llmStatus = "Round 3: " + team + (isPracticeMatch ? " final strategy" : " final vote");
        LogSystemConversation(isPracticeMatch
            ? "[" + team + "] Round 3: selected agents choose a final trial strategy without a leader."
            : "[" + team + "] Round 3: selected agents cast final leader votes and role choices.");
        for (int i = 0; i < speakers.Count; i++)
        {
            PoleAgent agent = speakers[i];
            llmStatus = "Round 3: " + agent.AgentName + " finalizing";
            yield return StartCoroutine(RequestLLMAgentOpinion(team, agent, "final", finalOpinions, discussionContext));

            if (IndividualAgentThinkingDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(IndividualAgentThinkingDelaySeconds);
            }
        }

        bool appliedPlan = ApplyIndividualAgentOpinions(team, finalOpinions, openingPlan);
        if (openingPlan && appliedPlan)
        {
            SetOpeningPlanReady(team, true);
        }

        if (!appliedPlan)
        {
            llmStatus = openingPlan && RequireLLMOpeningPlan
                ? "Individual LLM failed before start"
                : "Individual LLM failed: rule fallback";

            if (!openingPlan || !RequireLLMOpeningPlan)
            {
                GetRuleBrain(team).HoldDiscussion();
            }
        }
        else
        {
            llmStatus = "Individual LLM agents: active";
        }
    }

    private IEnumerator RunCurriculumReflection()
    {
        bool individualReflection = GetCurriculumStageIndex() < 4;
        llmStatus = individualReflection ? "Post-match self-reflection" : "Post-match team reflection: round 1";
        LogSystemConversation(individualReflection
            ? "Post-match self-reflection: every agent reviews only its own behavior."
            : "Post-match team reflection round 1: every agent gives an independent assessment.");
        TeamId[] teams = GetActiveTeams();
        bool rememberReflection = !curriculumMatchFailed
            && GetSelectedCurriculumCompletedCount() <= CurriculumLearningMatches;
        for (int t = 0; t < teams.Length; t++)
        {
            List<PoleAgent> members = GetMembers(teams[t]);
            List<LLMAgentOpinion> round1 = new List<LLMAgentOpinion>();
            List<LLMAgentOpinion> round2 = new List<LLMAgentOpinion>();
            List<LLMAgentOpinion> round3 = new List<LLMAgentOpinion>();
            for (int i = 0; i < members.Count; i++)
            {
                yield return StartCoroutine(RequestLLMAgentOpinion(teams[t], members[i],
                    individualReflection ? "reflection_self" : "reflection_initial",
                    round1, new List<LLMAgentOpinion>()));
            }

            if (!individualReflection)
            {
                llmStatus = "Team reflection: " + teams[t] + " round 2 revisions";
                LogSystemConversation("[" + teams[t] + "] Team reflection round 2: revise ideas after reading teammates.");
                for (int i = 0; i < members.Count; i++)
                {
                    yield return StartCoroutine(RequestLLMAgentOpinion(teams[t], members[i], "reflection_revision", round2, round1));
                }

                List<LLMAgentOpinion> context = new List<LLMAgentOpinion>();
                context.AddRange(round1);
                context.AddRange(round2);
                llmStatus = "Team reflection: " + teams[t] + " round 3 conclusions";
                LogSystemConversation("[" + teams[t] + "] Team reflection round 3: state a final team conclusion.");
                for (int i = 0; i < members.Count; i++)
                {
                    yield return StartCoroutine(RequestLLMAgentOpinion(teams[t], members[i], "reflection_conclusion", round3, context));
                }
            }

            if (rememberReflection)
            {
                if (individualReflection)
                {
                    for (int i = 0; i < round1.Count; i++)
                    {
                        GetLearningState(teams[t]).RecordAgentReflection(round1[i].agent, round1[i].message);
                    }
                }
                else
                {
                    StringBuilder summary = new StringBuilder();
                    for (int i = 0; i < round3.Count; i++)
                    {
                        if (i > 0) summary.Append(" | ");
                        summary.Append(round3[i].agent).Append(": ").Append(round3[i].message);
                    }
                    GetLearningState(teams[t]).RecordReflection(summary.ToString());
                }
            }
        }
        if (rememberReflection) SaveLearningData();
        llmStatus = "Post-match reflection complete";
        LogSystemConversation(rememberReflection
            ? (individualReflection
                ? "Individual self-reflections were saved to each agent's memory."
                : "Three-round team reflection completed and its conclusion was saved for the next match.")
            : "Reflection completed; evaluation memory remained frozen.");
        if (logger != null) logger.Flush();
    }

    private List<PoleAgent> GetDiscussionSpeakers(List<PoleAgent> members)
    {
        if (MaxLLMSpeakersPerTeam <= 0 || MaxLLMSpeakersPerTeam >= members.Count)
        {
            return members;
        }

        int count = Mathf.Min(MaxLLMSpeakersPerTeam, members.Count);
        int start = ((matchNumber - 1) * count) % members.Count;
        List<PoleAgent> speakers = new List<PoleAgent>(count);
        for (int i = 0; i < count; i++)
        {
            speakers.Add(members[(start + i) % members.Count]);
        }

        LogSystemConversation("[" + members[0].Team + "] Scalable council mode: " + count
            + "/" + members.Count + " rotating agents speak for this match.");
        return speakers;
    }

    private IEnumerator RequestLLMAgentOpinion(TeamId team, PoleAgent agent, string phase, List<LLMAgentOpinion> opinions, List<LLMAgentOpinion> visibleOpinions)
    {
        string basePrompt = BuildAgentLLMPrompt(team, agent, phase, visibleOpinions);
        LLMAgentOpinion opinion = null;
        for (int attempt = 0; attempt <= LLMRetryCount; attempt++)
        {
            string prompt = attempt == 0
                ? basePrompt
                : basePrompt + " IMPORTANT: Your previous response failed. Return one valid JSON object only. Do not add explanation or markdown.";
            UnityWebRequest request = CreateLLMRequest(prompt);
            float requestStarted = Time.realtimeSinceStartup;
            yield return request.SendWebRequest();
            float requestDuration = Time.realtimeSinceStartup - requestStarted;

            if (request.result != UnityWebRequest.Result.Success)
            {
                LogLLMRequest("agent_opinion", team, agent.AgentName, phase, attempt + 1, ClassifyLLMFailure(request), requestDuration, request.error, false);
                Debug.LogWarning("Individual LLM request failed for " + agent.AgentName + " attempt " + (attempt + 1) + ": " + request.error + " " + request.downloadHandler.text);
                LogSystemConversation(agent.AgentName + " LLM request failed; retry " + attempt + "/" + LLMRetryCount + ".");
                continue;
            }

            string responseText = ExtractLLMResponseText(request.downloadHandler.text);
            opinion = ParseAgentOpinion(responseText);
            if (opinion != null)
            {
                LogLLMRequest("agent_opinion", team, agent.AgentName, phase, attempt + 1, "success", requestDuration, "", false);
                break;
            }

            LogLLMRequest("agent_opinion", team, agent.AgentName, phase, attempt + 1, "parse_error", requestDuration, "Unreadable JSON", false);

            Debug.LogWarning("Individual LLM parse failed for " + agent.AgentName + " attempt " + (attempt + 1) + ". Raw text: " + responseText);
            LogSystemConversation(agent.AgentName + " returned unreadable JSON; retry " + attempt + "/" + LLMRetryCount + ".");
        }

        if (opinion == null)
        {
            if (IsVenueOneCurriculum)
            {
                curriculumMatchFailed = true;
                curriculumFailureReason = "LLM opinion failed and fallback was required";
            }
            if (!UseFallbackOpinionOnLLMFailure)
            {
                LogSystemConversation(agent.AgentName + " could not speak after retries.");
                yield break;
            }

            opinion = CreateFallbackOpinion(team, agent, phase, visibleOpinions);
            LogLLMRequest("agent_opinion", team, agent.AgentName, phase, LLMRetryCount + 2, "fallback", 0f, "LLM attempts exhausted", true);
            LogSystemConversation(agent.AgentName + " used fallback opinion after LLM failure.");
        }

        opinion.agent = agent.AgentName;
        opinion.phase = phase;
        opinions.Add(opinion);
        LogDiscussionRound(team, agent, opinion, "", "");
        LogConversation(team, agent.AgentName, "[" + GetPhaseLabel(phase) + "] " + BuildOpinionLogText(opinion));
    }

    private string BuildAgentLLMPrompt(TeamId team, PoleAgent agent, string phase, List<LLMAgentOpinion> visibleOpinions)
    {
        BattlePole ownPole = GetOwnPole(team);
        BattlePole enemyPole = GetEnemyPole(team);
        TeamId opponentTeam = GetPrimaryOpponentTeam(team, agent.transform.position);
        StringBuilder builder = new StringBuilder();
        builder.Append("You are an independent LLM agent controlling only ").Append(agent.AgentName).Append(" on team ").Append(team).Append(". ");
        builder.Append("You are not the whole team. Speak from your own viewpoint and make your own proposal. ");
        builder.Append(PersonalityDefinitions).Append(" Follow your assigned personality while still responding rationally to the current state. ");
        if (phase.StartsWith("reflection_"))
        {
            builder.Append("This is a post-match reflection in curriculum Stage ").Append(GetCurriculumStageIndex() + 1).Append(". ");
            if (phase == "reflection_self")
            {
                builder.Append("Review only your own behavior. State what you attempted, what worked or failed, the likely cause, and one adjustment for your next attempt. Do not infer a team consensus. ");
            }
            else if (phase == "reflection_initial")
            {
                builder.Append("Round 1: independently identify what worked, what failed, and one likely cause. ");
            }
            else if (phase == "reflection_revision")
            {
                builder.Append("Round 2: read other agents' assessments, then agree, disagree, or revise your diagnosis with evidence. ");
            }
            else
            {
                builder.Append("Round 3: give a concise final conclusion and one concrete lesson for the next stage. ");
            }
            builder.Append("Your memory: ").Append(GetLearningState(team).GetAgentSummary(agent.AgentName)).Append(". ");
            builder.Append("Team summary: ").Append(GetLearningState(team).GetSummary()).Append(". Opponent summary: ")
                .Append(GetLearningState(GetPrimaryOpponentTeam(team, agent.transform.position)).GetSummary()).Append(". ");
            builder.Append("Previous reflection statements: ");
            AppendOpinionSummaries(builder, visibleOpinions, 40);
            builder.Append("Return only JSON: {\"agent\":\"").Append(agent.AgentName)
                .Append("\",\"preferredRole\":\"Attacker\",\"preferredAction\":\"Idle\",\"targetPole\":1,\"leaderVote\":\"\",\"message\":\"reflection text\",\"confidence\":0.75}. ");
            builder.Append("Do not vote for a leader. confidence is 0 to 1 and means certainty in your reflection conclusion.");
            return builder.ToString();
        }
        if (isPracticeMatch)
        {
            builder.Append("This is practice match ").Append(matchNumber).Append(" of ").Append(PracticeMatchCount).Append(". ");
            if (IsVenueOneCurriculum)
            {
                builder.Append(GetCurriculumSummary()).Append(" Focus only on this stage goal while still completing the battle safely. ");
                builder.Append(GetCurriculumPhaseCode() == "T"
                    ? "This is a training match and its result updates memory. "
                    : "This is a non-learning check or evaluation match; memory is frozen. ");
                builder.Append(ShouldElectLeader()
                    ? "Elect one leader for this match and provide a leaderVote. "
                    : "This stage has no leader election; set leaderVote to an empty string. ");
                builder.Append(CurriculumUsesLLMRoles()
                    ? "Propose your role and action. "
                    : "Roles are fixed by the exercise; discuss only how to perform the stage goal. ");
            }
            else
            {
                builder.Append("A predetermined rotating provisional leader will direct the battle after discussion. Propose and compare varied strategies; do not vote, and set leaderVote to an empty string. ");
            }
        }
        else
        {
            builder.Append(LeaderDefinition).Append(" ");
        }
        builder.Append(BuildPhaseInstruction(phase));
        builder.Append("Return only JSON with this exact shape: ");
        builder.Append("{\"agent\":\"").Append(agent.AgentName).Append("\",\"preferredRole\":\"Attacker\",\"preferredAction\":\"MoveToEnemyPole\",\"targetPole\":1,\"leaderVote\":\"").Append(agent.AgentName).Append("\",\"message\":\"I think we should push from the right.\",\"confidence\":0.75}. ");
        builder.Append("confidence must be 0 to 1 and means your self-assessed certainty that your proposed strategy and, in official matches, leader vote are appropriate for the current evidence. It is not an objective win probability. ");
        if (UseTugOfWarRules)
        {
            builder.Append("This venue does not use attacker, defender, or supporter roles. preferredRole and preferredAction are compatibility placeholders and are ignored; choose targetPole and explain the allocation instead. ");
        }
        else
        {
            builder.Append("Allowed roles: Attacker, Defender, Supporter. ");
            builder.Append("Allowed actions: MoveToEnemyPole, GuardOwnPole, SupportNearestAttacker, Idle. Do not block, chase, or deliberately obstruct opposing avatars. ");
        }
        builder.Append("Your memory: ").Append(GetLearningState(team).GetAgentSummary(agent.AgentName)).Append(". ");
        builder.Append("Team memory: ").Append(GetLearningState(team).GetSummary()).Append(". ");
        builder.Append("Opponent summary: ").Append(GetLearningState(opponentTeam).GetSummary()).Append(". ");
        builder.Append("Use this compact opponent summary and the current pole state to infer whether the opponent is likely to emphasize attack, defense, or support. Do not assume access to its private full logs. ");
        builder.Append("Current self state: ").Append(agent.GetShortState()).Append(". ");
        builder.Append("Pole state: own active poles ").Append(CountActivePoles(team)).Append(", own average stability ").Append(GetTeamAverageStability(team).ToString("F0"));
        builder.Append(", current own target tilt ").Append(ownPole != null ? ownPole.TiltAngleDegrees.ToString("F1") : "-");
        builder.Append(", primary opponent ").Append(opponentTeam).Append(" active poles ").Append(CountActivePoles(opponentTeam)).Append(", opponent average stability ").Append(GetTeamAverageStability(opponentTeam).ToString("F0"));
        builder.Append(", current enemy target tilt ").Append(enemyPole != null ? enemyPole.TiltAngleDegrees.ToString("F1") : "-").Append(". ");
        if (RequireAllPolesDownToLose)
        {
            builder.Append("This venue has three poles per team. A team loses only after all three poles fall. Fallen poles stay down and should be ignored as attack or defense targets. ");
        }
        if (RequireLastPoleStanding)
        {
            builder.Append("This venue has four teams. Each team has one pole. The battle continues until only one team's pole remains standing; eliminated teams should not be chosen as targets. ");
        }
        if (VenueId == "E")
        {
            builder.Append("This venue has unequal agent abilities. Use stronger agents for high-force tasks and faster agents for movement-sensitive roles when useful. ");
        }
        if (UseTugOfWarRules)
        {
            builder.Append("This venue is a 20-vs-20 tug-of-war with five neutral poles. Set targetPole to a number from 1 to 5. More agents pulling the same pole create more combined force. Captured poles lock permanently. Discuss whether to concentrate or distribute agents. Current state: ").Append(BuildTugStatusSummary()).Append(" ");
        }
        else
        {
            builder.Append("Force-vector rule: same-side attackers create stronger torque, opposite pushes cancel, defenders should push opposite the own pole lean. ");
        }
        builder.Append("Teammate discussion so far: ");
        AppendOpinionSummaries(builder, visibleOpinions, 24);
        if (!ShouldElectLeader())
        {
            builder.Append("Do not nominate or vote for a leader. Focus on what strategy should be tested and why.");
        }
        else
        {
            builder.Append("Vote for the agent you think should lead the entire match. You may vote for yourself or another teammate.");
        }
        return builder.ToString();
    }

    private string BuildPhaseInstruction(string phase)
    {
        if (phase == "initial")
        {
            return !ShouldElectLeader()
                ? "This is Round 1. Give an initial strategy idea that differs when useful from earlier practice patterns. State the role and action you want to test. "
                : "This is Round 1. Give your initial opinion before seeing a full team consensus. State what role you want, what action you prefer, and who you initially trust as leader. ";
        }

        if (phase == "response")
        {
            return !ShouldElectLeader()
                ? "This is Round 2. Compare teammate ideas, identify likely strengths and weaknesses, then agree, disagree, or propose a modified trial strategy. "
                : "This is Round 2. Read teammate opinions, then agree, disagree, or revise your plan. Mention whose idea influenced you. You may change your leader vote. ";
        }

        return !ShouldElectLeader()
            ? "This is Round 3. Select the final strategy to test. Give a final preferred role/action and keep leaderVote empty. "
            : "This is Round 3. Make your final decision after discussion. Cast your final leader vote and final preferred role/action. ";
    }

    private string GetPhaseLabel(string phase)
    {
        if (phase == "initial")
        {
            return "Round 1 initial";
        }

        if (phase == "response")
        {
            return "Round 2 response";
        }

        if (phase == "reflection_self") return "Individual self-reflection";
        if (phase == "reflection_initial") return "Reflection 1 opinion";
        if (phase == "reflection_revision") return "Reflection 2 revision";
        if (phase == "reflection_conclusion") return "Reflection 3 conclusion";

        return isPracticeMatch ? "Round 3 practice strategy" : "Round 3 final";
    }

    private string BuildOpinionLogText(LLMAgentOpinion opinion)
    {
        string role = string.IsNullOrEmpty(opinion.preferredRole) ? "-" : opinion.preferredRole;
        string action = string.IsNullOrEmpty(opinion.preferredAction) ? "-" : opinion.preferredAction;
        string vote = string.IsNullOrEmpty(opinion.leaderVote) ? "-" : opinion.leaderVote;
        string message = string.IsNullOrEmpty(opinion.message) ? "(no message)" : opinion.message;
        return message + " role=" + role + " action=" + action + " vote=" + vote + " confidence=" + opinion.confidence.ToString("F2");
    }

    private LLMAgentOpinion CreateFallbackOpinion(TeamId team, PoleAgent agent, string phase, List<LLMAgentOpinion> visibleOpinions)
    {
        LLMAgentOpinion opinion = new LLMAgentOpinion();
        opinion.agent = agent.AgentName;
        opinion.phase = phase;

        int index = ExtractAgentNumber(agent.AgentName) - 1;
        int lane = Mathf.Abs(index) % 6;
        if (lane == 0 || lane == 1)
        {
            opinion.preferredRole = "Defender";
            opinion.preferredAction = "GuardOwnPole";
            opinion.message = "[fallback] I will defend an active own pole while the local LLM is unavailable.";
        }
        else if (lane == 2)
        {
            opinion.preferredRole = "Supporter";
            opinion.preferredAction = "SupportNearestAttacker";
            opinion.message = "[fallback] I will support the nearest attacker and keep pressure coordinated.";
        }
        else
        {
            opinion.preferredRole = "Attacker";
            opinion.preferredAction = "MoveToEnemyPole";
            opinion.message = "[fallback] I will attack the nearest active enemy pole with the main group.";
        }

        opinion.leaderVote = ShouldElectLeader() ? ChooseFallbackLeaderVote(team, visibleOpinions) : "";
        opinion.confidence = 0.35f;
        return opinion;
    }

    private string ChooseFallbackLeaderVote(TeamId team, List<LLMAgentOpinion> visibleOpinions)
    {
        if (visibleOpinions != null && visibleOpinions.Count > 0)
        {
            for (int i = visibleOpinions.Count - 1; i >= 0; i--)
            {
                if (!string.IsNullOrEmpty(visibleOpinions[i].leaderVote))
                {
                    return visibleOpinions[i].leaderVote;
                }
            }
        }

        List<PoleAgent> members = GetMembers(team);
        return members.Count > 0 ? members[0].AgentName : "";
    }

    private int ExtractAgentNumber(string agentName)
    {
        Match match = Regex.Match(agentName, "(\\d+)$");
        if (!match.Success)
        {
            return 1;
        }

        int value;
        return int.TryParse(match.Groups[1].Value, out value) ? value : 1;
    }

    private LLMAgentOpinion ParseAgentOpinion(string responseText)
    {
        string json = ExtractJsonObject(responseText);
        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonUtility.FromJson<LLMAgentOpinion>(json);
        }
        catch
        {
            return null;
        }
    }

    private bool ApplyIndividualAgentOpinions(TeamId team, List<LLMAgentOpinion> opinions, bool openingPlan)
    {
        List<PoleAgent> members = GetMembers(team);
        int requiredOpinions = openingPlan && RequireLLMOpeningPlan ? Mathf.Min(members.Count, MinimumLLMAgentOpinionsToStart) : 1;
        if (opinions.Count < requiredOpinions)
        {
            LogSystemConversation("Only " + opinions.Count + " " + team + " agents spoke; required " + requiredOpinions + ".");
            return false;
        }

        string leader = "";
        if (ShouldElectLeader())
        {
            leader = ChooseLeaderFromOpinions(team, opinions);
            RecordLeaderChoice(team, leader, "final votes, confidence, and prior leadership memory were aggregated.");
            AddLimitedRow(leaderRows, Time.time.ToString("F1") + " [" + team + "] fixed match leader: " + leader);
        }
        else if (!IsVenueOneCurriculum)
        {
            leader = GetPracticeLeaderName(team);
            RecordLeaderChoice(team, leader, "scheduled rotation gives every avatar equal practice leadership experience.");
            AddLimitedRow(leaderRows, Time.time.ToString("F1") + " [" + team + "] provisional practice leader: " + leader);
        }

        AgentRole[] roles = new AgentRole[members.Count];
        AgentAction[] actions = new AgentAction[members.Count];
        string[] messages = new string[members.Count];

        for (int i = 0; i < members.Count; i++)
        {
            PoleAgent agent = members[i];
            LLMAgentOpinion opinion = FindOpinion(opinions, agent.AgentName);
            if (opinion == null)
            {
                roles[i] = AgentRole.Attacker;
                actions[i] = AgentAction.MoveToEnemyPole;
                messages[i] = "I could not speak this round, so I will follow the team attack plan.";
                continue;
            }

            roles[i] = ParseRole(opinion.preferredRole, AgentRole.Attacker);
            actions[i] = ParseAction(opinion.preferredAction, AgentAction.MoveToEnemyPole);
            messages[i] = string.IsNullOrEmpty(opinion.message) ? "I propose my own role and leader vote." : opinion.message;
        }

        if (IsVenueOneCurriculum && !CurriculumUsesLLMRoles())
        {
            int stage = GetCurriculumStageIndex() + 1;
            for (int i = 0; i < members.Count; i++)
            {
                if (stage == 6)
                {
                    roles[i] = AgentRole.Defender;
                    actions[i] = AgentAction.GuardOwnPole;
                }
                else if (stage == 7 && i % 2 == 1)
                {
                    roles[i] = AgentRole.Supporter;
                    actions[i] = AgentAction.SupportNearestAttacker;
                }
                else
                {
                    roles[i] = AgentRole.Attacker;
                    actions[i] = AgentAction.MoveToEnemyPole;
                }
            }
        }
        else if (!UseTugOfWarRules)
        {
            BalanceIndividualRoles(roles, actions);
        }

        for (int i = 0; i < members.Count; i++)
        {
            PoleAgent agent = members[i];
            LLMAgentOpinion opinion = FindOpinion(opinions, agent.AgentName);
            string vote = ShouldElectLeader() && opinion != null && !string.IsNullOrEmpty(opinion.leaderVote) ? opinion.leaderVote : leader;
            string message = isPracticeMatch
                ? messages[i] + (ShouldElectLeader() ? " Curriculum leader: " + leader + "." : " Curriculum stage without a leader.")
                : messages[i] + " Vote leader: " + vote + ".";
            actions[i] = UseTugOfWarRules ? agent.CurrentAction : AlignActionWithRole(roles[i], actions[i]);
            roles[i] = UseTugOfWarRules ? agent.Role : roles[i];
            if (UseTugOfWarRules && opinion != null && opinion.targetPole > 0)
            {
                AssignAgentToTugPole(agent, opinion.targetPole);
            }
            if (!UseTugOfWarRules)
            {
                RecordRoleAssignment(agent, roles[i]);
            }
            agent.ApplyDecision(new AgentDecision(roles[i], actions[i], message));
            LogConversation(team, agent.AgentName, message);
            if (!UseTugOfWarRules)
            {
                LogRole(agent, leader);
            }
            LogDiscussionDecision(team, agent, opinion, leader, "", message);
        }

        return true;
    }

    private string ChooseLeaderFromOpinions(TeamId team, List<LLMAgentOpinion> opinions)
    {
        List<PoleAgent> members = GetMembers(team);
        string bestLeader = members.Count > 0 ? members[0].AgentName : "";
        float bestScore = float.MinValue;

        for (int i = 0; i < members.Count; i++)
        {
            string candidate = members[i].AgentName;
            float score = GetLearningState(team).GetLeaderScore(candidate) * 0.25f;

            for (int j = 0; j < opinions.Count; j++)
            {
                if (SameAgentName(opinions[j].leaderVote, candidate))
                {
                    score += 1f + Mathf.Clamp01(opinions[j].confidence);
                }
            }

            if (score > bestScore)
            {
                bestScore = score;
                bestLeader = candidate;
            }
        }

        return bestLeader;
    }

    private LLMAgentOpinion FindOpinion(List<LLMAgentOpinion> opinions, string agentName)
    {
        for (int i = 0; i < opinions.Count; i++)
        {
            if (SameAgentName(opinions[i].agent, agentName))
            {
                return opinions[i];
            }
        }

        return null;
    }

    private bool SameAgentName(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
        {
            return false;
        }

        return a.Trim().ToLowerInvariant() == b.Trim().ToLowerInvariant();
    }

    private void BalanceIndividualRoles(AgentRole[] roles, AgentAction[] actions)
    {
        int attackers = CountRole(roles, AgentRole.Attacker);
        int defenders = CountRole(roles, AgentRole.Defender);
        int supporters = CountRole(roles, AgentRole.Supporter);
        int minimumDefenders = Mathf.Max(1, roles.Length / 6);
        int minimumSupporters = Mathf.Max(1, roles.Length / 6);
        int maximumAttackers = Mathf.Max(4, Mathf.CeilToInt(roles.Length * 0.67f));

        for (int i = 0; i < roles.Length && defenders < minimumDefenders; i++)
        {
            if (roles[i] != AgentRole.Defender)
            {
                if (roles[i] == AgentRole.Attacker)
                {
                    attackers--;
                }
                else if (roles[i] == AgentRole.Supporter)
                {
                    supporters--;
                }

                roles[i] = AgentRole.Defender;
                actions[i] = AgentAction.GuardOwnPole;
                defenders++;
            }
        }

        for (int i = 0; i < roles.Length && supporters < minimumSupporters; i++)
        {
            if (roles[i] != AgentRole.Supporter && (roles[i] == AgentRole.Attacker || defenders > minimumDefenders))
            {
                if (roles[i] == AgentRole.Attacker)
                {
                    attackers--;
                }
                else if (roles[i] == AgentRole.Defender)
                {
                    defenders--;
                }

                roles[i] = AgentRole.Supporter;
                actions[i] = AgentAction.SupportNearestAttacker;
                supporters++;
            }
        }

        for (int i = 0; i < roles.Length && attackers > maximumAttackers; i++)
        {
            if (roles[i] == AgentRole.Attacker)
            {
                roles[i] = defenders <= supporters ? AgentRole.Defender : AgentRole.Supporter;
                actions[i] = AlignActionWithRole(roles[i], actions[i]);
                attackers--;
                if (roles[i] == AgentRole.Defender)
                {
                    defenders++;
                }
                else
                {
                    supporters++;
                }
            }
        }

    }

    private int CountRole(AgentRole[] roles, AgentRole role)
    {
        int count = 0;
        for (int i = 0; i < roles.Length; i++)
        {
            if (roles[i] == role)
            {
                count++;
            }
        }

        return count;
    }

    private void LoadApiKey()
    {
        if (string.IsNullOrEmpty(OpenAIApiKey))
        {
            OpenAIApiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");
        }

        if (string.IsNullOrEmpty(OpenAIApiKey))
        {
            string keyPath = Path.Combine(Application.dataPath, OpenAIKeyFileName);
            if (File.Exists(keyPath))
            {
                OpenAIApiKey = File.ReadAllText(keyPath).Trim();
            }
        }
    }

    private void LoadRunLimit()
    {
        configuredMatchLimit = 0;
        string runCountPath = Path.Combine(Application.dataPath, RunCountFileName);
        if (!File.Exists(runCountPath))
        {
            File.WriteAllText(runCountPath, "0");
            return;
        }

        string text = File.ReadAllText(runCountPath).Trim();
        int value;
        if (int.TryParse(text, out value))
        {
            configuredMatchLimit = Mathf.Max(0, value);
        }
    }

    private void LoadInstructionCompliance()
    {
        if (sessionComplianceLoaded)
        {
            return;
        }

        sessionComplianceLoaded = true;
        sessionCompliance.Clear();
        teamSessionCompliance.Clear();
        string path = Path.Combine(Application.dataPath, InstructionComplianceFileName);
        if (!File.Exists(path))
        {
            return;
        }

        string[] lines = File.ReadAllLines(path);
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("#"))
            {
                continue;
            }

            string[] parts = line.Split('=');
            int agentNumber;
            InstructionCompliance compliance;
            if (parts.Length != 2 || !System.Enum.TryParse(parts[1].Trim(), true, out compliance))
            {
                continue;
            }

            string key = parts[0].Trim();
            if (int.TryParse(key, out agentNumber))
            {
                sessionCompliance[Mathf.Max(1, agentNumber)] = compliance;
            }
            else if (Regex.IsMatch(key, "^(Red|Blue|Green|Yellow)-\\d+$", RegexOptions.IgnoreCase))
            {
                teamSessionCompliance[key] = compliance;
            }
        }
    }

    private InstructionCompliance GetInstructionCompliance(string agentName)
    {
        InstructionCompliance compliance;
        if (teamSessionCompliance.TryGetValue(agentName, out compliance))
        {
            return compliance;
        }
        int agentNumber = ExtractAgentNumber(agentName);
        return sessionCompliance.TryGetValue(agentNumber, out compliance)
            ? compliance
            : InstructionCompliance.Full;
    }

    private string BuildLLMPrompt(TeamId team)
    {
        BattlePole ownPole = GetOwnPole(team);
        BattlePole enemyPole = GetEnemyPole(team);
        TeamId opponentTeam = GetPrimaryOpponentTeam(team, ownPole != null ? ownPole.transform.position : Vector3.zero);
        List<PoleAgent> members = GetMembers(team);
        StringBuilder builder = new StringBuilder();
        builder.Append("You are the tactical discussion system for team ").Append(team).Append(" in a pole-toppling simulation. ");
        builder.Append(PersonalityDefinitions).Append(" ");
        if (isPracticeMatch)
        {
            builder.Append("This is a practice match. Do not choose a leader; compare strategies and assign trial roles/actions. ");
        }
        else
        {
            builder.Append(LeaderDefinition).Append(" Choose one leader and assign every agent a role and action. ");
        }
        builder.Append("There are ").Append(members.Count).Append(" agents per team. Keep roles clear and proportional: about half attackers, one third defenders, and the rest supporters in balanced situations; use more defenders if your pole is threatened, and more attackers if the enemy pole is close to falling. ");
        builder.Append("Do not assign everyone to attack. Always keep at least one defender and one supporter unless the battle is already finished. ");
        builder.Append("Role meanings: Attacker pushes the enemy pole from a coordinated side; Defender guards your own pole and applies counter-force opposite the pole lean; Supporter follows attackers and coordinates group pressure. ");
        builder.Append("Think in force vectors, not only headcount: pushes from opposite sides can cancel, coordinated same-side pushes increase torque, and defenders can reduce tilt by pushing against the lean direction. ");
        builder.Append("Return only JSON with this exact shape: ");
        builder.Append("{\"leader\":\"Red-1\",\"decisions\":[{\"agent\":\"Red-1\",\"role\":\"Attacker\",\"action\":\"MoveToEnemyPole\",\"message\":\"I will push from the right.\"}]}. ");
        builder.Append("Allowed roles: Attacker, Defender, Supporter. ");
        builder.Append("Allowed actions: MoveToEnemyPole, GuardOwnPole, SupportNearestAttacker, Idle. Do not block, chase, or deliberately obstruct opposing avatars. ");
        builder.Append("State: own active poles ").Append(CountActivePoles(team)).Append(", own average stability ").Append(GetTeamAverageStability(team).ToString("F0"));
        builder.Append(", current own target tilt ").Append(ownPole != null ? ownPole.TiltAngleDegrees.ToString("F1") : "-");
        builder.Append(", primary opponent ").Append(opponentTeam).Append(" active poles ").Append(CountActivePoles(opponentTeam)).Append(", opponent average stability ").Append(GetTeamAverageStability(opponentTeam).ToString("F0"));
        builder.Append(", current enemy target tilt ").Append(enemyPole != null ? enemyPole.TiltAngleDegrees.ToString("F1") : "-").Append(". ");
        if (RequireAllPolesDownToLose)
        {
            builder.Append("This venue has three poles per team. A team loses only after all three poles fall. Fallen poles stay down and should be ignored as attack or defense targets. ");
        }
        if (RequireLastPoleStanding)
        {
            builder.Append("This venue has four teams. Each team has one pole. The battle continues until only one team's pole remains standing; eliminated teams should not be chosen as targets. ");
        }
        if (VenueId == "E")
        {
            builder.Append("This venue has unequal agent abilities. Use stronger agents for high-force tasks and faster agents for movement-sensitive roles when useful. ");
        }
        if (UseTugOfWarRules)
        {
            builder.Append("This is a 20-vs-20 tug-of-war with five neutral poles. Include targetPole 1-5 in each decision and allocate all agents. More agents produce more pulling force and captured poles lock. Current state: ").Append(BuildTugStatusSummary()).Append(" ");
        }
        builder.Append("Team memory: ").Append(GetLearningState(team).GetSummary()).Append(". ");
        builder.Append("Agents: ");
        for (int i = 0; i < members.Count; i++)
        {
            builder.Append(members[i].GetShortState()).Append("; ");
        }

        builder.Append("Recent conversation: ");
        AppendRecentRows(builder, conversationRows, 8);
        return builder.ToString();
    }

    private bool ApplyLLMPlan(TeamId team, string jsonText)
    {
        string json = ExtractJsonObject(jsonText);
        if (string.IsNullOrEmpty(json))
        {
            return false;
        }

        LLMTeamPlan plan;
        try
        {
            plan = JsonUtility.FromJson<LLMTeamPlan>(json);
        }
        catch
        {
            return false;
        }

        if (plan == null || plan.decisions == null || plan.decisions.Length == 0)
        {
            return false;
        }

        string leader = isPracticeMatch ? GetPracticeLeaderName(team) : (string.IsNullOrEmpty(plan.leader) ? plan.decisions[0].agent : plan.leader);
        if (!isPracticeMatch)
        {
            RecordLeaderChoice(team, leader, "LLM selected this agent after the opening discussion.");
            LogConversation(team, leader, "LLM leader proposal accepted for the full match.");
            AddLimitedRow(leaderRows, Time.time.ToString("F1") + " [" + team + "] fixed LLM leader: " + leader);
        }
        else
        {
            RecordLeaderChoice(team, leader, "scheduled rotation gives every avatar equal practice leadership experience.");
            AddLimitedRow(leaderRows, Time.time.ToString("F1") + " [" + team + "] provisional practice leader: " + leader);
        }

        int attackerCount = 0;
        int defenderCount = 0;
        int supporterCount = 0;

        for (int i = 0; i < plan.decisions.Length; i++)
        {
            LLMDecision raw = plan.decisions[i];
            PoleAgent agent = FindAgentByName(team, raw.agent);
            if (agent == null)
            {
                continue;
            }

            AgentRole role = UseTugOfWarRules ? agent.Role : ParseRole(raw.role, AgentRole.Attacker);
            if (!UseTugOfWarRules)
            {
                role = KeepMinimumRoleDiversity(role, i, ref attackerCount, ref defenderCount, ref supporterCount);
            }
            AgentAction action = UseTugOfWarRules ? agent.CurrentAction : ParseAction(raw.action, AgentAction.MoveToEnemyPole);
            if (!UseTugOfWarRules)
            {
                action = AlignActionWithRole(role, action);
            }
            if (UseTugOfWarRules && raw.targetPole > 0)
            {
                AssignAgentToTugPole(agent, raw.targetPole);
            }
            if (!UseTugOfWarRules)
            {
                RecordRoleAssignment(agent, role);
            }
            string message = string.IsNullOrEmpty(raw.message) ? "I will execute my assigned action." : raw.message;
            agent.ApplyDecision(new AgentDecision(role, action, message));
            LogConversation(team, agent.AgentName, message);
            if (!UseTugOfWarRules)
            {
                LogRole(agent, leader);
            }
        }

        return true;
    }

    private AgentRole KeepMinimumRoleDiversity(AgentRole requestedRole, int decisionIndex, ref int attackers, ref int defenders, ref int supporters)
    {
        AgentRole role = requestedRole;

        if (decisionIndex == 0 && role == AgentRole.Attacker)
        {
            role = AgentRole.Defender;
        }
        else if (decisionIndex == 1 && role == AgentRole.Attacker)
        {
            role = AgentRole.Supporter;
        }
        else if (role == AgentRole.Attacker && attackers >= 4)
        {
            role = defenders <= supporters ? AgentRole.Defender : AgentRole.Supporter;
        }

        switch (role)
        {
            case AgentRole.Defender:
                defenders++;
                break;
            case AgentRole.Supporter:
                supporters++;
                break;
            default:
                attackers++;
                break;
        }

        return role;
    }

    private AgentAction AlignActionWithRole(AgentRole role, AgentAction action)
    {
        if (role == AgentRole.Defender)
        {
            return AgentAction.GuardOwnPole;
        }

        if (action == AgentAction.ChaseEnemy)
        {
            return role == AgentRole.Supporter
                ? AgentAction.SupportNearestAttacker
                : AgentAction.MoveToEnemyPole;
        }

        if (role == AgentRole.Supporter && action == AgentAction.MoveToEnemyPole)
        {
            return AgentAction.SupportNearestAttacker;
        }

        return action;
    }

    private TeamBrain GetRuleBrain(TeamId team)
    {
        switch (team)
        {
            case TeamId.Red:
                return redBrain;
            case TeamId.Blue:
                return blueBrain;
            case TeamId.Green:
                return greenBrain;
            case TeamId.Yellow:
                return yellowBrain;
            default:
                return redBrain;
        }
    }

    private void MarkCurrentLeader(TeamId team, string leaderName)
    {
        List<PoleAgent> members = GetMembers(team);
        for (int i = 0; i < members.Count; i++)
        {
            members[i].SetLeaderVisual(members[i].AgentName == leaderName);
        }
    }

    private List<PoleAgent> GetMembers(TeamId team)
    {
        switch (team)
        {
            case TeamId.Red:
                return redAgents;
            case TeamId.Blue:
                return blueAgents;
            case TeamId.Green:
                return greenAgents;
            case TeamId.Yellow:
                return yellowAgents;
            default:
                return redAgents;
        }
    }

    private string GetRoleCountSummary(TeamId team)
    {
        List<PoleAgent> members = GetMembers(team);
        int attackers = 0;
        int defenders = 0;
        int supporters = 0;

        for (int i = 0; i < members.Count; i++)
        {
            switch (members[i].Role)
            {
                case AgentRole.Attacker:
                    attackers++;
                    break;
                case AgentRole.Defender:
                    defenders++;
                    break;
                case AgentRole.Supporter:
                    supporters++;
                    break;
            }
        }

        return "A:" + attackers + " D:" + defenders + " S:" + supporters;
    }

    private AgentRole ParseRole(string value, AgentRole fallback)
    {
        AgentRole parsed;
        return System.Enum.TryParse(value, true, out parsed) ? parsed : fallback;
    }

    private AgentAction ParseAction(string value, AgentAction fallback)
    {
        AgentAction parsed;
        return System.Enum.TryParse(value, true, out parsed) ? parsed : fallback;
    }

    private UnityWebRequest CreateLLMRequest(string prompt)
    {
        string body;
        UnityWebRequest request;
        if (Provider == LLMProvider.OllamaLocal)
        {
            body = "{\"model\":\"" + EscapeJson(LocalLLMModel) + "\",\"prompt\":\"" + EscapeJson(prompt) + "\",\"stream\":false,\"format\":\"json\",\"options\":{\"num_predict\":" + MaxLLMOutputTokens + ",\"temperature\":0.7}}";
            request = new UnityWebRequest(OllamaGenerateUrl, "POST");
        }
        else
        {
            body = "{\"model\":\"" + EscapeJson(OpenAIModel) + "\",\"input\":\"" + EscapeJson(prompt) + "\",\"max_output_tokens\":" + MaxLLMOutputTokens + "}";
            request = new UnityWebRequest("https://api.openai.com/v1/responses", "POST");
            request.SetRequestHeader("Authorization", "Bearer " + OpenAIApiKey);
        }

        byte[] payload = Encoding.UTF8.GetBytes(body);
        request.uploadHandler = new UploadHandlerRaw(payload);
        request.downloadHandler = new DownloadHandlerBuffer();
        request.timeout = Mathf.CeilToInt(LLMRequestTimeoutSeconds);
        request.SetRequestHeader("Content-Type", "application/json");
        return request;
    }

    private bool IsLLMReady()
    {
        if (!UseLLMBrain)
        {
            return false;
        }

        if (Provider == LLMProvider.OllamaLocal)
        {
            return !string.IsNullOrEmpty(LocalLLMModel) && !string.IsNullOrEmpty(OllamaGenerateUrl);
        }

        return !string.IsNullOrEmpty(OpenAIApiKey);
    }

    private string GetMissingLLMMessage()
    {
        if (!UseLLMBrain)
        {
            return "LLM required before start: UseLLMBrain is off";
        }

        if (Provider == LLMProvider.OllamaLocal)
        {
            return "Local LLM required: start Ollama and pull " + LocalLLMModel;
        }

        return "LLM required before start: set Assets/OpenAIKey.txt";
    }

    private string ExtractLLMResponseText(string responseJson)
    {
        if (Provider == LLMProvider.OllamaLocal)
        {
            Match response = Regex.Match(responseJson, "\"response\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
            if (response.Success)
            {
                return UnescapeJson(response.Groups[1].Value);
            }
        }

        return ExtractResponseText(responseJson);
    }

    private string ExtractResponseText(string responseJson)
    {
        Match outputText = Regex.Match(responseJson, "\"output_text\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
        if (outputText.Success)
        {
            return UnescapeJson(outputText.Groups[1].Value);
        }

        Match text = Regex.Match(responseJson, "\"text\"\\s*:\\s*\"((?:\\\\.|[^\"])*)\"");
        if (text.Success)
        {
            return UnescapeJson(text.Groups[1].Value);
        }

        return responseJson;
    }

    private string ExtractJsonObject(string text)
    {
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return "";
        }

        return text.Substring(start, end - start + 1);
    }

    private string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }

        return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }

    private string UnescapeJson(string value)
    {
        return value.Replace("\\n", "\n").Replace("\\\"", "\"").Replace("\\\\", "\\");
    }

    private void AddLimitedRow(List<string> rows, string row)
    {
        rows.Add(row);
        while (rows.Count > 36)
        {
            rows.RemoveAt(0);
        }
    }

    private void AppendRecentRows(StringBuilder builder, List<string> rows, int maxRows)
    {
        int start = Mathf.Max(0, rows.Count - maxRows);
        for (int i = start; i < rows.Count; i++)
        {
            builder.Append(rows[i]).Append(" | ");
        }
    }

    private void AppendOpinionSummaries(StringBuilder builder, List<LLMAgentOpinion> opinions, int maxOpinions)
    {
        if (opinions == null || opinions.Count == 0)
        {
            builder.Append("none yet. ");
            return;
        }

        int start = Mathf.Max(0, opinions.Count - maxOpinions);
        for (int i = start; i < opinions.Count; i++)
        {
            LLMAgentOpinion opinion = opinions[i];
            builder.Append("[")
                .Append(GetPhaseLabel(opinion.phase))
                .Append("] ")
                .Append(opinion.agent)
                .Append(": role=")
                .Append(string.IsNullOrEmpty(opinion.preferredRole) ? "-" : opinion.preferredRole)
                .Append(", action=")
                .Append(string.IsNullOrEmpty(opinion.preferredAction) ? "-" : opinion.preferredAction)
                .Append(", vote=")
                .Append(string.IsNullOrEmpty(opinion.leaderVote) ? "-" : opinion.leaderVote)
                .Append(", message=")
                .Append(string.IsNullOrEmpty(opinion.message) ? "(none)" : opinion.message)
                .Append(" | ");
        }
    }

    private void LogEvent(string eventName, PoleAgent agent, string leader, string message)
    {
        if (logger == null || agent == null)
        {
            return;
        }

        logger.WriteEvent(
            eventName,
            agent.Team,
            agent.AgentName,
            agent.Role,
            agent.CurrentAction,
            leader,
            message,
            GetTeamAverageStability(TeamId.Red),
            GetTeamAverageStability(TeamId.Blue),
            agent.transform.position);
    }

    private PoleAgent FindAgentByName(TeamId team, string name)
    {
        List<PoleAgent> agents = GetMembers(team);
        for (int i = 0; i < agents.Count; i++)
        {
            if (agents[i].AgentName == name)
            {
                return agents[i];
            }
        }

        return null;
    }

    private PoleAgent FindNearest(Vector3 position, List<PoleAgent> agents, PoleAgent exclude)
    {
        PoleAgent best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < agents.Count; i++)
        {
            PoleAgent candidate = agents[i];
            if (candidate == exclude)
            {
                continue;
            }

            float distance = Vector3.Distance(position, candidate.transform.position);
            if (distance < bestDistance)
            {
                best = candidate;
                bestDistance = distance;
            }
        }

        return best;
    }

    private void AddSeparationFromList(PoleAgent requester, List<PoleAgent> agents, float radius, ref Vector3 push)
    {
        for (int i = 0; i < agents.Count; i++)
        {
            PoleAgent other = agents[i];
            if (other == requester)
            {
                continue;
            }

            Vector3 delta = requester.transform.position - other.transform.position;
            delta.y = 0f;
            float distance = delta.magnitude;
            if (distance > 0.01f && distance < radius)
            {
                float strength = (radius - distance) / radius;
                push += delta.normalized * strength * strength * 2.0f;
            }
        }
    }

    private bool IsPositionClearFromList(PoleAgent requester, Vector3 candidatePosition, List<PoleAgent> agents, float minDistance)
    {
        for (int i = 0; i < agents.Count; i++)
        {
            PoleAgent other = agents[i];
            if (other == requester)
            {
                continue;
            }

            Vector3 delta = candidatePosition - other.transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < minDistance * minDistance)
            {
                return false;
            }
        }

        return true;
    }

    private void DecayPressureScores()
    {
        List<PoleAgent> keys = new List<PoleAgent>(recentPressure.Keys);
        for (int i = 0; i < keys.Count; i++)
        {
            recentPressure[keys[i]] *= 0.35f;
        }
    }

    private void LoadLearningData()
    {
        string folder = Path.Combine(Application.persistentDataPath, "PoleBattleLogs");
        Directory.CreateDirectory(folder);
        string suffix = VenueId == "A" ? "" : "_" + VenueId;
        learningSavePath = Path.Combine(folder, "team_learning" + suffix + ".json");

        if (File.Exists(learningSavePath))
        {
            try
            {
                learningData = JsonUtility.FromJson<LearningSaveData>(File.ReadAllText(learningSavePath));
            }
            catch
            {
                learningData = new LearningSaveData();
            }
        }
        else
        {
            learningData = new LearningSaveData();
        }

        if (learningData == null)
        {
            learningData = new LearningSaveData();
        }
    }

    private void SaveLearningData()
    {
        if (learningData == null || string.IsNullOrEmpty(learningSavePath))
        {
            return;
        }

        File.WriteAllText(learningSavePath, JsonUtility.ToJson(learningData, true));
    }

    private void UpdateLearningAfterBattle(TeamId? winner)
    {
        if (!ShouldUpdateLearning())
        {
            if (logger != null)
            {
                logger.WriteLearningLine("Memory frozen for " + GetCurriculumSummary());
            }
            return;
        }

        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            bool won = winner.HasValue && winner.Value == teams[i];
            if (isPracticeMatch)
            {
                GetLearningState(teams[i]).RecordPracticeResult(won);
            }
            else
            {
                GetLearningState(teams[i]).RecordMatchResult(won);
            }
            if (!IsVenueOneCurriculum || GetCurriculumStageIndex() >= 4)
            {
                RecordAgentRoleLearning(teams[i], won);
            }
        }

        SaveLearningData();

        if (logger != null)
        {
            for (int i = 0; i < teams.Length; i++)
            {
                logger.WriteLearningLine("[" + teams[i] + "] " + GetLearningState(teams[i]).GetSummary());
            }
        }
    }

    private void RecordAgentRoleLearning(TeamId team, bool won)
    {
        TeamLearningState learning = GetLearningState(team);
        List<PoleAgent> members = GetMembers(team);
        for (int i = 0; i < members.Count; i++)
        {
            learning.RecordAgentRole(members[i].AgentName, members[i].Role, won);
        }
    }

    private void WriteSummaryCsvs(TeamId? winner, string result)
    {
        if (logger == null)
        {
            return;
        }

        string winnerTextForCsv = winner.HasValue ? winner.Value.ToString() : "Draw";
        float duration = Mathf.Max(0f, Time.time - battleStartTime);
        logger.WriteMatchSummary(
            GetLogVenueId(),
            matchNumber,
            experimentSeed,
            winnerTextForCsv,
            result,
            duration,
            UseTugOfWarRules ? CountCapturedTugPoles(TeamId.Red) : CountActivePoles(TeamId.Red),
            UseTugOfWarRules ? CountCapturedTugPoles(TeamId.Blue) : CountActivePoles(TeamId.Blue),
            CountActivePoles(TeamId.Green),
            CountActivePoles(TeamId.Yellow),
            UseTugOfWarRules ? GetTugCapturePercent(TeamId.Red) : GetTeamAverageStability(TeamId.Red),
            UseTugOfWarRules ? GetTugCapturePercent(TeamId.Blue) : GetTeamAverageStability(TeamId.Blue),
            GetTeamAverageStability(TeamId.Green),
            GetTeamAverageStability(TeamId.Yellow),
            GetRoleCountSummary(TeamId.Red),
            GetRoleCountSummary(TeamId.Blue),
            GetRoleCountSummary(TeamId.Green),
            GetRoleCountSummary(TeamId.Yellow),
            currentRedLeader,
            currentBlueLeader,
            currentGreenLeader,
            currentYellowLeader);

        TeamId[] teams = GetActiveTeams();
        for (int t = 0; t < teams.Length; t++)
        {
            bool won = winner.HasValue && winner.Value == teams[t];
            List<PoleAgent> members = GetMembers(teams[t]);
            for (int i = 0; i < members.Count; i++)
            {
                PoleAgent agent = members[i];
                int pushSamples = GetCounter(pushSamplesThisMatch, agent);
                float estimatedForceSum = GetFloat(estimatedForceSumThisMatch, agent);
                float avgForce = pushSamples > 0 ? estimatedForceSum / pushSamples : 0f;
                logger.WriteAgentSummary(
                    GetLogVenueId(),
                    matchNumber,
                    teams[t],
                    agent.AgentName,
                    GetLearningState(teams[t]).GetPersonality(agent.AgentName),
                    agent.Compliance.ToString(),
                    agent.AbilityNote,
                    won,
                    agent.Role,
                    agent.CurrentAction,
                    GetCounter(leaderSelectionsThisMatch, agent),
                    agent.LeaderDirectivesReceived,
                    agent.LeaderDirectivesFollowed,
                    agent.LeaderDirectivesDeclined,
                    GetCounter(attackerAssignmentsThisMatch, agent),
                    GetCounter(defenderAssignmentsThisMatch, agent),
                    GetCounter(supporterAssignmentsThisMatch, agent),
                    pushSamples,
                    estimatedForceSum,
                    avgForce);
            }
        }
    }

    private void FinishBattle(string result, TeamId? winner)
    {
        if (BattleFinished)
        {
            return;
        }

        BattleFinished = true;
        finishedBattleDurationSeconds = Mathf.Max(0f, Time.time - battleStartTime);
        winnerText = result;
        UpdateLearningAfterBattle(winner);
        WriteSummaryCsvs(winner, result);
        RecordCurriculumProgress(winner);
        if (curriculumMatchFailed && logger != null)
        {
            logger.WriteLearningLine("Curriculum failed match excluded from saved count: " + curriculumFailureReason);
        }

        TeamId[] teams = GetActiveTeams();
        for (int t = 0; t < teams.Length; t++)
        {
            LogMatchResultToDiscussionCsv(teams[t], result);
        }

        globalMatchNumber++;

        for (int t = 0; t < teams.Length; t++)
        {
            List<PoleAgent> members = GetMembers(teams[t]);
            for (int i = 0; i < members.Count; i++)
            {
                members[i].ApplyDecision(new AgentDecision(members[i].Role, AgentAction.Celebrate, result));
                LogEvent("finish", members[i], "", result);
            }
        }

        if (logger != null)
        {
            logger.Flush();
        }

        Debug.Log(result);

        LoadRunLimit();
        if (IsVenueOneCurriculum && !curriculumReflectionStarted)
        {
            curriculumReflectionStarted = true;
            StartCoroutine(ReflectThenScheduleNextMatch());
        }
        else if (!IsVenueOneCurriculum)
        {
            ScheduleNextMatch();
        }
    }

    private IEnumerator ReflectThenScheduleNextMatch()
    {
        yield return StartCoroutine(RunCurriculumReflection());
        ScheduleNextMatch();
    }

    private void ScheduleNextMatch()
    {
        if (AutoRestartBattles && ShouldRestartAfterThisMatch())
        {
            StartCoroutine(RestartBattleAfterDelay());
        }
        else if (!isPracticeMatch && configuredMatchLimit > 0 && matchNumber >= configuredMatchLimit)
        {
            llmStatus = "Run limit reached: " + configuredMatchLimit + " matches complete";
            LogSystemConversation("Run limit reached. Auto restart stopped after " + configuredMatchLimit + " matches.");
        }
    }

    private IEnumerator RestartBattleAfterDelay()
    {
        yield return new WaitForSeconds(RestartDelaySeconds);
        PoleBattleBootstrap.RestartBattle();
    }

    private bool ShouldRestartAfterThisMatch()
    {
        if (stopAfterCurrentMatch)
        {
            return false;
        }
        if (IsVenueOneCurriculum)
        {
            return GetSelectedCurriculumCompletedCount() < CurriculumTargetMatches;
        }
        return isPracticeMatch || configuredMatchLimit <= 0 || matchNumber < configuredMatchLimit;
    }

    private string GetLogVenueId()
    {
        if (IsVenueOneCurriculum)
        {
            return VenueId + "_S" + (GetCurriculumStageIndex() + 1).ToString("00") + "_" + GetCurriculumPhaseCode();
        }
        return isPracticeMatch ? VenueId + "_PRACTICE" : VenueId;
    }

    private void LogMatchResultToDiscussionCsv(TeamId team, string result)
    {
        List<PoleAgent> members = GetMembers(team);
        string leader = GetCurrentLeader(team);
        for (int i = 0; i < members.Count; i++)
        {
            LogDiscussionDecision(team, members[i], null, leader, result, "Match " + matchNumber + " result: " + result);
        }
    }

    private string GetCurrentLeader(TeamId team)
    {
        switch (team)
        {
            case TeamId.Red:
                return currentRedLeader;
            case TeamId.Blue:
                return currentBlueLeader;
            case TeamId.Green:
                return currentGreenLeader;
            case TeamId.Yellow:
                return currentYellowLeader;
            default:
                return "";
        }
    }

    private string GetPracticeLeaderName(TeamId team)
    {
        List<PoleAgent> members = GetMembers(team);
        if (members.Count == 0)
        {
            return "";
        }

        int index = (Mathf.Max(1, matchNumber) - 1) % members.Count;
        return members[index].AgentName;
    }

    private void OnGUI()
    {
        GUI.Box(new Rect(12, 12, 760, 306), "");
        GUI.Label(new Rect(24, 24, 420, 24), "AI Pole Battle Prototype - " + (isPracticeMatch ? "Practice " : "Official Match ") + matchNumber);
        GUI.Label(new Rect(24, 48, 720, 24), "Venue " + VenueId + ": " + VenueName + "   [1] [2] [3] [4] [5]");
        if (UseTugOfWarRules)
        {
            GUI.Label(new Rect(24, 72, 700, 24), "Tug poles: Red captured " + CountCapturedTugPoles(TeamId.Red) + " / Blue captured " + CountCapturedTugPoles(TeamId.Blue) + " / Neutral " + (tugPoles.Count - CountCapturedTugPoles(TeamId.Red) - CountCapturedTugPoles(TeamId.Blue)));
            GUI.Label(new Rect(24, 96, 700, 24), BuildTugStatusSummary());
        }
        else
        {
            GUI.Label(new Rect(24, 72, 580, 24), "Red poles: " + GetTeamPoleSummary(TeamId.Red) + " tilt: " + (RedPole != null ? RedPole.TiltAngleDegrees.ToString("F1") : "-"));
            GUI.Label(new Rect(24, 96, 580, 24), "Blue poles: " + GetTeamPoleSummary(TeamId.Blue) + " tilt: " + (BluePole != null ? BluePole.TiltAngleDegrees.ToString("F1") : "-"));
        }
        if (RequireLastPoleStanding)
        {
            GUI.Label(new Rect(24, 120, 580, 24), "Green poles: " + GetTeamPoleSummary(TeamId.Green) + " tilt: " + GetTeamTiltSummary(TeamId.Green));
            GUI.Label(new Rect(24, 144, 580, 24), "Yellow poles: " + GetTeamPoleSummary(TeamId.Yellow) + " tilt: " + GetTeamTiltSummary(TeamId.Yellow));
        }

        int statusY = RequireLastPoleStanding ? 168 : 120;
        string progressText = !BattleStarted
            ? (isPracticeMatch ? "Waiting for practice strategy discussion" : "Waiting for leader election")
            : "Next leader directive in " + Mathf.Max(0f, nextLeaderInstructionTime - Time.time).ToString("F1") + " sec";
        GUI.Label(new Rect(24, statusY, 350, 24), BattleFinished ? winnerText : progressText);
        GUI.Label(new Rect(390, statusY, 350, 24), GetBattleTimeSummary());
        string providerText = Provider == LLMProvider.OpenAI ? "OpenAI " + OpenAIModel : "Ollama " + LocalLLMModel;
        GUI.Label(new Rect(24, statusY + 24, 700, 24), llmStatus + " (" + providerText + ")");
        GUI.Label(new Rect(24, statusY + 48, 720, 24), GetRunLimitSummary());
        GUI.Label(new Rect(24, statusY + 72, 720, 24), UseTugOfWarRules
            ? "Pole allocation: " + BuildTugAllocationSummary()
            : "Role counts: " + BuildRoleCountSummary());
        GUI.Label(new Rect(24, statusY + 96, 720, 24), "Learning: " + BuildLearningSummary());

        if (logger != null)
        {
            GUI.Label(new Rect(24, statusY + 120, 720, 24), "CSV: " + ShortPath(logger.LogPath) + " | " + ShortPath(logger.DiscussionPath));
            GUI.Label(new Rect(24, statusY + 142, 720, 24), "Force/Summary: " + ShortPath(logger.ForcePath) + " | " + ShortPath(logger.MatchSummaryPath) + " | " + ShortPath(logger.AgentSummaryPath));
        }

        if (VenueId == "E")
        {
            GUI.Label(new Rect(24, statusY + 164, 720, 24), BuildAbilitySummary());
        }

        if (VenueId == "A" && curriculumControl != null)
        {
            DrawCurriculumSelector();
        }

        DrawTable(new Rect(Screen.width - 430, 12, 410, 250), "AI conversation", conversationRows);
        DrawTable(new Rect(Screen.width - 430, 272, 410, 170), "Force by pole", forceRows);
        DrawTable(new Rect(Screen.width - 430, 452, 410, 130), "Push log", pushRows);
        DrawTable(new Rect(Screen.width - 430, 592, 410, 130), "Leaders", leaderRows);
        if (VenueId == "A" && curriculumControl != null && showCurriculumProgress)
        {
            DrawCurriculumProgressOverview();
        }
    }

    private void DrawCurriculumSelector()
    {
        float y = Mathf.Max(330f, Screen.height - 116f);
        Rect panel = new Rect(12f, y, 760f, 104f);
        GUI.Box(panel, "Curriculum selection (Venue 1)");
        string pendingName = pendingCurriculumMode <= CurriculumGoals.Length
            ? "Stage " + pendingCurriculumMode.ToString("00") + ": " + CurriculumGoals[pendingCurriculumMode - 1]
            : "Match mode: full official battle";
        GUI.Label(new Rect(24f, y + 22f, 500f, 22f), pendingName);
        if (GUI.Button(new Rect(530f, y + 20f, 42f, 26f), "<"))
        {
            pendingCurriculumMode = pendingCurriculumMode <= 1 ? CurriculumGoals.Length + 1 : pendingCurriculumMode - 1;
        }
        if (GUI.Button(new Rect(578f, y + 20f, 42f, 26f), ">"))
        {
            pendingCurriculumMode = pendingCurriculumMode >= CurriculumGoals.Length + 1 ? 1 : pendingCurriculumMode + 1;
        }
        if (GUI.Button(new Rect(626f, y + 20f, 132f, 26f), "Start / Apply"))
        {
            curriculumControl.SelectedMode = Mathf.Clamp(pendingCurriculumMode, 1, CurriculumGoals.Length + 1);
            SaveCurriculumControl();
            curriculumSelectionConfirmed = true;
            Time.timeScale = 1f;
            ResetMatchCounter();
            PoleBattleBootstrap.RestartBattle();
        }
        string active = curriculumControl.SelectedMode <= CurriculumGoals.Length
            ? "Active S" + curriculumControl.SelectedMode.ToString("00") + " saved " + GetSelectedCurriculumCompletedCount() + "/" + CurriculumTargetMatches
            : "Active MATCH mode";
        int selectedIndex = Mathf.Clamp(curriculumControl.SelectedMode - 1, 0, CurriculumGoals.Length - 1);
        int success = curriculumControl.SelectedMode <= CurriculumGoals.Length ? curriculumControl.StageSuccessCounts[selectedIndex] : 0;
        int failed = curriculumControl.SelectedMode <= CurriculumGoals.Length ? curriculumControl.StageFailedCounts[selectedIndex] : 0;
        GUI.Label(new Rect(24f, y + 46f, 600f, 20f), active + " | success " + success + " | failed " + failed + " | " + Path.GetFileName(curriculumControlPath));

        string pauseLabel = Time.timeScale > 0f ? "Pause" : "Resume";
        if (GUI.Button(new Rect(24f, y + 70f, 90f, 26f), pauseLabel))
        {
            Time.timeScale = Time.timeScale > 0f ? 0f : 1f;
        }
        if (GUI.Button(new Rect(120f, y + 70f, 160f, 26f), stopAfterCurrentMatch ? "Stop requested" : "Stop after current"))
        {
            stopAfterCurrentMatch = !stopAfterCurrentMatch;
        }
        if (GUI.Button(new Rect(286f, y + 70f, 150f, 26f), resetStageConfirmation ? "Confirm reset" : "Reset selected stage"))
        {
            if (resetStageConfirmation)
            {
                ResetSelectedCurriculumStage();
                resetStageConfirmation = false;
            }
            else
            {
                resetStageConfirmation = true;
            }
        }
        if (GUI.Button(new Rect(442f, y + 70f, 120f, 26f), showCurriculumProgress ? "Hide Progress" : "Show Progress"))
        {
            showCurriculumProgress = !showCurriculumProgress;
        }
        GUI.Label(new Rect(568f, y + 72f, 185f, 22f), stopAfterCurrentMatch ? "Stops after this match." : "Continuous run.");
    }

    private void DrawCurriculumProgressOverview()
    {
        curriculumControl.EnsureValid(CurriculumGoals.Length);
        float width = Mathf.Min(860f, Screen.width - 40f);
        float height = 224f;
        float x = Mathf.Max(12f, (Screen.width - width) * 0.5f);
        float y = Mathf.Max(86f, Screen.height - 350f);
        GUI.Box(new Rect(x, y, width, height), "Curriculum progress overview");
        GUI.Label(new Rect(x + 12f, y + 24f, width - 24f, 20f), "Stage   Saved/1000   Train%   Eval%   Failed   Avg sec");
        float columnWidth = (width - 30f) * 0.5f;
        for (int stage = 0; stage < CurriculumGoals.Length; stage++)
        {
            int column = stage / 7;
            int row = stage % 7;
            int saved = curriculumControl.StageTrainingCounts[stage];
            int success = curriculumControl.StageSuccessCounts[stage];
            int evaluationCount = curriculumControl.StageEvaluationCounts[stage];
            int evaluationSuccess = curriculumControl.StageEvaluationSuccessCounts[stage];
            int failed = curriculumControl.StageFailedCounts[stage];
            int trainingCount = Mathf.Min(saved, CurriculumLearningMatches);
            float rate = trainingCount > 0 ? success * 100f / trainingCount : 0f;
            float evaluationRate = evaluationCount > 0 ? evaluationSuccess * 100f / evaluationCount : 0f;
            float average = saved > 0 ? curriculumControl.StageTotalDuration[stage] / saved : 0f;
            string marker = curriculumControl.SelectedMode == stage + 1 ? ">" : " ";
            string line = marker + " S" + (stage + 1).ToString("00")
                + "   " + saved.ToString().PadLeft(4) + "/1000"
                + "   " + (trainingCount > 0 ? rate.ToString("F1") + "%" : "-").PadLeft(7)
                + "   " + (evaluationCount > 0 ? evaluationRate.ToString("F1") + "%" : "-").PadLeft(7)
                + "   " + failed.ToString().PadLeft(4)
                + "   " + (saved > 0 ? average.ToString("F1") : "-").PadLeft(7);
            GUI.Label(new Rect(x + 12f + column * columnWidth, y + 48f + row * 23f, columnWidth - 8f, 22f), line);
        }
        GUI.Label(new Rect(x + 12f, y + height - 24f, width - 24f, 20f), "T001-T900 update memory; E001-E100 freeze memory. Failed matches are excluded from Saved.");
    }

    private void ResetSelectedCurriculumStage()
    {
        if (curriculumControl == null || pendingCurriculumMode < 1 || pendingCurriculumMode > CurriculumGoals.Length)
        {
            return;
        }
        curriculumControl.EnsureValid(CurriculumGoals.Length);
        int index = pendingCurriculumMode - 1;
        curriculumControl.StageTrainingCounts[index] = 0;
        curriculumControl.StageSuccessCounts[index] = 0;
        curriculumControl.StageFailedCounts[index] = 0;
        curriculumControl.StageTotalDuration[index] = 0f;
        curriculumControl.StageEvaluationCounts[index] = 0;
        curriculumControl.StageEvaluationSuccessCounts[index] = 0;
        curriculumControl.CurrentBlockValidCounts[index] = 0;
        curriculumControl.CurrentBlockSuccessCounts[index] = 0;
        curriculumControl.CurrentBlockFailedCounts[index] = 0;
        curriculumControl.CurrentBlockDuration[index] = 0f;
        SaveCurriculumControl();
    }

    private string GetTeamTiltSummary(TeamId team)
    {
        BattlePole pole = GetActivePole(team);
        return pole != null ? pole.TiltAngleDegrees.ToString("F1") : "-";
    }

    private string BuildRoleCountSummary()
    {
        StringBuilder builder = new StringBuilder();
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(teams[i]).Append(" ").Append(GetRoleCountSummary(teams[i]));
        }

        return builder.ToString();
    }

    private string BuildLearningSummary()
    {
        StringBuilder builder = new StringBuilder();
        TeamId[] teams = GetActiveTeams();
        for (int i = 0; i < teams.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(" | ");
            }

            builder.Append(teams[i]).Append(" ").Append(GetLearningState(teams[i]).GetSummary());
        }

        return builder.ToString();
    }

    private string BuildAbilitySummary()
    {
        return "Abilities: 1 power x2 | 2 power x0.5 | 3 speed x2 | 4 speed x0.5 | 5-6 normal";
    }

    private string GetBattleTimeSummary()
    {
        if (!BattleStarted)
        {
            return "Battle time: waiting";
        }

        if (BattleFinished)
        {
            return "Battle time: " + finishedBattleDurationSeconds.ToString("F1") + " sec";
        }

        return "Battle time: " + Mathf.Max(0f, Time.time - battleStartTime).ToString("F1") + " sec";
    }

    private string GetRunLimitSummary()
    {
        if (isPracticeMatch)
        {
            if (IsVenueOneCurriculum)
            {
                return GetCurriculumSummary();
            }
            return "Practice: " + matchNumber + " / " + PracticeMatchCount + " (rotating provisional leader)";
        }

        if (configuredMatchLimit <= 0)
        {
            return "Run limit: unlimited (Assets/RunCount.txt = 0)";
        }

        return "Official matches: " + matchNumber + " / " + configuredMatchLimit + " after " + PracticeMatchCount + " practice matches";
    }

    private string ShortPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return "";
        }

        return Path.GetFileName(path);
    }

    private void DrawTable(Rect rect, string title, List<string> rows)
    {
        GUI.Box(rect, "");
        GUI.Label(new Rect(rect.x + 10, rect.y + 8, rect.width - 20, 22), title);

        float y = rect.y + 32;
        int maxVisibleRows = Mathf.Max(1, Mathf.FloorToInt((rect.height - 40f) / 18f));
        int start = Mathf.Max(0, rows.Count - maxVisibleRows);
        for (int i = start; i < rows.Count; i++)
        {
            GUI.Label(new Rect(rect.x + 10, y, rect.width - 20, 18), rows[i]);
            y += 18;
        }
    }
}

[System.Serializable]
public class CurriculumControlData
{
    public int SelectedMode = 1;
    public int[] StageTrainingCounts = new int[14];
    public int[] StageSuccessCounts = new int[14];
    public int[] StageFailedCounts = new int[14];
    public float[] StageTotalDuration = new float[14];
    public int[] StageEvaluationCounts = new int[14];
    public int[] StageEvaluationSuccessCounts = new int[14];
    public int[] CurrentBlockValidCounts = new int[14];
    public int[] CurrentBlockSuccessCounts = new int[14];
    public int[] CurrentBlockFailedCounts = new int[14];
    public float[] CurrentBlockDuration = new float[14];
    public string LastProvider = "";
    public string LastModel = "";
    public int LastSeed;
    public string LastCompliance = "";
    public string LastPersonalities = "";
    public string LastUpdatedUtc = "";

    public void EnsureValid(int stageCount)
    {
        SelectedMode = Mathf.Clamp(SelectedMode, 1, stageCount + 1);
        StageTrainingCounts = ResizeIntArray(StageTrainingCounts, stageCount);
        StageSuccessCounts = ResizeIntArray(StageSuccessCounts, stageCount);
        StageFailedCounts = ResizeIntArray(StageFailedCounts, stageCount);
        StageTotalDuration = ResizeFloatArray(StageTotalDuration, stageCount);
        StageEvaluationCounts = ResizeIntArray(StageEvaluationCounts, stageCount);
        StageEvaluationSuccessCounts = ResizeIntArray(StageEvaluationSuccessCounts, stageCount);
        CurrentBlockValidCounts = ResizeIntArray(CurrentBlockValidCounts, stageCount);
        CurrentBlockSuccessCounts = ResizeIntArray(CurrentBlockSuccessCounts, stageCount);
        CurrentBlockFailedCounts = ResizeIntArray(CurrentBlockFailedCounts, stageCount);
        CurrentBlockDuration = ResizeFloatArray(CurrentBlockDuration, stageCount);
    }

    private static int[] ResizeIntArray(int[] source, int count)
    {
        if (source != null && source.Length == count) return source;
        int[] replacement = new int[count];
        if (source != null)
        {
            int copyCount = Mathf.Min(source.Length, replacement.Length);
            for (int i = 0; i < copyCount; i++)
            {
                replacement[i] = source[i];
            }
        }
        return replacement;
    }

    private static float[] ResizeFloatArray(float[] source, int count)
    {
        if (source != null && source.Length == count) return source;
        float[] replacement = new float[count];
        if (source != null)
        {
            int copyCount = Mathf.Min(source.Length, replacement.Length);
            for (int i = 0; i < copyCount; i++) replacement[i] = source[i];
        }
        return replacement;
    }
}

[System.Serializable]
public class LLMTeamPlan
{
    public string leader;
    public string message;
    public LLMDecision[] decisions;
}

[System.Serializable]
public class LLMDecision
{
    public string agent;
    public string role;
    public string action;
    public int targetPole;
    public string message;
}

[System.Serializable]
public class LLMAgentOpinion
{
    public string agent;
    public string phase;
    public string preferredRole;
    public string preferredAction;
    public int targetPole;
    public string leaderVote;
    public string message;
    public float confidence;
}
