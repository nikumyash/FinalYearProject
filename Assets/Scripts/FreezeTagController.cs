using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;  // Add this for BehaviorParameters and BehaviorType
using System.Collections.Generic;
using UnityEngine.UI; // Add UI namespace
using TMPro; // Add TextMeshPro namespace
using System.Collections; // Add for coroutines

public class FreezeTagEnvController : MonoBehaviour
{
    [System.Serializable]
    public class AgentInfo // This class seems unused currently, can be removed if not needed
    {
        public Agent AgentReference;
        public Vector3 StartingPos;
        public Quaternion StartingRot;
    }

    [Header("Environment Objects")]
    public GameObject FoodballPrefab;
    public GameObject FreezeBallPrefab;
    public Transform EnvironmentBounds; // An object defining spawn area (e.g., the ground plane)

    [Header("Agent Prefabs")] // <<< NEW SECTION >>>
    public GameObject RunnerAgentPrefab;
    public GameObject TaggerAgentPrefab;

    [Header("UI Elements")]
    public TextMeshProUGUI timerText; // Reference to the timer text UI element
    public GameObject timerPanel; // Panel containing the timer (can be enabled/disabled)

    [Header("Level Control")]
    [Tooltip("Check this to override ML-Agents curriculum and force a specific level")]
    public bool overrideLevel = false;
    [Tooltip("The level to use when override is enabled (0=Level1, 1=Level2)")]
    public int manualLevelIndex = 0;

    // Agent lists will now store dynamically spawned agents
    private List<RunnerAgent> RunnerAgents = new List<RunnerAgent>();
    private List<TaggerAgent> TaggerAgents = new List<TaggerAgent>();

    [Header("Level Configuration (Read from Curriculum)")]
    private int levelIndex = 0; // 0 for Level 1, 1 for Level 2
    
    [Header("Manual Configuration (Used in Heuristic Mode)")]
    [Tooltip("Number of runner agents to spawn")]
    [SerializeField] private int manualRunnerCount = 5;
    [Tooltip("Number of tagger agents to spawn (Level 2 only)")]
    [SerializeField] private int manualTaggerCount = 1;
    [Tooltip("Number of foodballs to spawn")]
    [SerializeField] private int manualFoodballCount = 10;
    [Tooltip("Number of freezeballs to spawn (Level 2 only)")]
    [SerializeField] private int manualFreezeBallCount = 5;
    [Tooltip("Time limit for the game in seconds")]
    [SerializeField] private float manualGameTimeLimit = 60.0f;
    
    // Variables used during gameplay (set from either manual values or curriculum)
    private int numRunnersToSpawn = 5; // Default value, will be overwritten
    private int numTaggersToSpawn = 0; // Default value, will be overwritten
    private int numFoodballsToSpawn = 10; // Default value, will be overwritten
    private int numFreezeBallsToSpawn = 5; // Default value, will be overwritten
    private float gameTimeLimit = 60.0f; // Default value, will be overwritten

    [Header("Spawning Limits")]
    public int MaxFoodballs = 10; // Max concurrent foodballs - Can be set by curriculum or fixed
    public int MaxFreezeBalls = 5; // Max concurrent freezeballs - Can be set by curriculum or fixed
    private List<GameObject> activeFoodballs = new List<GameObject>();
    private List<GameObject> activeFreezeBalls = new List<GameObject>();

    [Header("Game State")]
    private float currentGameTime = 0f;
    private bool isLevel2 => levelIndex == 1;

    // Add a field to track the previous level index
    private int previousLevelIndex = -1; // Initialize to -1 to ensure first change is logged

    // Add this field near the top with other private fields
    private bool resetInProgress = false;

