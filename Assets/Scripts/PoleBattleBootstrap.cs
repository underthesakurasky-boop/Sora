using UnityEngine;

public class PoleBattleBootstrap : MonoBehaviour
{
    private const string RuntimeRootName = "Pole Battle Runtime";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void EnableBackgroundExecution()
    {
        Application.runInBackground = true;
    }

    public enum ExperimentVenue
    {
        Initial6v6,
        Large12v12,
        Huge30v30ThreePoles,
        FourTeam12v12v12v12,
        AbilityVariant6v6
    }

    public static ExperimentVenue SelectedVenue = ExperimentVenue.Initial6v6;

    public int AgentsPerTeam = 6;
    public Vector2 FieldSize = new Vector2(32f, 20f);
    public string VenueId = "A";
    public string VenueName = "Initial 6v6";

    private Transform runtimeRoot;
    private PoleBattleGameManager gameManager;
    private Material redMaterial;
    private Material blueMaterial;
    private Material groundMaterial;
    private Material poleRedMaterial;
    private Material poleBlueMaterial;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AutoCreateBootstrap()
    {
        if (FindFirstObjectByType<PoleBattleBootstrap>() != null || FindFirstObjectByType<PoleBattleGameManager>() != null)
        {
            return;
        }

        GameObject bootstrap = new GameObject("Pole Battle Bootstrap");
        bootstrap.AddComponent<PoleBattleBootstrap>();
    }

    private void Update()
    {
        if (WasVenueKeyPressed(1))
        {
            SwitchVenue(ExperimentVenue.Initial6v6);
        }
        else if (WasVenueKeyPressed(2))
        {
            SwitchVenue(ExperimentVenue.Large12v12);
        }
        else if (WasVenueKeyPressed(3))
        {
            SwitchVenue(ExperimentVenue.Huge30v30ThreePoles);
        }
        else if (WasVenueKeyPressed(4))
        {
            SwitchVenue(ExperimentVenue.FourTeam12v12v12v12);
        }
        else if (WasVenueKeyPressed(5))
        {
            SwitchVenue(ExperimentVenue.AbilityVariant6v6);
        }
    }

    private bool WasVenueKeyPressed(int venueNumber)
    {
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null)
        {
            if (venueNumber == 1 && UnityEngine.InputSystem.Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                return true;
            }

            if (venueNumber == 2 && UnityEngine.InputSystem.Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                return true;
            }

            if (venueNumber == 3 && UnityEngine.InputSystem.Keyboard.current.digit3Key.wasPressedThisFrame)
            {
                return true;
            }

            if (venueNumber == 4 && UnityEngine.InputSystem.Keyboard.current.digit4Key.wasPressedThisFrame)
            {
                return true;
            }

            if (venueNumber == 5 && UnityEngine.InputSystem.Keyboard.current.digit5Key.wasPressedThisFrame)
            {
                return true;
            }
        }
#endif

#if ENABLE_LEGACY_INPUT_MANAGER
        if (venueNumber == 1 && Input.GetKeyDown(KeyCode.Alpha1))
        {
            return true;
        }

        if (venueNumber == 2 && Input.GetKeyDown(KeyCode.Alpha2))
        {
            return true;
        }

        if (venueNumber == 3 && Input.GetKeyDown(KeyCode.Alpha3))
        {
            return true;
        }

        if (venueNumber == 4 && Input.GetKeyDown(KeyCode.Alpha4))
        {
            return true;
        }

        if (venueNumber == 5 && Input.GetKeyDown(KeyCode.Alpha5))
        {
            return true;
        }
#endif

