using UnityEngine;
using UnityEngine.SceneManagement; // Required for scene management
using UnityEngine.UI; // Required for UI elements like Buttons

public class MainMenuManager : MonoBehaviour
{
    public Button level1Button;
    public Button level2Button;
    public Button trainButton;  // Renamed from trainedButton to trainButton
    
    private MLAgentsTrainer mlAgentsTrainer;

    void Start()
    {
        // Find or create the ML-Agents trainer
        mlAgentsTrainer = FindObjectOfType<MLAgentsTrainer>();
        if (mlAgentsTrainer == null)
        {
            GameObject trainerObj = new GameObject("ML-Agents Trainer");
            mlAgentsTrainer = trainerObj.AddComponent<MLAgentsTrainer>();
            DontDestroyOnLoad(trainerObj);
        }
        
        // Add listeners to the buttons OnClick events
        if (level1Button != null) {
            level1Button.onClick.AddListener(LoadLevel1);
        } else {
            Debug.LogWarning("Level 1 Button not assigned in MainMenuManager.");
        }

        if (level2Button != null) {
             level2Button.onClick.AddListener(LoadLevel2);
        } else {
             Debug.LogWarning("Level 2 Button not assigned in MainMenuManager.");
        }
        
        if (trainButton != null) {
            trainButton.onClick.AddListener(StartTraining);
        } else {
            Debug.LogWarning("Train Button not assigned in MainMenuManager.");
        }
    }

    public void LoadLevel1()
    {
        Debug.Log("Loading Level 1...");
        // Make sure "Level1" scene is added to Build Settings (File -> Build Settings)
        SceneManager.LoadScene("Level1");
    }

    public void LoadLevel2()
    {
        Debug.Log("Loading Level 2...");
        // Make sure "Level2" scene is added to Build Settings
        SceneManager.LoadScene("Level2");
    }
    
    public void StartTraining()
    {
        Debug.Log("Starting ML-Agents training...");
        if (mlAgentsTrainer != null)
        {
            mlAgentsTrainer.StartTraining();
        }
        else
        {
            Debug.LogError("ML-Agents Trainer not found!");
        }
    }

     void OnDestroy() {
         // Clean up listeners if the object is destroyed
         if (level1Button != null) level1Button.onClick.RemoveListener(LoadLevel1);
         if (level2Button != null) level2Button.onClick.RemoveListener(LoadLevel2);
         if (trainButton != null) trainButton.onClick.RemoveListener(StartTraining);
     }
}