    void Awake()
    {
        Debug.unityLogger.logEnabled = false;
        Debug.Log("FreezeTagEnvController Awake started");
        
        // Initialize lists - they will be populated during EnvironmentReset
        RunnerAgents = new List<RunnerAgent>();
        TaggerAgents = new List<TaggerAgent>();

        // Create or find the timer UI
        EnsureTimerUIExists();

        // Check if Academy is available
        if (Academy.IsInitialized)
        {
            Debug.Log("ML-Agents Academy detected - setting overrideLevel to false to use curriculum");
            overrideLevel = false;
            
            Debug.Log("Subscribing to Academy.Instance.OnEnvironmentReset event");
            Academy.Instance.OnEnvironmentReset += EnvironmentReset;
            Debug.Log("Successfully subscribed to OnEnvironmentReset event");
        }
        else
        {
            Debug.Log("No ML-Agents Academy detected - setting overrideLevel to true to use manual settings");
            overrideLevel = true;
            
            // Call EnvironmentReset manually since there's no Academy to trigger it
            Debug.Log("Manually calling EnvironmentReset since no Academy is available");
            EnvironmentReset();
        }

        Debug.Log("FreezeTagEnvController Awake completed");
    }

    void OnDestroy()
    {
        Debug.Log("FreezeTagEnvController OnDestroy called");
        if (Academy.IsInitialized)
        {
            Debug.Log("Unsubscribing from Academy.Instance.OnEnvironmentReset event");
            Academy.Instance.OnEnvironmentReset -= EnvironmentReset;
            Debug.Log("Successfully unsubscribed from OnEnvironmentReset event");
        }
        else
        {
            Debug.Log("Academy not initialized, no need to unsubscribe");
        }
        
        // Clean up any remaining spawned objects if the controller is destroyed mid-game
        Debug.Log("Cleaning up spawned objects");
        ClearAllSpawnedObjects();
        Debug.Log("FreezeTagEnvController OnDestroy completed");
    }


    void FixedUpdate()
    {
        // Skip processing if reset is in progress
        if (resetInProgress) return;
        
        // Update timer for both Level 1 and Level 2
        currentGameTime += Time.fixedDeltaTime;
        
        // Update the timer UI
        UpdateTimerText();
        
        if (gameTimeLimit > 0 && currentGameTime >= gameTimeLimit)
        {
            HandleTimeout();
        }
        
        // Level 2 specific checks
        if (isLevel2)
        {
            // Check win conditions less frequently? Or check within agent actions/rewards?
            // Checking every FixedUpdate might be slightly heavy depending on agent count.
            CheckAllRunnersFrozen(); // Keep checking win condition
        }
    }

