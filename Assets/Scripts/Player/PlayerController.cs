using System;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlayerController : MonoBehaviour
{
    
    [Header("Horizontal Movement Variables")]
    [SerializeField] private float maxGroundSpeed;
    [SerializeField] private float maxAirSpeed;
    [SerializeField] private float groundAcceleration;
    [SerializeField] private float airAcceleration;
    [SerializeField] private float crouchSpeedMult;
    
    private Vector3 _currentVelocity;
        
    private float _currentSpeed;
    private float _speedToAdd;
    private float _calculatedSpeed;
    
    [Header("Vertical Movement Variables")]
    [SerializeField] private float jumpSpeed;
    [SerializeField] private float gravityScale;
    
    private int _remainingJumps;
    private const int MaxJumps = 2;
    
    [Header("Drag Variables")]
    [SerializeField] private float currentToDesiredSpeedTime;
    [SerializeField] private float currentToDesiredSpeedTimeCrouchMultiplier;
    
    private float _desiredSpeedTimer;
    private float _desiredSpeed;
    
    [Header("GroundCheck Variables")]
    [SerializeField] private float playerHeight;
    [SerializeField] private LayerMask whatIsGround;

    [Header("WallRunning variables")] 
    [SerializeField] private LayerMask whatIsWall;
    [SerializeField] private float wallDetectionRange;
    [SerializeField] private float wallStickForce;
    [SerializeField] private float wallRunMaxDuration;
    [SerializeField] private float wallRunVerticalStartForce;
    [SerializeField] private float wallJumpForce;

    private RaycastHit _leftWallHit;
    private RaycastHit _rightWallHit;

    private Vector3 _previousWallRunNormal;
    private Vector3 _currentWallRunNormal;
    private Vector3 _currentWallRunFoward;
    
    private float _wallRunStartHeight;
    private float _wallRunTimer;

    private bool _walldetected;
    
    [Header("Input Variables")]
    [SerializeField] private float verticalLookSensitivity;
    [SerializeField] private float horizontalLookSensitivity;
    
    private Vector3 _movementInputVector;
    private Vector3 _lookInputVector;
    private bool _jumpInput;
    private bool _crouchInput;
    
    //General Variables
    private Rigidbody _rb;
    private PlayerCameraController _playerCameraController;
    private PlayerInput _playerInput;
    
    //CharacterStates
    private bool _grounded;
    private bool _applyGravity;
    private bool _wallRunning;
    
    
    private void Start()
    {
        _rb = GetComponent<Rigidbody>();
        _playerCameraController = GetComponentInChildren<PlayerCameraController>();
        _playerInput = GetComponent<PlayerInput>();


        Cursor.lockState = CursorLockMode.Confined;
        Cursor.visible = false;
        _applyGravity = true;
    }

    private void Update()
    {
        GetInputs();
    }
    
    private void GetInputs()
    {
        _lookInputVector = GetInput(_playerInput.actions["Look"]); 
        _movementInputVector = GetInput(_playerInput.actions["Move"]);
        if(_playerInput.actions["Jump"].WasPressedThisFrame()) _jumpInput = true;
        _crouchInput = _playerInput.actions["Crouch"].IsPressed();
    }
    
    private Vector2 GetInput(InputAction action)
    {
        return action.ReadValue<Vector2>();
    }

    private void FixedUpdate()
    {
        GroundCheck();
        GetCurrentVelocity();
        
        if (CheckWall() && !_grounded) WallRun();
        
        Move();
        
        Rotate();
    }
    
    
    private void GroundCheck()
    {
        Physics.Raycast(transform.position, transform.TransformDirection(Vector3.down), out RaycastHit hit, playerHeight / 2 + 0.05f);
        
        _grounded = hit.collider != null && hit.collider.gameObject.layer == LayerMask.NameToLayer("Ground");
        if (_grounded)
        {
            _remainingJumps = MaxJumps;
            _previousWallRunNormal = Vector3.up;
        }
    }
    
    private void Move()
    {
        _rb.angularVelocity = Vector3.zero;
        
        GetMoveVector();
        GetResultingSpeed(_grounded ? maxGroundSpeed : maxAirSpeed, _grounded ? groundAcceleration : airAcceleration);
        
        if (_jumpInput && _remainingJumps > 0) AddJumpVelocity();
        if (!_grounded && _applyGravity) ApplyGravity();
        
        _rb.linearVelocity = _currentVelocity;
        _jumpInput = false;
    }
    
    private void GetMoveVector()
    {
        _movementInputVector = transform.forward * _movementInputVector.y + transform.right * _movementInputVector.x;
    }

    private void GetCurrentVelocity()
    {
        _currentVelocity = _rb.linearVelocity;
    }

    private void GetResultingSpeed(float maxSpeed, float acceleration)
    {
        _currentSpeed = Vector3.Dot(_currentVelocity, _movementInputVector);
        
        _calculatedSpeed = _crouchInput switch
        {
            true => maxSpeed * crouchSpeedMult,
            false => maxSpeed
        };
        
        _speedToAdd = Mathf.Clamp(_calculatedSpeed - _currentSpeed, 0, acceleration * Time.fixedDeltaTime);
        
        _currentVelocity += _speedToAdd * _movementInputVector;
        
        ApplyGroundDrag();
    }

    private void ApplyGroundDrag()
    {
        _desiredSpeed = maxGroundSpeed * _movementInputVector.magnitude;

        if (!_grounded || _currentVelocity.magnitude <= _desiredSpeed)
        {
            _desiredSpeedTimer = 0;
            return;
        }
        
        var desiredSpeedX = _currentVelocity.x * _desiredSpeed / _currentVelocity.magnitude;
        var desiredSpeedZ = _currentVelocity.z * _desiredSpeed / _currentVelocity.magnitude;
 
        _currentVelocity.x = Mathf.Lerp(_currentVelocity.x, desiredSpeedX, _desiredSpeedTimer);
        _currentVelocity.z = Mathf.Lerp(_currentVelocity.z, desiredSpeedZ, _desiredSpeedTimer);
        
        var timerMult = currentToDesiredSpeedTimeCrouchMultiplier * Convert.ToInt32(_crouchInput) + 1;
        
        _desiredSpeedTimer += Time.fixedDeltaTime / (currentToDesiredSpeedTime * timerMult);
        
        if(Mathf.Approximately(_currentVelocity.magnitude, _desiredSpeed)) _desiredSpeedTimer = 0;
    }
    
    private void AddJumpVelocity()
    {
        _currentVelocity.y = Mathf.Max(_currentVelocity.y, 0);
        _currentVelocity += Vector3.up * (jumpSpeed * Time.fixedDeltaTime);
        
        _remainingJumps--;
        
        if (_wallRunning)
        {
            _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z);
            _currentVelocity += _currentWallRunNormal.normalized * wallJumpForce;
            StopWallRun();
        }
    }

    private void ApplyGravity()
    {
        _currentVelocity += Physics.gravity * (Time.fixedDeltaTime * gravityScale);
    }

    private void Rotate()
    {
        RotatePlayer(_lookInputVector.x * horizontalLookSensitivity);
        _playerCameraController.RotateCamera(-_lookInputVector.y * verticalLookSensitivity);
    }

    private void RotatePlayer(float angle)
    {
        transform.Rotate(0f, angle, 0f);
    }

    private bool CheckWall()
    {
        bool returnValue = false;
        
        _walldetected = Physics.Raycast(transform.position, transform.right, out _rightWallHit, wallDetectionRange, whatIsWall);
        _walldetected = Physics.Raycast(transform.position, -transform.right , out _leftWallHit, wallDetectionRange, whatIsWall);

        if(_rightWallHit.collider) returnValue = CheckWallNormals(_rightWallHit);
        if(_leftWallHit.collider) returnValue = CheckWallNormals(_leftWallHit);
        
        if (returnValue == false && _wallRunning) StopWallRun(); 
        
        return returnValue;
    }
    
    

    private bool CheckWallNormals(RaycastHit hit)
    {
        _currentWallRunNormal = hit.normal.normalized;
        _currentWallRunFoward = Vector3.Cross(_currentWallRunNormal, transform.up);
        return _currentWallRunNormal != _previousWallRunNormal.normalized;
    }

    private void WallRun()
    {
        if(_wallRunTimer == 0) StartWallRun();
        
        _rb.AddForce(-_currentWallRunNormal * wallStickForce, ForceMode.Force); 
        _wallRunTimer += Time.fixedDeltaTime;

        if(_wallRunTimer >= wallRunMaxDuration) StopWallRun();
    }

    private void StartWallRun()
    {
        _wallRunning = true;
        _rb.linearVelocity = new Vector3(_rb.linearVelocity.x, 0, _rb.linearVelocity.z);
        
        GetCurrentVelocity();
        
        if((transform.forward - _currentWallRunFoward).magnitude > (transform.forward + _currentWallRunFoward).magnitude)
            _currentWallRunFoward = -_currentWallRunFoward;
        
        _currentVelocity = _currentWallRunFoward * _currentVelocity.magnitude;
        _rb.AddForce(transform.up * wallRunVerticalStartForce, ForceMode.Force);
        
        _remainingJumps = 1;
    }

    private void StopWallRun()
    {
        _wallRunning = false;
        _previousWallRunNormal = _currentWallRunNormal;
        _wallRunTimer = 0;
        _remainingJumps = 1;
    }
}
