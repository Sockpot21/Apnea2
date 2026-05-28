using System.Collections.Generic;
using KinematicCharacterController;
using UnityEngine;

public class EnemyCharacter : MonoBehaviour, ICharacterController
{
    [Header("References")]
    [SerializeField] private KinematicCharacterMotor motor;
    [SerializeField] private Transform target;
    [SerializeField] private HealthManager targetHealth;

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 8f;
    [SerializeField] private float airSpeed = 12f;
    [SerializeField] private float airAcceleration = 50f;
    [SerializeField] private float gravity = -90f;

    [Header("Combat")]
    [SerializeField] private float detectionRange = 30f;
    [SerializeField] private float stoppingDistance = 1.7f;
    [SerializeField] private float attackCooldown = 1.35f;
    [SerializeField] private float slashDamage = 12f;

    [SerializeField]
    private List<BodyPart> targetableLimbs = new List<BodyPart>();

    private float nextAttackTime;

    private void Start()
    {
        if (motor == null)
            motor = GetComponent<KinematicCharacterMotor>();

        if (motor != null)
            motor.CharacterController = this;
        else
            Debug.LogError("[EnemyCharacter] Missing KinematicCharacterMotor!");

        if (targetableLimbs.Count == 0)
        {
            targetableLimbs.AddRange(new[] {
                BodyPart.Chest, BodyPart.Abdomen, BodyPart.Head,
                BodyPart.LeftUpperArm, BodyPart.RightUpperArm,
                BodyPart.LeftThigh, BodyPart.RightThigh
            });
        }

        AutoAssignTarget();
    }

    private void AutoAssignTarget()
    {
        if (target != null && targetHealth != null) return;

        GameObject playerObj = GameObject.FindWithTag("Player");
        if (playerObj == null)
        {
            Debug.LogError("[EnemyCharacter] Player with tag 'Player' not found!");
            return;
        }

        target = playerObj.transform;
        targetHealth = playerObj.GetComponentInChildren<HealthManager>(true);
    }

    private bool HasValidTarget() => target != null && targetHealth != null && target.gameObject.activeInHierarchy;

    // KCC Callbacks
    public void BeforeCharacterUpdate(float deltaTime) { }
    public void AfterCharacterUpdate(float deltaTime) { }
    public void PostGroundingUpdate(float deltaTime) { }
    public bool IsColliderValidForCollisions(Collider coll) => true;
    public void OnDiscreteCollisionDetected(Collider hitCollider) { }
    public void OnGroundHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }
    public void OnMovementHit(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, ref HitStabilityReport hitStabilityReport) { }
    public void ProcessHitStabilityReport(Collider hitCollider, Vector3 hitNormal, Vector3 hitPoint, Vector3 atCharacterPosition, Quaternion atCharacterRotation, ref HitStabilityReport hitStabilityReport) { }

    public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
    {
        if (!HasValidTarget()) return;

        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude < 0.001f) return;

        Quaternion targetRot = Quaternion.LookRotation(dir.normalized, motor.CharacterUp);
        currentRotation = Quaternion.Slerp(currentRotation, targetRot, 1f - Mathf.Exp(-20f * deltaTime));
    }

    public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
    {
        if (!HasValidTarget())
        {
            currentVelocity = Vector3.zero;
            return;
        }

        Vector3 myPos = transform.position;
        Vector3 targetPos = target.position;
        float distance = Vector3.Distance(myPos, targetPos);

        if (distance > detectionRange)
        {
            currentVelocity = Vector3.zero;
            return;
        }

        // Direction to target (flat)
        Vector3 direction = (targetPos - myPos).normalized;
        direction.y = 0f;

        if (motor.GroundingStatus.IsStableOnGround)
        {
            if (distance > stoppingDistance)
            {
                Vector3 targetVel = direction * moveSpeed;
                currentVelocity = Vector3.Lerp(currentVelocity, targetVel, 1f - Mathf.Exp(-18f * deltaTime));
            }
            else
            {
                currentVelocity.x = 0;
                currentVelocity.z = 0;
                TryAttack();
            }
        }
        else
        {
            // Air movement
            if (direction.sqrMagnitude > 0f)
            {
                Vector3 planarMovement = Vector3.ProjectOnPlane(direction, motor.CharacterUp) * direction.magnitude;
                Vector3 currentPlanar = Vector3.ProjectOnPlane(currentVelocity, motor.CharacterUp);

                Vector3 movementForce = planarMovement * airAcceleration * deltaTime;

                if (currentPlanar.magnitude < airSpeed)
                {
                    Vector3 targetPlanar = currentPlanar + movementForce;
                    targetPlanar = Vector3.ClampMagnitude(targetPlanar, airSpeed);
                    movementForce = targetPlanar - currentPlanar;
                }
                else if (Vector3.Dot(currentPlanar, movementForce) > 0)
                {
                    movementForce = Vector3.ProjectOnPlane(movementForce, currentPlanar.normalized);
                }

                currentVelocity += movementForce;
            }

            // Gravity
            currentVelocity += motor.CharacterUp * gravity * deltaTime;
        }
    }

    private void TryAttack()
    {
        if (Time.time < nextAttackTime) return;
        nextAttackTime = Time.time + attackCooldown;

        if (targetableLimbs.Count == 0) return;

        BodyPart part = targetableLimbs[Random.Range(0, targetableLimbs.Count)];
        var damage = new List<(DamageType, float)> { (DamageType.Slash, slashDamage) };

        targetHealth?.ReceiveDamageOnPart(part, damage);
        Debug.Log($"[Enemy] Melee hit on {part} for {slashDamage} Slash damage");
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, detectionRange);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, stoppingDistance);
    }
}