    void ApplyCurriculumParameters()
    {
        bool isHeuristicMode = false;
        
        // Check if Academy is available
        if (!Academy.IsInitialized)
        {
            Debug.Log("No Academy instance available - using manual parameters only");
            overrideLevel = true;
            levelIndex = manualLevelIndex;
            
            // Use manual values
            numRunnersToSpawn = manualRunnerCount;
            numTaggersToSpawn = manualTaggerCount;
            numFoodballsToSpawn = manualFoodballCount;
            numFreezeBallsToSpawn = manualFreezeBallCount;
            gameTimeLimit = manualGameTimeLimit;
            
            // Set Max items
            MaxFoodballs = manualFoodballCount;
            MaxFreezeBalls = manualFreezeBallCount;
            
            Debug.Log($"Using manual values: Level={levelIndex}, " +
                     $"Runners={numRunnersToSpawn}, Taggers={numTaggersToSpawn}, " +
                     $"Food={numFoodballsToSpawn}, Freeze={numFreezeBallsToSpawn}, Time={gameTimeLimit}");
            return;
        }
        
        var envParams = Academy.Instance.EnvironmentParameters;

        // Check if any agents are in heuristic mode
        if (RunnerAgents.Count > 0 && RunnerAgents[0] != null)
        {
            isHeuristicMode = RunnerAgents[0].GetComponent<BehaviorParameters>().BehaviorType == BehaviorType.HeuristicOnly;
        }
        else if (TaggerAgents.Count > 0 && TaggerAgents[0] != null)
        {
            isHeuristicMode = TaggerAgents[0].GetComponent<BehaviorParameters>().BehaviorType == BehaviorType.HeuristicOnly;
        }

        // Get the new level index - if override is enabled or in heuristic mode, use manual level instead
        int newLevelIndex;
        if (overrideLevel || isHeuristicMode)
        {
            newLevelIndex = manualLevelIndex;
            if (isHeuristicMode)
            {
                Debug.Log("Heuristic mode detected - using manual settings instead of YAML parameters");
            }
            else
            {
                Debug.Log($"Using manual level override: {newLevelIndex}");
            }
        }
        else
        {
            newLevelIndex = (int)envParams.GetWithDefault("level_index", 0f);
            Debug.Log($"Level Index we got from params is {newLevelIndex} , levelIndex is {levelIndex}, previousLevelIndex is {previousLevelIndex}");
        }
        
        // Check if level has changed
        if (newLevelIndex != previousLevelIndex)
        {
            Debug.Log($"Level changed: {previousLevelIndex} -> {newLevelIndex}");
            previousLevelIndex = newLevelIndex;
        }
        float rawLesson = envParams.GetWithDefault("lesson", -1f);
        float rawLevel = envParams.GetWithDefault("level_index", -99f);
        Debug.Log($"[DEBUG] Env Parameters: lesson = {rawLesson}, level_index = {rawLevel}");

        levelIndex = newLevelIndex;

        // Use manual values in heuristic mode, otherwise use env parameters
        if (overrideLevel || isHeuristicMode)
        {
            // Use the manual values from Inspector
            numRunnersToSpawn = manualRunnerCount;
            numTaggersToSpawn = manualTaggerCount;
            numFoodballsToSpawn = manualFoodballCount;
            numFreezeBallsToSpawn = manualFreezeBallCount;
            gameTimeLimit = manualGameTimeLimit;
            
            // Optionally link Max items to manual values
            MaxFoodballs = manualFoodballCount;
            MaxFreezeBalls = manualFreezeBallCount;
            
            // Keep the current inspector values rather than getting from YAML
            Debug.Log($"Using manual values in heuristic mode: Level={levelIndex}, " +
                     $"Runners={numRunnersToSpawn}, Taggers={numTaggersToSpawn}, " +
                     $"Food={numFoodballsToSpawn}, Freeze={numFreezeBallsToSpawn}, Time={gameTimeLimit}");
        }
        else
        {
            // Read values from YAML as before
            numRunnersToSpawn = (int)envParams.GetWithDefault("num_runners", 5f);
            numTaggersToSpawn = (int)envParams.GetWithDefault("num_taggers", 0f);
            numFoodballsToSpawn = (int)envParams.GetWithDefault("num_foodballs", 10f);
            numFreezeBallsToSpawn = (int)envParams.GetWithDefault("num_freezeballs", 5f);
            gameTimeLimit = envParams.GetWithDefault("time_limit", 60.0f);

            // Optionally link Max items to spawned number, or keep fixed
            MaxFoodballs = numFoodballsToSpawn;
            MaxFreezeBalls = numFreezeBallsToSpawn;

            Debug.Log($"Applied Curriculum: Level={levelIndex}, Runners={numRunnersToSpawn}, Taggers={numTaggersToSpawn}, Food={numFoodballsToSpawn}, Freeze={numFreezeBallsToSpawn}, Time={gameTimeLimit}");
        }
    }

    void EnvironmentReset()
    {
        Debug.Log("Environment Reset started...");

        // 1. Clear old spawned objects
        Debug.Log("Step 1: Clearing old spawned objects...");
        ClearAllSpawnedObjects();

        // 2. Apply curriculum parameters to get counts etc.
        Debug.Log("Step 2: Applying curriculum parameters...");
        ApplyCurriculumParameters();

        // 3. Reset Timer
        Debug.Log("Step 3: Resetting game timer...");
        currentGameTime = 0f;
        
        // 4. Setup and initialize UI
        Debug.Log("Step 4: Setting up timer UI...");
        SetupTimerUI();

        // 5. Spawn Agents based on parameters
        Debug.Log("Step 5: Spawning agents...");
        SpawnRunners();
        SpawnTaggers(); // This function will internally check if it's Level 2

        // 6. Spawn initial items based on parameters
        Debug.Log("Step 6: Spawning items...");
        for (int i = 0; i < numFoodballsToSpawn; i++) SpawnFoodball();
        if (isLevel2)
        {
             for (int i = 0; i < numFreezeBallsToSpawn; i++) SpawnFreezeBall();
        }

        // Reset the flag now that environment is fully reset
        resetInProgress = false;
        Debug.Log("Environment Reset completed successfully. resetInProgress = false");
    }

