using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Sensors;
using Unity.MLAgents.Actuators;
using System.Collections; // Needed if you use coroutines

// Ensure this is attached to the TaggerAgent prefab/instance
public class TaggerAgent : Agent
{
    [Header("Movement Parameters")]
    public float MoveSpeed = 0.3f;
    public float TurnSpeed = 180f;
    public float JumpForce = 5f;
    private bool isGrounded = true;
    private float groundCheckDistance = 0.1f;

    [Header("Agent State")]
    public int FreezeBallsHeld = 0;

    // Internal variables
    private Rigidbody rb;
    [HideInInspector] // No need to assign manually now, but needs link
    public FreezeTagEnvController envController;

    // Penalty tracking
    private bool applyUnfreezePenalty = false;

    // Add fields to store actions between OnActionReceived and FixedUpdate
    private int forwardBackwardAction = 0;
    private int rotateAction = 0;
    private int jumpAction = 0;

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
        // envController is assigned by the EnvironmentController during spawn now.
    }

    public override void OnEpisodeBegin()
    {
        // State is reset by EnvController during spawn (position, rotation)
        ResetFreezeBalls();
        applyUnfreezePenalty = false;
    }

    public void ResetFreezeBalls() {
        FreezeBallsHeld = 0;
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        // Observations:
        sensor.AddObservation(FreezeBallsHeld);
        
        // Add normalized time remaining (0 to 1 value)
        float normalizedTimeRemaining = 0f;
        if (envController != null) {
            normalizedTimeRemaining = envController.GetNormalizedTimeRemaining();
        }
        sensor.AddObservation(normalizedTimeRemaining);
        
        // Add global team state
        if (envController != null) {
            // Observe percentage of runners currently frozen
            float percentFrozen = envController.GetPercentRunnersCurrentlyFrozen();
            sensor.AddObservation(percentFrozen);
            
            // Observe freeze ball distribution balance
            float teamFreezeBallBalance = envController.GetFreezeBallDistributionBalance();
            sensor.AddObservation(teamFreezeBallBalance);
        }
        
        // Raycasts added automatically if RayPerceptionSensor component is present
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- Read Actions ---
        forwardBackwardAction = actions.DiscreteActions[0]; // 0=none, 1=forward, 2=backward
        rotateAction = actions.DiscreteActions[1];          // 0=none, 1=right, 2=left
        jumpAction = actions.DiscreteActions[2];            // 0=none, 1=jump

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

    // --- Tagger Specific Methods (Collision, Trigger, Penalties etc.) ---

    void OnCollisionEnter(Collision collision)
    {
        // Freeze runners on collision IF tagger has a freeze ball
        if (FreezeBallsHeld > 0 && (collision.gameObject.CompareTag("runner") || collision.gameObject.CompareTag("BeingUnfreezed")))
        {
            RunnerAgent runner = collision.gameObject.GetComponent<RunnerAgent>();
            if (runner != null && !runner.IsFrozen) // Check if actually not frozen
            {
                FreezeRunner(runner);
            }
        }
        if (collision.gameObject.CompareTag("Ground"))
            isGrounded = true;
    }

    void OnTriggerEnter(Collider other)
    {
        // Collect freeze balls
        if (other.CompareTag("freezeball"))
        {
            CollectFreezeBall(other.gameObject);
        }
    }

    void CollectFreezeBall(GameObject ball)
    {
        FreezeBallsHeld++;
        //Debug.Log($"{name} collected freeze ball. Total: {FreezeBallsHeld}");
        AddReward(0.05f); // Decreased from 0.1f to 0.05f
        envController?.FreezeBallCollected(ball); // Tell controller (use null-conditional)
    }

    void FreezeRunner(RunnerAgent runner)
    {
        FreezeBallsHeld--;
        //Debug.Log($"{name} froze {runner.name}! Freeze balls left: {FreezeBallsHeld}");
        runner.FreezeAgent(true); // Specify that this freeze is by a tagger
        AddReward(0.5f); // Reward for freezing one runner (reduced from 1.0)
    }

    public void StartApplyingUnfreezePenalty() { applyUnfreezePenalty = true; }
    public void StopApplyingUnfreezePenalty() { applyUnfreezePenalty = false; }

    // Use FixedUpdate for physics-based movement and penalty application
    void FixedUpdate()
    {
        // Apply unfreezing penalty if needed
        if (applyUnfreezePenalty)
        {
            AddReward(-0.025f * Time.fixedDeltaTime); // Decreased from -0.05f to -0.025f per second
        }

        // Penalty for each second runners survive
        AddReward(-0.005f * Time.fixedDeltaTime); // Decreased from -0.01f to -0.005f per second

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
}