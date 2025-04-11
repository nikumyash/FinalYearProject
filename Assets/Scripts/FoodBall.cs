
using UnityEngine;

public class Foodball : MonoBehaviour
{
    public FreezeTagEnvController envController; // Link set by EnvController on spawn

    void OnTriggerEnter(Collider other)
    {
        // Only active runners can collect
        if (other.CompareTag("runner"))
        {
            // The runner agent handles its own reward in its OnTriggerEnter
            // This script just notifies the controller
            if (envController != null) {
                envController.FoodballCollected(this.gameObject);
            } else {
                 Debug.LogError("Foodball has no EnvController link!", this);
                 Destroy(gameObject); // Destroy self anyway
            }
        }
    }
}