    // Setup timer UI based on level
    private void SetupTimerUI()
    {
        if (timerPanel != null)
        {
            timerPanel.SetActive(true);
        }
        
        if (timerText != null)
        {
            string levelName = isLevel2 ? "Level 2" : "Level 1";
            UpdateTimerText();
        }
    }

    // Update timer display
    private void UpdateTimerText()
    {
        if (timerText != null)
        {
            float timeRemaining = Mathf.Max(0, gameTimeLimit - currentGameTime);
            int minutes = Mathf.FloorToInt(timeRemaining / 60);
            int seconds = Mathf.FloorToInt(timeRemaining % 60);
            timerText.text = string.Format("{0}:{1:00} - {2}", 
                                          minutes, seconds, 
                                          isLevel2 ? "Level 2" : "Level 1");
        }
    }

    void ClearAllSpawnedObjects()
    {
        Debug.Log($"Clearing {RunnerAgents.Count} runners and {TaggerAgents.Count} taggers");
        
        // Clear existing agents from previous episode
        foreach (var agent in RunnerAgents) 
        { 
            if (agent != null) 
            {
                Debug.Log($"Destroying runner {agent.name}");
                Destroy(agent.gameObject); 
            }
        }
        RunnerAgents.Clear();
        
        foreach (var agent in TaggerAgents) 
        { 
            if (agent != null) 
            {
                Debug.Log($"Destroying tagger {agent.name}");
                Destroy(agent.gameObject); 
            }
        }
        TaggerAgents.Clear();

        // Clear existing items
        ClearItems(activeFoodballs);
        ClearItems(activeFreezeBalls);
        
        Debug.Log("All spawned objects cleared");
    }

    void ClearItems(List<GameObject> items)
    {
        foreach (var item in items)
        {
            if (item != null) Destroy(item);
        }
        items.Clear();
    }

