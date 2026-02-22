using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(CubeController))]
public class CubeJumping : NetworkBehaviour
{
    [Header("Forces")]
    [Range(0.25f,4)]
    // default 1
    public float jumpForceMultiplier = 1f;
    public int upForce = 3;
    public int upTorque = 50;
    
    float _jumpTimer = 0;
    [SerializeField]
    bool _isCanFirstJump = false;
    bool _isJumping = false;
    [SerializeField]
    bool _isCanKeepJumping = false;

    Rigidbody _rb;
    CubeController _controller;
    InputManager _inputManager;

    // Exposed for server reconciliation state capture/restore
    public float jumpTimer { get => _jumpTimer; set => _jumpTimer = value; }
    public bool isJumping { get => _isJumping; set => _isJumping = value; }
    public bool isCanFirstJump { get => _isCanFirstJump; set => _isCanFirstJump = value; }
    public bool isCanKeepJumping { get => _isCanKeepJumping; set => _isCanKeepJumping = value; }

    void Start()
    {
        _rb = GetComponentInParent<Rigidbody>();
        _controller = GetComponent<CubeController>();
        _inputManager = GetComponent<InputManager>();
    }

    private void FixedUpdate()
    {
        Jump();
        JumpBackToTheFeet();
    }

    private void Jump()
    {
        if (_inputManager == null) return;

         // Determine jump direction: use world-up when car is flipped, local-up otherwise
        bool isFlipped = Vector3.Dot(Vector3.up, transform.up) < 0f;
        Vector3 jumpDir = isFlipped ? Vector3.up : transform.up;
 
        // Do initial jump impulse only once
        if (_inputManager.isJump && _isCanFirstJump)
        {
            _rb.AddForce(jumpDir * 240 / 100 * jumpForceMultiplier, ForceMode.VelocityChange);
            _isCanKeepJumping = true;
            _isCanFirstJump = false;
            _isJumping = true;
 
            _jumpTimer += Time.fixedDeltaTime;
        }
 
        // Keep jumping if the jump button is being pressed (shorter hold window)
        if (_inputManager.isJump && _isJumping && _isCanKeepJumping && _jumpTimer <= 0.15f)
        {
            _rb.AddForce(jumpDir * 1200f / 100 * jumpForceMultiplier, ForceMode.Acceleration);
            _jumpTimer += Time.fixedDeltaTime;
        }
 
        // If jump button was released we can't start jumping again mid air
        if (_inputManager.isJumpUp)
            _isCanKeepJumping = false;
 
        // Reset jump flags when landed (wheels on surface OR body on surface)
        bool isTouchingSurface = _controller.isAllWheelsSurface || _controller.isBodySurface;
        if (isTouchingSurface)
        {
            // Need a timer, otherwise while jumping we are setting isJumping flag to false right on the next frame
            if (_jumpTimer >= 0.1f)
                _isJumping = false;
 
            _jumpTimer = 0;
            _isCanFirstJump = true;
        }
        // Cant start jumping while in the air
        else if (!isTouchingSurface)
            _isCanFirstJump = false;
    }

    //Auto jump and rotate when the car is on the roof
    void JumpBackToTheFeet()
    {
        if (_controller.carState != CubeController.CarStates.BodyGroundDead) return;
        
        if (_inputManager.isJumpDown || Input.GetButtonDown("A"))
        {
            _rb.AddForce(Vector3.up * upForce, ForceMode.VelocityChange);
            _rb.AddTorque(transform.forward * upTorque, ForceMode.VelocityChange);
        }
    }
}