using System.Collections;
using System.Diagnostics;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.IO;

public class MLAgentsTrainer : MonoBehaviour
{
    [Header("Training Configuration")]
    [SerializeField] private string configPath = "Assets/Config/freeze_tag_config.yaml";
    [SerializeField] private string runID = "freeze_tag_1";
    [SerializeField] private bool useGraphics = true;
    [SerializeField] private string condaEnvironment = "project_env";
    
    [Header("Environment Settings")]
    [SerializeField] private int numParallelEnvironments = 1; // Number of parallel environments to train with
    // [SerializeField] private bool forceCPU = false; // Force training on CPU instead of GPU
    
    [Header("UI")]
    [SerializeField] private GameObject trainingUI;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button stopButton;
    
    private Process trainingProcess;
    private bool isTraining = false;
    
    private void Start()
    {
        // if (Application.isBatchMode || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
        // {
        //     Debug.Log("[MainMenuManager] Headless mode detected. Loading Level1 directly.");
        //     SceneManager.LoadScene("Level1");
        //     return;
        // }

        if (stopButton != null)
        {
            stopButton.onClick.AddListener(StopTraining);
        }
        
        if (trainingUI != null)
        {
            trainingUI.SetActive(false);
        }
    }

    public void StartTraining()
    {
        if (isTraining)
        {
            UnityEngine.Debug.Log("Training is already in progress");
            return;
        }
        
        isTraining = true;
        
        if (trainingUI != null)
        {
            trainingUI.SetActive(true);
        }
        
        if (statusText != null)
        {
            statusText.text = "Starting ML-Agents training...";
        }
        
        StartCoroutine(RunTrainingProcess());
    }
    
    private IEnumerator RunTrainingProcess()
    {
        yield return new WaitForSeconds(0.5f); // Small delay to let UI update
        
        string mlAgentsArgs = "";
        try
        {
            // Build ML-Agents arguments
            mlAgentsArgs = configPath;
            mlAgentsArgs += " --run-id=" + runID;
            
            // Note: These parameters are set in the YAML configuration file
            // and should not be passed as command-line arguments
            
            // Add number of parallel environments
            if (numParallelEnvironments > 1)
            {
                mlAgentsArgs += $" --num-envs={numParallelEnvironments}";
            }
            
            if (!useGraphics)
            {
                mlAgentsArgs += " --no-graphics";
            }
            
            UnityEngine.Debug.Log($"Starting ML-Agents training with args: {mlAgentsArgs}");
            
            // Using the original PowerShell command format
            trainingProcess = new Process();
            trainingProcess.StartInfo.FileName = "powershell.exe";
            trainingProcess.StartInfo.Arguments = $"-NoExit -Command \"conda activate {condaEnvironment}; mlagents-learn {mlAgentsArgs}\"";
            trainingProcess.StartInfo.UseShellExecute = true;
            trainingProcess.StartInfo.CreateNoWindow = false;
            trainingProcess.StartInfo.RedirectStandardOutput = false;
            
            trainingProcess.Start();
            
            if (statusText != null)
            {
                string status = $"Training in progress using {condaEnvironment} environment...\n" +
                                $"Training {numParallelEnvironments} parallel environments\n" +
                                "Check console window for details";
                statusText.text = status;
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error starting training: {e.Message}");
            
            if (statusText != null)
            {
                statusText.text = $"Training error: {e.Message}";
            }
            
            isTraining = false;
            yield break; // Exit the coroutine
        }
        
        // Wait for process to exit - moved outside try/catch
        while (trainingProcess != null && !trainingProcess.HasExited)
        {
            yield return new WaitForSeconds(1.0f);
        }
        
        if (statusText != null)
        {
            statusText.text = "Training completed!";
        }
        
        isTraining = false;
    }
    
    public void StopTraining()
    {
        if (!isTraining || trainingProcess == null)
        {
            return;
        }
        
        try
        {
            UnityEngine.Debug.Log("Stopping ML-Agents training process");
            trainingProcess.Kill();
            trainingProcess = null;
            
            if (statusText != null)
            {
                statusText.text = "Training stopped";
            }
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError($"Error stopping training: {e.Message}");
        }
        
        isTraining = false;
        
        if (trainingUI != null)
        {
            trainingUI.SetActive(false);
        }
    }
    
    private void OnDestroy()
    {
        StopTraining();
        
        if (stopButton != null)
        {
            stopButton.onClick.RemoveListener(StopTraining);
        }
    }
}