    void SpawnRunners()
    {
        if (RunnerAgentPrefab == null) {
            Debug.LogError("RunnerAgentPrefab not assigned in Inspector!");
            return;
        }

        // First spawn all runners and set them to frozen
        for (int i = 0; i < numRunnersToSpawn; i++)
        {
            // Instantiate the prefab as a child of this controller's transform
            GameObject runnerGO = Instantiate(RunnerAgentPrefab, GetRandomSpawnPos(), Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
            runnerGO.name = $"RunnerAgent_{i}"; // Optional: Give unique names for debugging
            RunnerAgent runnerAgent = runnerGO.GetComponent<RunnerAgent>();

            if (runnerAgent != null)
            {
                RunnerAgents.Add(runnerAgent); // Add to list for tracking
                runnerAgent.envController = this; // Ensure the agent knows its controller

                // Set all runners to frozen initially
                runnerAgent.FreezeAgent();
            }
            else
            {
                Debug.LogError("RunnerAgentPrefab is missing the RunnerAgent script component!", RunnerAgentPrefab);
                Destroy(runnerGO); // Clean up invalid instance
            }
        }

        // Determine how many runners to unfreeze based on level
        int runnersToUnfreeze;
        if (isLevel2)
        {
            // In Level 2, unfreeze half of the runners
            runnersToUnfreeze = Mathf.CeilToInt(RunnerAgents.Count / 2f);  // Use ceiling to round up
            Debug.Log($"Level 2: Unfreezing {runnersToUnfreeze} out of {RunnerAgents.Count} runners");
        }
        else
        {
            // In Level 1, unfreeze just one runner
            runnersToUnfreeze = 1;
            Debug.Log($"Level 1: Unfreezing only 1 out of {RunnerAgents.Count} runners");
        }

        // Create a shuffled list of indices to randomize which runners get unfrozen
        List<int> indices = new List<int>();
        for (int i = 0; i < RunnerAgents.Count; i++)
        {
            indices.Add(i);
        }
        // Fisher-Yates shuffle
        for (int i = 0; i < indices.Count; i++)
        {
            int temp = indices[i];
            int randomIndex = Random.Range(i, indices.Count);
            indices[i] = indices[randomIndex];
            indices[randomIndex] = temp;
        }

        // Unfreeze the determined number of runners
        for (int i = 0; i < runnersToUnfreeze && i < indices.Count; i++)
        {
            RunnerAgents[indices[i]].UnfreezeAgent();
            Debug.Log($"Runner {indices[i]} unfrozen.");
        }

        Debug.Log($"Spawned {RunnerAgents.Count} runners with {runnersToUnfreeze} unfrozen.");
    }

     void SpawnTaggers()
    {
        // Only spawn taggers if it's level 2 and the count is > 0
        if (!isLevel2 || numTaggersToSpawn <= 0) {
            // Ensure any residual taggers from a previous different level are cleared
             foreach (var agent in TaggerAgents) { if (agent != null) Destroy(agent.gameObject); }
             TaggerAgents.Clear();
             return;
        }

        if (TaggerAgentPrefab == null) {
             Debug.LogError("TaggerAgentPrefab not assigned in Inspector!");
             return;
        }

        for (int i = 0; i < numTaggersToSpawn; i++)
        {
            GameObject taggerGO = Instantiate(TaggerAgentPrefab, GetRandomSpawnPos(), Quaternion.Euler(0, Random.Range(0f, 360f), 0), transform);
            taggerGO.name = $"TaggerAgent_{i}";
            TaggerAgent taggerAgent = taggerGO.GetComponent<TaggerAgent>();

            if (taggerAgent != null)
            {
                TaggerAgents.Add(taggerAgent);
                taggerAgent.envController = this;
                taggerAgent.ResetFreezeBalls(); // Ensure starting state
                taggerGO.SetActive(true); // Make sure it's active
            }
            else
            {
                 Debug.LogError("TaggerAgentPrefab is missing the TaggerAgent script component!", TaggerAgentPrefab);
                 Destroy(taggerGO);
            }
        }
         Debug.Log($"Spawned {TaggerAgents.Count} taggers.");
    }


    public Vector3 GetRandomSpawnPos()
    {
        float spawnAreaMargin = 1f;
        if (EnvironmentBounds == null) {
            Debug.LogError("EnvironmentBounds not assigned!");
            return transform.position; // Fallback
        }
        Collider envCollider = EnvironmentBounds.GetComponent<Collider>();
        if (envCollider == null) {
             Debug.LogError("EnvironmentBounds needs a Collider component!", EnvironmentBounds.gameObject);
             return EnvironmentBounds.position + Vector3.up * 0.5f; // Fallback
        }
        Bounds bounds = envCollider.bounds;

        // Add Log: Visual confirmation of bounds
        // Debug.Log($"Spawn Bounds: Center={bounds.center}, Size={bounds.size}");

        if (bounds.size.x <= spawnAreaMargin * 2 || bounds.size.z <= spawnAreaMargin * 2)
        {
             Debug.LogWarning("Spawn area defined by EnvironmentBounds collider is too small or invalid after margin.", EnvironmentBounds);
             return bounds.center + Vector3.up * 0.5f; // Use center if area too small
        }

        float randomX = Random.Range(bounds.min.x + spawnAreaMargin, bounds.max.x - spawnAreaMargin);
        float randomZ = Random.Range(bounds.min.z + spawnAreaMargin, bounds.max.z - spawnAreaMargin);
        // Ensure Y position is slightly above the BOUNDS center Y, not necessarily world 0
        Vector3 spawnPos = new Vector3(randomX, bounds.center.y + 0.5f, randomZ);

        // Add Log: Visual confirmation of spawn point
        // Debug.Log($"Calculated Spawn Position: {spawnPos}");

        return spawnPos;
    }

    // --- SpawnFoodball, SpawnFreezeBall, Collection, Reporting, Win/Loss Checks remain largely the same ---
    // --- Ensure they reference the dynamic RunnerAgents and TaggerAgents lists correctly ---

    public void SpawnFoodball()
    {
        if (activeFoodballs.Count >= MaxFoodballs) return;
        if (FoodballPrefab == null) { Debug.LogError("FoodballPrefab not assigned!"); return; }

        GameObject food = Instantiate(FoodballPrefab, GetRandomSpawnPos(), Quaternion.identity, transform); // Parent optional
        Foodball fbScript = food.GetComponent<Foodball>();
        if (fbScript != null) { fbScript.envController = this; }
        else { Debug.LogError("FoodballPrefab missing Foodball script!"); Destroy(food); return;}
        activeFoodballs.Add(food);
    }

     public void SpawnFreezeBall()
    {
        if (!isLevel2 || activeFreezeBalls.Count >= MaxFreezeBalls) return;
        if (FreezeBallPrefab == null) { Debug.LogError("FreezeBallPrefab not assigned!"); return; }

        GameObject freeze = Instantiate(FreezeBallPrefab, GetRandomSpawnPos(), Quaternion.identity, transform); // Parent optional
        FreezeBall frbScript = freeze.GetComponent<FreezeBall>();
         if (frbScript != null) { frbScript.envController = this;}
         else { Debug.LogError("FreezeBallPrefab missing FreezeBall script!"); Destroy(freeze); return;}
        activeFreezeBalls.Add(freeze);
    }

     public void FoodballCollected(GameObject foodball)
    {
        if (activeFoodballs.Remove(foodball))
        {
            Destroy(foodball);
            SpawnFoodball();
        }
    }

     public void FreezeBallCollected(GameObject freezeBall)
    {
         if (activeFreezeBalls.Remove(freezeBall))
         {
            Destroy(freezeBall);
            SpawnFreezeBall();
         }
    }

     public void ReportUnfreezeStarted(RunnerAgent frozenAgent, RunnerAgent rescuer)
    {
        if (isLevel2)
        {
            foreach(var tagger in TaggerAgents) { // Iterate through the dynamically populated list
                if (tagger != null && tagger.gameObject.activeSelf) {
                    tagger.StartApplyingUnfreezePenalty();
                }
            }
        }
    }

     public void ReportUnfreezeEnded(RunnerAgent frozenAgent, RunnerAgent rescuer, bool success)
    {
         if (isLevel2)
        {
            foreach(var tagger in TaggerAgents) { // Iterate through the dynamically populated list
                 if (tagger != null && tagger.gameObject.activeSelf) {
                    tagger.StopApplyingUnfreezePenalty();
                    if (success) {
                        tagger.AddReward(-0.5f);
                         Debug.Log($"Tagger {tagger.name} penalized for Runner unfreeze.");
                    }
                }
            }
        }
    }

     void CheckAllRunnersFrozen()
    {
         if (!isLevel2 || RunnerAgents.Count == 0) return; // Check only in level 2 and if runners exist

        int frozenCount = 0;
        foreach(var runner in RunnerAgents) // Iterate through the dynamically populated list
        {
             // Important: Check if runner object still exists before accessing properties
            if (runner != null && runner.IsFrozen) {
                frozenCount++;
            }
        }

        if (frozenCount == RunnerAgents.Count)
        {
            Debug.Log("All runners frozen! Taggers win.");
            foreach(var tagger in TaggerAgents) { // Iterate through the dynamically populated list
                 if (tagger != null && tagger.gameObject.activeSelf) {
                    tagger.SetReward(1.0f);
                 }
            }
            EndEpisodeForAllAgents(); // End episode for all agents
        }
    }

    void HandleTimeout()
    {
        //Debug.Log("Time limit reached! Episode ending.");
        
        // Level 2: Runners win if time is up
        if (isLevel2)
        {
            //Debug.Log("Time limit reached! Runners win.");
            
            // Give penalty to taggers
            foreach(var tagger in TaggerAgents)
            {
                if (tagger != null && tagger.gameObject.activeSelf)
                {
                    tagger.SetReward(-1.0f);
                }
            }
            
            // Give reward to runners for surviving
            foreach(var runner in RunnerAgents)
            {
                if (runner != null && !runner.IsFrozen)
                {
                    runner.AddReward(1.0f); // +1 reward for surviving until time limit
                }
            }
        }
        
        // End episode for all agents, works for both Level 1 and Level 2
        EndEpisodeForAllAgents();
    }

     void EndEpisodeForAllAgents() {
        // If already resetting, don't trigger again
        if (resetInProgress) return;
        
        resetInProgress = true;
        Debug.Log("Ending episode for all dynamically spawned agents.");
        
        // Use ToList() to create copy in case EndEpisode modifies original list indirectly
        List<RunnerAgent> runnersToEnd = new List<RunnerAgent>(RunnerAgents);
        Debug.Log($"About to end episode for {runnersToEnd.Count} runners");
        
        int runnerCount = 0;
        foreach(var runner in runnersToEnd) {
            if (runner != null) {
                runner.EndEpisode();
                Debug.Log($"EndEpisode called on runner {runnerCount} - {runner.name}");
                // Disable the GameObject to prevent further interactions
                runner.gameObject.SetActive(false);
                runnerCount++;
            }
        }
        Debug.Log($"Finished ending episodes for {runnerCount} runners");
        
        List<TaggerAgent> taggersToEnd = new List<TaggerAgent>(TaggerAgents);
        Debug.Log($"About to end episode for {taggersToEnd.Count} taggers");
        
        int taggerCount = 0;
        foreach(var tagger in taggersToEnd) {
            if (tagger != null && tagger.gameObject.activeSelf) {
                tagger.EndEpisode();
                Debug.Log($"EndEpisode called on tagger {taggerCount} - {tagger.name}");
                // Disable the GameObject to prevent further interactions
                tagger.gameObject.SetActive(false);
                taggerCount++;
            }
        }
        Debug.Log($"Finished ending episodes for {taggerCount} taggers");
        Debug.Log("All EndEpisode calls completed. Waiting for EnvironmentReset to be called.");
        
        // Force a manual reset since the Academy.OnEnvironmentReset event might not trigger correctly
        Debug.Log("Scheduling manual environment reset");
        Invoke("ManualEnvironmentReset", 0.5f);
    }
    
    void ManualEnvironmentReset()
    {
        Debug.Log("ManualEnvironmentReset called - forcing environment reset");
        EnvironmentReset();
    }

    // Optional: Gizmos still useful for debugging spawn area
    void OnDrawGizmosSelected()
    {
        if (EnvironmentBounds != null)
        {
            Collider col = EnvironmentBounds.GetComponent<Collider>();
            if (col != null)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawWireCube(col.bounds.center, col.bounds.size);
            }
        }
    }

