using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections;

// Ensure this is attached to the RunnerAgent prefab/instance
public class RunnerAgent : Agent
{
    [Header("Movement Parameters")]
    public float MoveSpeed = 0.3f;
    public float TurnSpeed = 180f;
    public float JumpForce = 5f;
    private bool isGrounded = true;
    private float groundCheckDistance = 0.1f;

    [Header("Agent State")]
    public bool IsFrozen = false;
    public bool IsBeingUnfrozen = false;

    // Internal variables
    private Rigidbody rb;
    [HideInInspector] // No need to assign manually now, but needs link
    public FreezeTagEnvController envController;
    [SerializeField] // Keep collider assignment visible in Inspector
    private Collider unfreezeTriggerCollider;

    // Unfreeze state tracking
    private RunnerAgent currentRescuer = null;
    private float unfreezeTimer = 0f;
    private const float UNFREEZE_TIME_REQUIRED = 5.0f;
    private Coroutine unfreezeRewardCoroutine = null;

    // Add a private field for the visual indicator
    private GameObject unfreezeVisualIndicator;
    private const float UNFREEZE_RADIUS = 5.0f;

    // Add a nested TriggerRelay class to relay trigger events from the child object
    public class TriggerRelay : MonoBehaviour
    {
        public RunnerAgent agent;
        
        void OnTriggerEnter(Collider other)
        {
            if (agent != null) agent.HandleTriggerEnter(other);
        }
        
        void OnTriggerStay(Collider other)
        {
            if (agent != null) agent.HandleTriggerStay(other);
        }
        
        void OnTriggerExit(Collider other)
        {
            if (agent != null) agent.HandleTriggerExit(other);
        }
    }

    public override void Initialize()
    {
        rb = GetComponent<Rigidbody>();
        if (rb == null) {
             //Debug.LogError($"Rigidbody component not found on {name}!", this);
        }
        else {
            // Prevent rolling by freezing rotation on X and Z axes
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }
        
        // Set up unfreeze trigger area - check if we have it first
        if (unfreezeTriggerCollider == null) {
            // Attempt to find trigger collider if not assigned
            SphereCollider sphere = GetComponentInChildren<SphereCollider>();
            if (sphere != null && sphere.isTrigger) {
                 unfreezeTriggerCollider = sphere;
                 //Debug.Log($"Found Unfreeze Trigger Collider on {name}.");
            } else {
                 // Create a sphere collider as a child object for the unfreeze trigger
                 GameObject triggerObj = new GameObject("UnfreezeTrigger");
                 triggerObj.transform.parent = this.transform;
                 triggerObj.transform.localPosition = Vector3.zero;
                 
                 SphereCollider triggerCollider = triggerObj.AddComponent<SphereCollider>();
                 triggerCollider.radius = UNFREEZE_RADIUS; // 5 tile radius
                 triggerCollider.isTrigger = true;
                 
                 // Add a TriggerRelay component to relay trigger events to this agent
                 TriggerRelay relay = triggerObj.AddComponent<TriggerRelay>();
                 relay.agent = this;
                 
                 unfreezeTriggerCollider = triggerCollider;
            }
        }
        
        // Set the size to 5 tiles
        if (unfreezeTriggerCollider is SphereCollider sphereCollider) {
            sphereCollider.radius = UNFREEZE_RADIUS;
        }
        
        // Create the visual indicator for the unfreeze area
        CreateUnfreezeVisualIndicator();
        
        // Start with trigger and visual disabled
        if (unfreezeTriggerCollider != null) unfreezeTriggerCollider.enabled = false;
        if (unfreezeVisualIndicator != null) unfreezeVisualIndicator.SetActive(false);
    }

