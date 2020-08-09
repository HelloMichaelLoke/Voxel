using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    public float walkSpeed = 5.0f;
    public float runSpeed = 10.0f;
    public float crouchSpeed = 2.5f;
    public float jumpHeight = 1.3f;

    [Header("Mouse Settings")]
    public Vector2 mouseSensivity = new Vector2(180.0f, 180.0f);

    [Header("Collider Size")]
    public float walkHeight = 1.73f;
    public float crouchHeight = 1.0f;

    [Header("References")]
    public Camera playerCamera;

    private float cameraRotation = 0.0f;
    private float speed = 0.0f;
    private Vector3 velocity = Vector3.zero;
    private Vector3 direction = Vector3.zero;

    private CharacterController characterController;

    private bool isCrouching = false;
    private bool isRunning = false;

    private float lastJumpTimer = 2.0f;
    private float lastGroundedTimer = 0.0f;

    private float jumpCooldown = 0.51f;
    private float groundedCooldown = 0.5f;

    private void Start()
    {
        // Mouse Cursor
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Set References
        this.characterController = this.GetComponent<CharacterController>();

        // Set Values
        this.characterController.height = this.walkHeight;
    }

    private void Update()
    {
        this.LookAround();
        this.ToggleCrouching();
        this.ToggleRunning();
        this.DetermineSpeed();

        Vector2 velocityHorizontal = new Vector2(this.velocity.x, this.velocity.z);

        this.direction = Vector3.zero;

        if (velocityHorizontal.magnitude < this.speed)
        {
            if (Input.GetKey(KeyCode.W))
                this.direction += this.transform.forward;
            if (Input.GetKey(KeyCode.S))
                this.direction -= this.transform.forward;
            if (Input.GetKey(KeyCode.A))
                this.direction -= this.transform.right;
            if (Input.GetKey(KeyCode.D))
                this.direction += this.transform.right;

            this.direction.Normalize();
            this.direction *= 150.0f;
        }

        if (this.lastJumpTimer <= this.jumpCooldown) 
            this.lastJumpTimer += Time.deltaTime;

        if (this.lastGroundedTimer <= this.groundedCooldown)
            this.lastGroundedTimer += Time.deltaTime;
        else
            if (this.characterController.isGrounded) this.lastGroundedTimer = 0.0f;

        // Jump if the player is grounded
        if (Input.GetKeyDown(KeyCode.Space) && this.lastJumpTimer > this.jumpCooldown && this.lastGroundedTimer < this.groundedCooldown)
        {
            this.lastJumpTimer = 0.0f;
            this.velocity.y = Mathf.Sqrt(this.jumpHeight * -2.0f * Physics.gravity.y);
        }

        this.characterController.Move(this.velocity * Time.deltaTime);
    }

    private void FixedUpdate()
    {
        Vector3 opposite = 10.0f * new Vector3(this.velocity.x, 0.0f, this.velocity.z);

        if (this.characterController.isGrounded)
        {
            this.velocity.y = -2.0f;
            this.velocity.x += direction.x * Time.fixedDeltaTime;
            this.velocity.z += direction.z * Time.fixedDeltaTime;
            this.velocity -= opposite * Time.fixedDeltaTime;
        }
        else
        {
            this.velocity.y += Physics.gravity.y * 3.0f * Time.fixedDeltaTime;
            this.velocity.x += 0.75f * direction.x * Time.fixedDeltaTime;
            this.velocity.z += 0.75f * direction.z * Time.fixedDeltaTime;
            this.velocity -= 0.5f * opposite * Time.fixedDeltaTime;
        }
    }

    public void Stop()
    {
        this.velocity = Vector3.zero;
    }

    private void LookAround()
    {
        // Rotate Player
        this.transform.Rotate(Vector3.up, Input.GetAxis("Mouse X") * this.mouseSensivity.x * Time.deltaTime);

        // Rotate Camera
        this.cameraRotation -= Input.GetAxis("Mouse Y") * this.mouseSensivity.y * Time.deltaTime;
        this.cameraRotation = Mathf.Clamp(this.cameraRotation, -90.0f, 90.0f);
        this.playerCamera.transform.localRotation = Quaternion.Euler(this.cameraRotation, 0.0f, 0.0f);
    }

    private void ToggleCrouching()
    {
        if (!this.isCrouching && Input.GetKey(KeyCode.LeftControl))
        {
            this.isCrouching = true;
            this.characterController.height = this.crouchHeight;
        }
        else if (this.isCrouching && !Input.GetKey(KeyCode.LeftControl))
        {
            this.isCrouching = false;
            this.characterController.height = this.walkHeight;
        }
    }

    private void ToggleRunning()
    {
        if (!this.isRunning && Input.GetKey(KeyCode.LeftShift))
        {
            this.isRunning = true;
        }
        else if (this.isRunning && !Input.GetKey(KeyCode.LeftShift))
        {
            this.isRunning = false;
        }
    }

    private void DetermineSpeed()
    {
        if (this.characterController.isGrounded)
        {
            this.speed = this.walkSpeed;
            if (this.isRunning) this.speed = this.runSpeed;
            if (this.isCrouching) this.speed = this.crouchSpeed;
        }
    }
}
