
using UnityEngine;

public class FreezeBall : MonoBehaviour
{
     public FreezeTagEnvController envController; // Link set by EnvController on spawn

    void OnTriggerEnter(Collider other)
    {
        // Only taggers can collect
        if (other.CompareTag("tagger"))
        {
            // The tagger agent handles its own state/reward in its OnTriggerEnter
            // This script just notifies the controller
             if (envController != null) {
                envController.FreezeBallCollected(this.gameObject);
             } else {
                  Debug.LogError("FreezeBall has no EnvController link!", this);
                  Destroy(gameObject); // Destroy self anyway
             }
        }
    }
}