        return false;
    }

    public static void RestartBattle()
    {
        PoleBattleBootstrap[] bootstraps = FindObjectsByType<PoleBattleBootstrap>(FindObjectsSortMode.None);
        for (int i = 0; i < bootstraps.Length; i++)
        {
            Destroy(bootstraps[i].gameObject);
        }

        DestroyRuntimeRoots();

        GameObject bootstrap = new GameObject("Pole Battle Bootstrap");
        bootstrap.AddComponent<PoleBattleBootstrap>();
    }

    public static void SwitchVenue(ExperimentVenue venue)
    {
        if (SelectedVenue == venue)
        {
            return;
        }

        SelectedVenue = venue;
        PoleBattleGameManager.ResetMatchCounter();
        RestartBattle();
    }

    private void Start()
    {
        ApplyVenueSettings();
        DestroyRuntimeRoots();
        runtimeRoot = new GameObject(RuntimeRootName).transform;

        BuildMaterials();
        CreateManagers();
        CreateEnvironment();
        CreateCameraAndLight();

        if (SelectedVenue == ExperimentVenue.Huge30v30ThreePoles)
        {
            CreateTugOfWarVenue();
            return;
        }

        if (SelectedVenue == ExperimentVenue.FourTeam12v12v12v12)
        {
            CreateFourTeamVenue();
            return;
        }

        float poleX = FieldSize.x * 0.38f;
        float agentStartX = FieldSize.x * 0.27f;
        BattlePole redPole = null;
        BattlePole bluePole = null;
        redPole = CreatePole("Red Pole", TeamId.Red, new Vector3(-poleX, 2.5f, 0f), poleRedMaterial);
        bluePole = CreatePole("Blue Pole", TeamId.Blue, new Vector3(poleX, 2.5f, 0f), poleBlueMaterial);

        CreateAgents(TeamId.Red, redPole, bluePole, redMaterial, -agentStartX);
        CreateAgents(TeamId.Blue, bluePole, redPole, blueMaterial, agentStartX);
    }

    private void ApplyVenueSettings()
    {
        if (SelectedVenue == ExperimentVenue.Large12v12)
        {
            AgentsPerTeam = 12;
            FieldSize = new Vector2(56f, 34f);
            VenueId = "B";
            VenueName = "Large 12v12 individual LLM";
            return;
        }

        if (SelectedVenue == ExperimentVenue.Huge30v30ThreePoles)
        {
            AgentsPerTeam = 20;
            FieldSize = new Vector2(72f, 44f);
            VenueId = "C";
            VenueName = "20v20 five-pole tug of war";
            return;
        }

        if (SelectedVenue == ExperimentVenue.FourTeam12v12v12v12)
        {
            AgentsPerTeam = 12;
            FieldSize = new Vector2(68f, 68f);
            VenueId = "D";
            VenueName = "Four-team 12 each last pole standing";
            return;
        }

        if (SelectedVenue == ExperimentVenue.AbilityVariant6v6)
        {
            AgentsPerTeam = 6;
            FieldSize = new Vector2(40f, 26f);
            VenueId = "E";
            VenueName = "Ability variant 6v6 individual LLM";
            return;
        }

        AgentsPerTeam = 6;
        FieldSize = new Vector2(40f, 26f);
        VenueId = "A";
        VenueName = "Initial 6v6 individual LLM";
    }

    private void BuildMaterials()
    {
        redMaterial = MakeMaterial(new Color(0.9f, 0.12f, 0.1f));
        blueMaterial = MakeMaterial(new Color(0.1f, 0.32f, 0.95f));
        groundMaterial = MakeMaterial(new Color(0.24f, 0.32f, 0.24f));
        poleRedMaterial = MakeMaterial(new Color(0.95f, 0.2f, 0.16f));
        poleBlueMaterial = MakeMaterial(new Color(0.14f, 0.42f, 1f));
    }

    private Material GetTeamMaterial(TeamId team, bool pole)
    {
        switch (team)
        {
            case TeamId.Red:
                return pole ? poleRedMaterial : redMaterial;
            case TeamId.Blue:
                return pole ? poleBlueMaterial : blueMaterial;
            case TeamId.Green:
                return MakeMaterial(pole ? new Color(0.16f, 0.9f, 0.35f) : new Color(0.08f, 0.68f, 0.25f));
            case TeamId.Yellow:
                return MakeMaterial(pole ? new Color(1f, 0.82f, 0.1f) : new Color(0.95f, 0.68f, 0.08f));
            default:
                return MakeMaterial(Color.white);
        }
    }

    private Material MakeMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.color = color;
        return material;
    }

    private static void DestroyRuntimeRoots()
    {
        GameObject[] allObjects = FindObjectsByType<GameObject>(FindObjectsSortMode.None);
        for (int i = 0; i < allObjects.Length; i++)
        {
            if (allObjects[i].name == RuntimeRootName)
            {
                Destroy(allObjects[i]);
            }
        }
    }

    private void ParentToRuntime(GameObject target)
    {
        if (runtimeRoot != null)
        {
            target.transform.SetParent(runtimeRoot, true);
        }
    }

    private void CreateManagers()
    {
        GameObject managerObject = new GameObject("Pole Battle Game Manager");
        ParentToRuntime(managerObject);
        gameManager = managerObject.AddComponent<PoleBattleGameManager>();
        gameManager.VenueId = VenueId;
        gameManager.VenueName = VenueName;
        gameManager.ConfigureSchedule(SelectedVenue == ExperimentVenue.Huge30v30ThreePoles ? 20 : 12, 10f);
        gameManager.ConfigureFieldBounds(FieldSize);
        gameManager.MinimumLLMAgentOpinionsToStart = AgentsPerTeam;
        gameManager.UseTugOfWarRules = SelectedVenue == ExperimentVenue.Huge30v30ThreePoles;
        gameManager.RequireAllPolesDownToLose = false;
        gameManager.Provider = PoleBattleGameManager.LLMProvider.OllamaLocal;
        gameManager.RequireLastPoleStanding = SelectedVenue == ExperimentVenue.FourTeam12v12v12v12;
        if (gameManager.Provider == PoleBattleGameManager.LLMProvider.OllamaLocal)
        {
            gameManager.LLMRequestTimeoutSeconds = 120f;
            gameManager.MaxLLMOutputTokens = 360;
            gameManager.LLMRetryCount = 2;
        }

        if (SelectedVenue == ExperimentVenue.Huge30v30ThreePoles)
        {
            gameManager.MaxLLMSpeakersPerTeam = 10;
            gameManager.MinimumLLMAgentOpinionsToStart = 10;
            gameManager.IndividualAgentThinkingDelaySeconds = 0f;
            gameManager.DiscussionInterval = 9999f;
            gameManager.MaxLLMOutputTokens = 220;
            gameManager.LLMRetryCount = 1;
            gameManager.LLMRequestTimeoutSeconds = 75f;
            gameManager.MaxActivePushersPerPole = 0;
            gameManager.MaxBattleSeconds = 180f;
        }
    }

    private void CreateTugOfWarVenue()
    {
        float homeX = FieldSize.x * 0.42f;
        float captureX = FieldSize.x * 0.34f;
        gameManager.ConfigureTugOfWar(new Vector3(-homeX, 1.2f, 0f), new Vector3(homeX, 1.2f, 0f));

        float[] lanes = { -12f, -6f, 0f, 6f, 12f };
        for (int i = 0; i < lanes.Length; i++)
        {
            GameObject poleObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            poleObject.name = "Tug Pole " + (i + 1);
            poleObject.transform.position = new Vector3(0f, 1.15f, lanes[i]);
            poleObject.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            poleObject.transform.localScale = new Vector3(0.55f, 2.4f, 0.55f);
            ParentToRuntime(poleObject);

            TugPole pole = poleObject.AddComponent<TugPole>();
            pole.PoleNumber = i + 1;
            pole.RedHome = new Vector3(-homeX, 1.15f, lanes[i]);
            pole.BlueHome = new Vector3(homeX, 1.15f, lanes[i]);
            pole.CaptureBoundaryX = captureX;
            gameManager.RegisterTugPole(pole);
        }

        CreateCaptureZone("Red Capture Zone", new Vector3(-homeX, 0.08f, 0f), new Color(0.65f, 0.08f, 0.06f));
        CreateCaptureZone("Blue Capture Zone", new Vector3(homeX, 0.08f, 0f), new Color(0.05f, 0.18f, 0.65f));
        CreateAgents(TeamId.Red, null, null, redMaterial, -FieldSize.x * 0.27f);
        CreateAgents(TeamId.Blue, null, null, blueMaterial, FieldSize.x * 0.27f);
    }

    private void CreateCaptureZone(string name, Vector3 position, Color color)
    {
        GameObject zone = GameObject.CreatePrimitive(PrimitiveType.Cube);
        zone.name = name;
        zone.transform.position = position;
        zone.transform.localScale = new Vector3(3.5f, 0.15f, FieldSize.y * 0.9f);
        zone.GetComponent<Renderer>().material = MakeMaterial(color);
        ParentToRuntime(zone);
    }

    private void CreateFourTeamVenue()
    {
        float poleOffset = FieldSize.x * 0.31f;
        BattlePole redPole = CreatePole("Red Pole", TeamId.Red, new Vector3(-poleOffset, 2.5f, 0f), GetTeamMaterial(TeamId.Red, true));
        BattlePole bluePole = CreatePole("Blue Pole", TeamId.Blue, new Vector3(poleOffset, 2.5f, 0f), GetTeamMaterial(TeamId.Blue, true));
        BattlePole greenPole = CreatePole("Green Pole", TeamId.Green, new Vector3(0f, 2.5f, poleOffset), GetTeamMaterial(TeamId.Green, true));
        BattlePole yellowPole = CreatePole("Yellow Pole", TeamId.Yellow, new Vector3(0f, 2.5f, -poleOffset), GetTeamMaterial(TeamId.Yellow, true));

        CreateAgents(TeamId.Red, redPole, bluePole, GetTeamMaterial(TeamId.Red, false), -FieldSize.x * 0.20f);
        CreateAgents(TeamId.Blue, bluePole, redPole, GetTeamMaterial(TeamId.Blue, false), FieldSize.x * 0.20f);
        CreateAgentsAt(TeamId.Green, greenPole, redPole, GetTeamMaterial(TeamId.Green, false), new Vector3(0f, 1f, FieldSize.y * 0.20f), true);
        CreateAgentsAt(TeamId.Yellow, yellowPole, bluePole, GetTeamMaterial(TeamId.Yellow, false), new Vector3(0f, 1f, -FieldSize.y * 0.20f), true);
    }

    private void CreateEnvironment()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Field";
        ground.transform.position = new Vector3(0f, -0.08f, 0f);
        ground.transform.localScale = new Vector3(FieldSize.x, 0.15f, FieldSize.y);
        ground.GetComponent<Renderer>().material = groundMaterial;
        ParentToRuntime(ground);

        CreateBoundary("North Wall", new Vector3(0f, 0.6f, FieldSize.y * 0.5f), new Vector3(FieldSize.x, 1.2f, 0.25f));
        CreateBoundary("South Wall", new Vector3(0f, 0.6f, -FieldSize.y * 0.5f), new Vector3(FieldSize.x, 1.2f, 0.25f));
        CreateBoundary("West Wall", new Vector3(-FieldSize.x * 0.5f, 0.6f, 0f), new Vector3(0.25f, 1.2f, FieldSize.y));
        CreateBoundary("East Wall", new Vector3(FieldSize.x * 0.5f, 0.6f, 0f), new Vector3(0.25f, 1.2f, FieldSize.y));
    }

    private void CreateBoundary(string name, Vector3 position, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.position = position;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().material = MakeMaterial(new Color(0.18f, 0.18f, 0.18f));
        ParentToRuntime(wall);
    }

    private void CreateCameraAndLight()
    {
        Camera camera = Camera.main;
        if (camera == null)
        {
            GameObject cameraObject = new GameObject("Main Camera");
            camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
        }

        camera.enabled = true;
        camera.clearFlags = CameraClearFlags.Skybox;
        float longestSide = Mathf.Max(FieldSize.x, FieldSize.y);
        camera.orthographic = true;
        float aspect = Mathf.Max(0.75f, camera.aspect);

        float sizeForHeight = FieldSize.y * 0.82f;
        float sizeForWidth = (FieldSize.x * 0.5f / aspect) * 1.48f;
        float cameraHeight = longestSide * 0.82f;
        float cameraBack = longestSide * 0.78f;
        Vector3 cameraPosition = new Vector3(0f, cameraHeight, -cameraBack);
        Quaternion cameraRotation = Quaternion.Euler(56f, 0f, 0f);

        if (SelectedVenue == ExperimentVenue.Large12v12)
        {
            sizeForHeight = FieldSize.y * 0.88f;
            sizeForWidth = (FieldSize.x * 0.5f / aspect) * 1.54f;
            cameraPosition = new Vector3(0f, longestSide * 0.86f, -longestSide * 0.84f);
        }
        else if (SelectedVenue == ExperimentVenue.Huge30v30ThreePoles)
        {
            sizeForHeight = FieldSize.y * 0.92f;
            sizeForWidth = (FieldSize.x * 0.5f / aspect) * 1.58f;
            cameraPosition = new Vector3(0f, longestSide * 0.90f, -longestSide * 0.90f);
        }
        else if (SelectedVenue == ExperimentVenue.FourTeam12v12v12v12)
        {
            sizeForHeight = FieldSize.y * 0.76f;
            sizeForWidth = (FieldSize.x * 0.5f / aspect) * 1.52f;
            cameraPosition = new Vector3(-longestSide * 0.62f, longestSide * 0.88f, -longestSide * 0.62f);
            cameraRotation = Quaternion.LookRotation(Vector3.zero - cameraPosition, Vector3.up);
        }

        camera.orthographicSize = Mathf.Max(sizeForHeight, sizeForWidth);
        camera.transform.position = cameraPosition;
        camera.transform.rotation = cameraRotation;
        camera.fieldOfView = 60f;
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 300f;

        GameObject lightObject = new GameObject("Directional Light");
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.15f;
        light.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
        ParentToRuntime(lightObject);
    }

    private BattlePole CreatePole(string name, TeamId team, Vector3 position, Material material)
    {
        GameObject poleObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        poleObject.name = name;
        poleObject.transform.position = position;
        poleObject.transform.localScale = new Vector3(0.65f, 2.5f, 0.65f);
        poleObject.GetComponent<Renderer>().material = material;
        ParentToRuntime(poleObject);

        BattlePole pole = poleObject.AddComponent<BattlePole>();
        pole.Team = team;
        gameManager.RegisterPole(pole);
        return pole;
    }

    private void CreateAgents(TeamId team, BattlePole ownPole, BattlePole enemyPole, Material material, float startX)
    {
        Color bodyColor = material.color;
        for (int i = 0; i < AgentsPerTeam; i++)
        {
            int columns = AgentsPerTeam >= 30 ? 10 : AgentsPerTeam > 6 ? 4 : 3;
            int row = i / columns;
            int column = i % columns;
            float zSpacing = AgentsPerTeam >= 30 ? 2.15f : 2.15f;
            float rowSpacing = AgentsPerTeam >= 30 ? 1.45f : 1.35f;
            float z = (column - (columns - 1) * 0.5f) * zSpacing;
            float x = startX + (team == TeamId.Red ? -row * rowSpacing : row * rowSpacing);

            GameObject agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            agentObject.name = team + " Agent " + (i + 1);
            agentObject.transform.position = new Vector3(x, 1f, z);
            agentObject.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            agentObject.GetComponent<Renderer>().material = material;
            ParentToRuntime(agentObject);

            PoleAgent agent = agentObject.AddComponent<PoleAgent>();
            agent.Initialize(gameManager, team.ToString() + "-" + (i + 1), team, ownPole, enemyPole, bodyColor);
            ApplyAbilityVariantIfNeeded(agent, i);
            gameManager.RegisterAgent(agent);
        }
    }

    private void ApplyAbilityVariantIfNeeded(PoleAgent agent, int index)
    {
        if (SelectedVenue != ExperimentVenue.AbilityVariant6v6)
        {
            return;
        }

        switch (index)
        {
            case 0:
                agent.SetAbilityProfile("power x2", 2f, 1f);
                break;
            case 1:
                agent.SetAbilityProfile("power x0.5", 0.5f, 1f);
                break;
            case 2:
                agent.SetAbilityProfile("speed x2", 1f, 2f);
                break;
            case 3:
                agent.SetAbilityProfile("speed x0.5", 1f, 0.5f);
                break;
            default:
                agent.SetAbilityProfile("normal", 1f, 1f);
                break;
        }
    }

    private void CreateAgentsAt(TeamId team, BattlePole ownPole, BattlePole enemyPole, Material material, Vector3 startCenter, bool spreadOnX)
    {
        Color bodyColor = material.color;
        int columns = 4;
        for (int i = 0; i < AgentsPerTeam; i++)
        {
            int row = i / columns;
            int column = i % columns;
            float lateral = (column - (columns - 1) * 0.5f) * 2.15f;
            float depth = row * 1.35f * (startCenter.z >= 0f ? 1f : -1f);
            Vector3 position = spreadOnX
                ? new Vector3(startCenter.x + lateral, 1f, startCenter.z + depth)
                : new Vector3(startCenter.x + depth, 1f, startCenter.z + lateral);

            GameObject agentObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            agentObject.name = team + " Agent " + (i + 1);
            agentObject.transform.position = position;
            agentObject.transform.localScale = new Vector3(0.8f, 1f, 0.8f);
            agentObject.GetComponent<Renderer>().material = material;
            ParentToRuntime(agentObject);

            PoleAgent agent = agentObject.AddComponent<PoleAgent>();
            agent.Initialize(gameManager, team.ToString() + "-" + (i + 1), team, ownPole, enemyPole, bodyColor);
            gameManager.RegisterAgent(agent);
        }
    }
}
