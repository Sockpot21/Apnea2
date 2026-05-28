// OuterLayerEnemy.cs
// Simple enemy: detect player, move toward them, attack outermost layer.
// Uses KinematicCharacterMotor to move within the KCC system.

using UnityEngine;
using System.Collections.Generic;
using KinematicCharacterController;

public class OuterLayerEnemy : MonoBehaviour, ICharacterController
{
    [Header("References")]
    [SerializeField] private KinematicCharacterMotor motor;
    [SerializeField] private HealthManager playerHealthManager;

    [Header("Detection")]
    [SerializeField] private float visionRange = 20f;
    [SerializeField] private float visionConeAngle = 90f;

    [Header("Attack")]
    [SerializeField] private float attackRange = 2f;
    [SerializeField] private float attackCooldown = 2f;
    [SerializeField] private float damagePerHit = 12f;
    [SerializeField] private DamageType damageType = DamageType.Slash;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;

    [Header("Target Body Parts")]
    [SerializeField] private List<BodyPart> targetBodyParts = new List<BodyPart>();

    // Runtime state
    private Transform _playerTransform;
    private bool _playerDetected = false;
    private float _lastAttackTime = -Mathf.Infinity;
    private Vector3 _moveDirection = Vector3.zero;
    private bool _initialized = false;

    private void OnEnable()
    {
        if (motor != null)
            motor.CharacterController = this;
    }

    private void Start()
    {
        // Auto-find motor if not assigned
        if (motor == null)
            motor = GetComponent<KinematicCharacterMotor>();

        // Find player transform
        var playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
            _playerTransform = playerGO.transform;

        // Auto-find health manager if not assigned
        if (playerHealthManager == null && _playerTransform != null)
            playerHealthManager = _playerTransform.GetComponent<HealthManager>();

        // Default target body parts
        if (targetBodyParts.Count == 0)
        {
            targetBodyParts = new List<BodyPart>
            {
                BodyPart.Head, BodyPart.Chest, BodyPart.Abdomen,
                BodyPart.LeftUpperArm, BodyPart.RightUpperArm,
                BodyPart.LeftForearm, BodyPart.RightForearm,
                BodyPart.LeftThigh, BodyPart.RightThigh,
                BodyPart.LeftShin, BodyPart.RightShin
            };
        }

        _initialized = (motor != null && _playerTransform != null && playerHealthManager != null);

        if (!_initialized)
            Debug.LogError($"[OuterLayerEnemy] {gameObject.name}: Failed to initialize. " +
                         $"Motor={motor != null}, Player={_playerTransform != null}, HealthManager={playerHealthManager != null}");
        else
            Debug.Log($"[OuterLayerEnemy] {gameObject.name}: Ready");
    }

    private void Update()
    {
        if (!_initialized || _playerTransform == null) return;

        // Calculate direction to player (horizontal only)
        Vector3 dirToPlayer = (_playerTransform.position - transform.position);
        Vector3 dirToPlayerHorizontal = new Vector3(dirToPlayer.x, 0f, dirToPlayer.z).normalized;

        // Always face player
        if (dirToPlayerHorizontal.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(dirToPlayerHorizontal);

        // Detect player
        _playerDetected = IsPlayerDetected();

        if (_playerDetected)
        {
            _moveDirection = dirToPlayerHorizontal;

            // Try attack if in range
            if (IsPlayerInAttackRange())
                TryAttack();
        }
        else
        {
            _moveDirection = Vector3.zero;
        }
    }

    private bool IsPlayerDetected()
    {
        if (_playerTransform == null) return false;

        Vector3 dirToPlayer = _playerTransform.position - transform.position;
        float distToPlayer = dirToPlayer.magnitude;

        if (distToPlayer > visionRange) return false;

        float angleToPlayer = Vector3.Angle(transform.forward, dirToPlayer);
        if (angleToPlayer > visionConeAngle * 0.5f) return false;

        return true;
    }

    private bool IsPlayerInAttackRange()
    {
        if (_playerTransform == null) return false;
        return Vector3.Distance(transform.position, _playerTransform.position) <= attackRange;
    }

    private void TryAttack()
    {
        float timeSinceLastAttack = Time.time - _lastAttackTime;
        if (timeSinceLastAttack < attackCooldown) return;

        _lastAttackTime = Time.time;

        if (playerHealthManager == null || targetBodyParts.Count == 0)
            return;

        // Pick random body part
        BodyPart targetPart = targetBodyParts[Random.Range(0, targetBodyParts.Count)];
        var bodyPart = playerHealthManager.GetBodyPart(targetPart);

        if (bodyPart == null || bodyPart.layers.Count == 0)
            return;

        // Find outermost non-destroyed layer
        RuntimeSubPart outerLayer = null;
        for (int i = 0; i < bodyPart.layers.Count; i++)
        {
            if (!bodyPart.layers[i].IsDestroyed)
            {
                outerLayer = bodyPart.layers[i];
                break;
            }
        }

        if (outerLayer == null) return;

        // Deal damage
        var damageEntries = new List<(DamageType, float)> { (damageType, damagePerHit) };
        playerHealthManager.ReceiveDamageOnPart(targetPart, damageEntries);

        Debug.Log($"[OuterLayerEnemy] ATTACK: {targetPart} → {outerLayer.displayName}");
    }

    // ── ICharacterController (minimal implementation) ────────────────────────

    public void BeforeCharacterUpdate(float deltaTime) { }
    public void PostGroundUpdate(float deltaTime) { }
    public void AfterCharacterUpdate(float deltaTime) { }
    public bool IsColliderValidForCollisions(Collider coll) => true;
    public void OnDiscreteCollisionDetected(Collider hitCollider) { }
    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }
    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }
    public void PostGroundingUpdate(float deltaTime) { }
    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport) { }

    public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
    {
        // Rotation handled in Update()
    }

    public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
        // Apply gravity
        if (!motor.GroundingStatus.IsStableOnGround)
        {
            currentVelocity.y += Physics.gravity.y * deltaTime;
        }
        else if (currentVelocity.y < 0f)
        {
            currentVelocity.y = 0f;
        }

        // Move toward player
        currentVelocity.x = _moveDirection.x * moveSpeed;
        currentVelocity.z = _moveDirection.z * moveSpeed;
    }
}