    public override void OnEpisodeBegin()
    {
        // State is reset by EnvController during spawn (position, rotation, frozen status)
        IsBeingUnfrozen = false;
        currentRescuer = null;
        unfreezeTimer = 0f;
        StopUnfreezeRewardCoroutine();
        if (unfreezeTriggerCollider != null) unfreezeTriggerCollider.enabled = IsFrozen; // Ensure trigger matches state
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Observations:
        sensor.AddObservation(IsFrozen ? 1f : 0f);
        sensor.AddObservation(IsBeingUnfrozen ? 1f : 0f);
        sensor.AddObservation(IsBeingUnfrozen ? (unfreezeTimer / UNFREEZE_TIME_REQUIRED) : 0f);
        // Raycasts added automatically if RayPerceptionSensor component is present
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- Frozen Check ---
        if (IsFrozen)
        {
            if (rb != null)
            {
                 rb.velocity = Vector3.zero;        // Stop linear movement
                 rb.angularVelocity = Vector3.zero; // Stop angular movement
            }
            return; // Do nothing else if frozen
        }

        // --- Read Actions ---
        int forwardBackwardAction = actions.DiscreteActions[0]; // 0=none, 1=forward, 2=backward
        int rotateAction = actions.DiscreteActions[1];          // 0=none, 1=right, 2=left
        int jumpAction = actions.DiscreteActions[2];            // 0=none, 1=jump

        // --- Movement Implementation ---
        Vector3 moveDirection = Vector3.zero;

        // Forward/Backward movement
        if (forwardBackwardAction == 1) moveDirection += transform.forward;
        else if (forwardBackwardAction == 2) moveDirection -= transform.forward;

        // Apply movement as direct velocity, not force
        if (moveDirection != Vector3.zero)
        {
            // Normalize to ensure consistent speed
            moveDirection.Normalize();
            
            // Set horizontal velocity directly (preserving vertical component for jumps)
            Vector3 newVelocity = moveDirection * MoveSpeed;
            newVelocity.y = rb.velocity.y; // Preserve existing vertical velocity
            rb.velocity = newVelocity;
        }
        else
        {
            // If no movement input, stop horizontal movement (preserve vertical for jumps)
            Vector3 newVelocity = Vector3.zero;
            newVelocity.y = rb.velocity.y;
            rb.velocity = newVelocity;
        }

        // Rotation
        if (rotateAction == 1) transform.Rotate(Vector3.up, TurnSpeed * Time.fixedDeltaTime);
        else if (rotateAction == 2) transform.Rotate(Vector3.up, -TurnSpeed * Time.fixedDeltaTime);
        
        // Jump
        if (jumpAction == 1 && isGrounded)
        {
            rb.AddForce(Vector3.up * JumpForce, ForceMode.Impulse);
            isGrounded = false;
        }

        // Check if we're grounded
        CheckGrounded();
    }

    private void CheckGrounded()
    {
        if (Physics.Raycast(transform.position, Vector3.down, groundCheckDistance + 0.1f))
        {
            isGrounded = true;
        }
    }

    private void Jump()
    {
        // Ensure rb is available
        if (rb == null) return;

        // Ground check
        if (isGrounded)
        {
            rb.AddForce(Vector3.up * JumpForce, ForceMode.Impulse);
            isGrounded = false;
            //Debug.Log($"Agent {name} Jumped!");
        }
    }

    // --- Heuristic method for manual testing ---
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var discreteActionsOut = actionsOut.DiscreteActions;
        
        // Forward/Backward: 0=none, 1=forward, 2=backward
        if (Input.GetKey(KeyCode.W))
            discreteActionsOut[0] = 1;
        else if (Input.GetKey(KeyCode.S))
            discreteActionsOut[0] = 2;
        else
            discreteActionsOut[0] = 0;
        
        // Rotation: 0=none, 1=right, 2=left
        if (Input.GetKey(KeyCode.D))
            discreteActionsOut[1] = 1;
        else if (Input.GetKey(KeyCode.A))
            discreteActionsOut[1] = 2;
        else
            discreteActionsOut[1] = 0;
        
