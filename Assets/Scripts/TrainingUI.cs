using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TrainingUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private Button closeButton;
    
    private void Start()
    {
        if (closeButton != null)
        {
            closeButton.onClick.AddListener(Hide);
        }
        
        // Start hidden
        gameObject.SetActive(false);
    }
    
    public void SetStatusText(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }
    
    public void Show()
    {
        gameObject.SetActive(true);
    }
    
    public void Hide()
    {
        gameObject.SetActive(false);
    }
    
    private void OnDestroy()
    {
        if (closeButton != null)
        {
            closeButton.onClick.RemoveListener(Hide);
        }
    }
} 