    // Ensure timer UI exists
    private void EnsureTimerUIExists()
    {
        // Check if we already have a reference to the timer UI
        if (timerText == null || timerPanel == null)
        {
            // Try to find existing timer UI in the scene
            Canvas canvas = FindObjectOfType<Canvas>();
            
            // If no canvas exists, create one
            if (canvas == null)
            {
                GameObject canvasObj = new GameObject("UICanvas");
                canvas = canvasObj.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObj.AddComponent<CanvasScaler>();
                canvasObj.AddComponent<GraphicRaycaster>();
            }
            
            // Create timer panel if not found
            if (timerPanel == null)
            {
                GameObject panel = new GameObject("TimerPanel");
                panel.transform.SetParent(canvas.transform, false);
                
                RectTransform panelRect = panel.AddComponent<RectTransform>();
                panelRect.anchorMin = new Vector2(0.5f, 1f);
                panelRect.anchorMax = new Vector2(0.5f, 1f);
                panelRect.pivot = new Vector2(0.5f, 1f);
                panelRect.sizeDelta = new Vector2(200, 50);
                panelRect.anchoredPosition = new Vector2(0, 0);
                
                Image panelImage = panel.AddComponent<Image>();
                panelImage.color = new Color(0, 0, 0, 0.7f);
                
                timerPanel = panel;
            }
            
            // Create timer text if not found
            if (timerText == null)
            {
                GameObject textObj = new GameObject("TimerText");
                textObj.transform.SetParent(timerPanel.transform, false);
                
                RectTransform textRect = textObj.AddComponent<RectTransform>();
                textRect.anchorMin = Vector2.zero;
                textRect.anchorMax = Vector2.one;
                textRect.offsetMin = new Vector2(10, 5);
                textRect.offsetMax = new Vector2(-10, -5);
                
                timerText = textObj.AddComponent<TextMeshProUGUI>();
                timerText.fontSize = 24;
                timerText.alignment = TextAlignmentOptions.Center;
                timerText.color = Color.white;
                timerText.text = "0:00";
            }
        }
    }
}