        // Jump: 0=none, 1=jump
        discreteActionsOut[2] = Input.GetKey(KeyCode.Space) ? 1 : 0;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = true;
    }

    // Implement the trigger handlers
    public void HandleTriggerEnter(Collider other)
    {
        if (!IsFrozen) return; // Only frozen agents care about triggers
        
        // Check if it's another runner agent that can unfreeze this agent
        if (other.CompareTag("runner"))
        {
            RunnerAgent rescuer = other.GetComponent<RunnerAgent>();
            if (rescuer != null && !rescuer.IsFrozen)
            {
                AttemptUnfreeze(rescuer);
            }
        }
    }

    public void HandleTriggerStay(Collider other)
    {
        // This is handled through the attempt unfreeze logic and timer
    }

    public void HandleTriggerExit(Collider other)
    {
        if (!IsFrozen) return;
        
        // If a runner leaves the area, cancel any unfreeze attempt
        if (other.CompareTag("runner"))
        {
            RunnerAgent rescuer = other.GetComponent<RunnerAgent>();
            if (rescuer != null && rescuer == currentRescuer)
            {
                CancelUnfreezeAttempt(rescuer);
            }
        }
    }

    // Implement unfreeze attempt mechanics
    public void AttemptUnfreeze(RunnerAgent rescuer)
    {
        if (!IsFrozen || rescuer == null || rescuer.IsFrozen) return;
        
        // Set the current rescuer
        currentRescuer = rescuer;
        unfreezeTimer = 0f;
        IsBeingUnfrozen = true;
        
        // Notify the rescuer to start getting rewards
        rescuer.StartUnfreezeRewardCoroutine();
        
        // Change tag to reflect being unfrozen
        gameObject.tag = "BeingUnfreezed";
        
        // Change the color of the visual indicator to indicate unfreezing is in progress
        if (unfreezeVisualIndicator != null)
        {
            MeshRenderer renderer = unfreezeVisualIndicator.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                // Change to light green to show unfreezing in progress
                renderer.material.color = new Color(0.5f, 1f, 0.5f, 0.4f);
            }
        }
        
        // Notify the environment controller
        envController?.ReportUnfreezeStarted(this, rescuer);
    }

    public void CancelUnfreezeAttempt(RunnerAgent rescuer)
    {
        if (!IsBeingUnfrozen || rescuer == null || rescuer != currentRescuer) return;
        
        // Reset state
        IsBeingUnfrozen = false;
        
        // Stop giving reward to the rescuer
        rescuer.StopUnfreezeRewardCoroutine();
        
        // Restore tag to frozen
        gameObject.tag = "frozen runner";
        
        // Change the color of the visual indicator back to default
        if (unfreezeVisualIndicator != null)
        {
            MeshRenderer renderer = unfreezeVisualIndicator.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null)
            {
                // Change back to yellow
                renderer.material.color = new Color(1f, 1f, 0.4f, 0.3f);
            }
        }
        
        // Notify environment controller
        envController?.ReportUnfreezeEnded(this, rescuer, false);
        
        // Reset the timer and clear rescuer
        unfreezeTimer = 0f;
        currentRescuer = null;
    }

    // Add survival reward calculation
    void FixedUpdate()
    {
        // Add survival reward if not frozen
        if (!IsFrozen)
        {
            AddReward(0.01f * Time.fixedDeltaTime); // +0.01 per second for surviving
        }
        
        if (IsBeingUnfrozen && currentRescuer != null)
        {
            unfreezeTimer += Time.fixedDeltaTime;
            
            // Check if unfreeze is complete
            if (unfreezeTimer >= UNFREEZE_TIME_REQUIRED)
            {
                CompleteUnfreeze();
            }
        }
    }

    void CompleteUnfreeze()
    {
        if (!IsBeingUnfrozen || currentRescuer == null) return;
        
        // Give bonus reward to the rescuer for completing the unfreeze
        currentRescuer.AddReward(0.5f);
        
        // Stop the reward coroutine
        currentRescuer.StopUnfreezeRewardCoroutine();
        
        // Notify the environment controller
        envController?.ReportUnfreezeEnded(this, currentRescuer, true);
        
        // Actually unfreeze the agent
        UnfreezeAgent();
    }

    public void FreezeAgent() {
        if (!IsFrozen) {
            //Debug.Log($"{name} has been frozen!");
            IsFrozen = true;
            IsBeingUnfrozen = false;
            gameObject.tag = "frozen runner";
            if(rb != null) {
                rb.constraints = RigidbodyConstraints.FreezeAll;
                rb.velocity = Vector3.zero; // Explicitly stop velocity
                rb.angularVelocity = Vector3.zero; // Explicitly stop rotation
            } else { /*Debug.LogWarning($"Rigidbody missing on {name} during FreezeAgent!");*/ }
            
            // Enable the trigger collider and visual indicator
            if(unfreezeTriggerCollider != null) unfreezeTriggerCollider.enabled = true;
            if(unfreezeVisualIndicator != null) unfreezeVisualIndicator.SetActive(true);

            if(currentRescuer != null) {
                currentRescuer.StopUnfreezeRewardCoroutine();
                envController?.ReportUnfreezeEnded(this, currentRescuer, false); // Use null-conditional operator
                currentRescuer = null;
            }
            unfreezeTimer = 0f;
            StopUnfreezeRewardCoroutine(); // Stop own coroutine if it was somehow running
        }
    }

    public void UnfreezeAgent() {
        if (IsFrozen || IsBeingUnfrozen) { // Can unfreeze from either state only if rb exists
            if (rb == null) {
                //Debug.LogWarning($"Rigidbody missing on {name} during UnfreezeAgent!");
                return; // Can't unfreeze physics state without Rigidbody
            }
            IsFrozen = false;
            IsBeingUnfrozen = false;
            gameObject.tag = "runner";
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ; // Restore movement constraints
            
            // Disable the trigger collider and visual indicator
            if(unfreezeTriggerCollider != null) unfreezeTriggerCollider.enabled = false;
            if(unfreezeVisualIndicator != null) unfreezeVisualIndicator.SetActive(false);
            
            currentRescuer = null;
            unfreezeTimer = 0f;
            StopUnfreezeRewardCoroutine(); // Ensure coroutine is stopped
            //Debug.Log($"{name} has been unfrozen!");
        }
    }

    public void StopUnfreezeRewardCoroutine() {
        if (unfreezeRewardCoroutine != null) {
            StopCoroutine(unfreezeRewardCoroutine);
            unfreezeRewardCoroutine = null;
        }
    }
    
    public void StartUnfreezeRewardCoroutine() {
        if (unfreezeRewardCoroutine == null) {
            unfreezeRewardCoroutine = StartCoroutine(GiveUnfreezeProgressReward());
        }
    }
    
    IEnumerator GiveUnfreezeProgressReward() {
        while(true) {
            AddReward(0.025f * Time.deltaTime); // Increased from 0.01 to 0.025 per second
            yield return null;
        }
    }
    
    void OnTriggerEnter(Collider other) {
        // For collecting foodballs
        if (other.CompareTag("foodball")) {
            CollectFoodball(other.gameObject);
        }
    }
    
    void CollectFoodball(GameObject foodball) {
        if (!IsFrozen) {
            // Add reward for collecting foodball
            AddReward(0.2f);
            
            // Tell environment controller to handle the collection
            envController?.FoodballCollected(foodball);
        }
    }

    // Create a visual indicator for the unfreeze area
    private void CreateUnfreezeVisualIndicator()
    {
        // Create a new GameObject for the visual indicator
        unfreezeVisualIndicator = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        unfreezeVisualIndicator.name = "UnfreezeVisualIndicator";
        unfreezeVisualIndicator.transform.parent = this.transform;
        unfreezeVisualIndicator.transform.localPosition = new Vector3(0, 0.05f, 0); // Slightly above ground
        
        // Scale the cylinder to match the unfreeze radius (flattened)
        unfreezeVisualIndicator.transform.localScale = new Vector3(
            UNFREEZE_RADIUS * 2, // Diameter in X
            0.1f,                // Very flat in Y
            UNFREEZE_RADIUS * 2  // Diameter in Z
        );
        
        // Remove the collider as we don't need physics on the visual
        Destroy(unfreezeVisualIndicator.GetComponent<Collider>());
        
        // Get the renderer and set up a transparent material
        MeshRenderer meshRenderer = unfreezeVisualIndicator.GetComponent<MeshRenderer>();
        Material material = Resources.Load<Material>("Materials/UnfreezeArea");
        if (material == null)
        {
            Debug.LogError("❌ Could not load UnfreezeIndicator material from Resources/Materials/");
            return;
        }
        meshRenderer.material = material;
        
        // Start with the indicator disabled
        unfreezeVisualIndicator.SetActive(false);
    }
}