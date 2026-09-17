using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(Rigidbody), typeof(BoxCollider))]
public sealed class SpiderBotController : MonoBehaviour
{
    private static readonly List<SpiderBotController> ActiveBots = new();
    private static readonly BodyPart[] AttackableParts =
    {
        BodyPart.Head, BodyPart.Chest, BodyPart.Abdomen,
        BodyPart.LeftUpperArm, BodyPart.RightUpperArm,
        BodyPart.LeftForearm, BodyPart.RightForearm,
        BodyPart.LeftThigh, BodyPart.RightThigh,
        BodyPart.LeftShin, BodyPart.RightShin
    };

    [SerializeField] private SpiderBotDefinition definition;
    [SerializeField] private Rigidbody body;
    [SerializeField] private ProceduralSpiderLegs legs;
    [SerializeField] private SpiderBotHealth health;
    [SerializeField] private Transform muzzle;
    [SerializeField] private LayerMask worldMask = ~0;

    private readonly RaycastHit[] _probeHits = new RaycastHit[16];
    private Transform _target;
    private HealthManager _targetHealth;
    private float _nextAttackTime;
    private float _nextBurnTime;
    private float _nextLeapTime;
    private bool _attacking;
    private bool _corpse;
    private ParticleSystem _burnParticles;

    public Vector3 PlanarVelocity => body == null
        ? Vector3.zero
        : Vector3.ProjectOnPlane(body.linearVelocity, Vector3.up);

    public void Configure(SpiderBotDefinition value, Rigidbody rigidbody,
        ProceduralSpiderLegs proceduralLegs, SpiderBotHealth botHealth, Transform burnMuzzle)
    {
        definition = value;
        body = rigidbody;
        legs = proceduralLegs;
        health = botHealth;
        muzzle = burnMuzzle;
    }

    private void Awake()
    {
        if (body == null) body = GetComponent<Rigidbody>();
        if (legs == null) legs = GetComponent<ProceduralSpiderLegs>();
        if (health == null) health = GetComponent<SpiderBotHealth>();
        body.mass = definition != null ? definition.bodyMass : 58f;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        body.linearDamping = .12f;
        body.angularDamping = .8f;
        body.maxAngularVelocity = 12f;
        body.solverIterations = 12;
        body.solverVelocityIterations = 5;
        if (definition != null) body.centerOfMass = definition.centreOfMassOffset;
        BoxCollider chassisCollider = GetComponent<BoxCollider>();
        if (chassisCollider != null)
        {
            chassisCollider.center = new Vector3(0f, .15f, 0f);
            chassisCollider.size = new Vector3(2.2f, 2.6f, 1.9f);
        }
        if (muzzle != null) muzzle.localPosition = new Vector3(0f, -.35f, 1.22f);
        BuildBurnParticles();
    }

    private void OnEnable()
    {
        if (!ActiveBots.Contains(this)) ActiveBots.Add(this);
    }

    private void OnDisable()
    {
        ActiveBots.Remove(this);
    }

    private void Start() => ResolveTarget();

    private void ResolveTarget()
    {
        PlayerCharacter player = FindAnyObjectByType<PlayerCharacter>();
        if (player == null) return;
        _target = player.transform;
        _targetHealth = player.GetComponent<HealthManager>()
            ?? player.GetComponentInParent<HealthManager>()
            ?? player.GetComponentInChildren<HealthManager>();
    }

    private void Update()
    {
        if (_corpse || definition == null) return;
        if (_target == null || _targetHealth == null) ResolveTarget();
        if (_target == null || _targetHealth == null || _attacking) return;
        if (legs != null && !legs.IsSupported) return;

        Vector3 offset = _target.position - transform.position;
        float distance = Vector3.ProjectOnPlane(offset, Vector3.up).magnitude;
        if (distance > definition.detectionRange || Time.time < _nextAttackTime) return;

        if (distance <= definition.burnRange && Time.time >= _nextBurnTime
            && Random.value < .32f)
            StartCoroutine(BurnAttack());
        else if (distance <= definition.stabRange && Random.value < .58f)
            StartCoroutine(StabAttack());
        else if (distance >= definition.leapMinimumRange && distance <= definition.leapRange
            && Time.time >= _nextLeapTime)
            StartCoroutine(LeapAttack());
    }

    private void FixedUpdate()
    {
        if (_corpse || definition == null || body == null) return;
        if (_target == null)
        {
            legs?.SetLocomotion(Vector3.zero, transform.forward);
            return;
        }

        Vector3 toTarget = Vector3.ProjectOnPlane(_target.position - transform.position, Vector3.up);
        float distance = toTarget.magnitude;
        if (distance > definition.detectionRange)
        {
            legs?.SetLocomotion(Vector3.zero, transform.forward);
            return;
        }
        Vector3 targetDirection = distance > .01f ? toTarget / distance : transform.forward;
        float rangeBias = distance > definition.preferredCombatRange ? 1f : -.25f;
        Vector3 steering = targetDirection * rangeBias + CalculateBoidSteering()
            + CalculateObstacleAvoidance(targetDirection);
        if (steering.sqrMagnitude > 1f) steering.Normalize();

        float speedScale = _attacking ? .22f : 1f;
        Vector3 desiredVelocity = steering * definition.moveSpeed * speedScale;
        legs?.SetLocomotion(desiredVelocity, targetDirection);

        if (targetDirection.sqrMagnitude > .01f && (legs == null || legs.IsSupported))
        {
            Vector3 facing = _attacking ? targetDirection : Vector3.Slerp(transform.forward,
                steering.sqrMagnitude > .01f ? steering : targetDirection, .65f);
            float signed = Vector3.SignedAngle(transform.forward, facing, Vector3.up);
            body.AddTorque(Vector3.up * (signed * Mathf.Deg2Rad
                * definition.turnResponse * definition.yawTorque), ForceMode.Acceleration);
        }
    }

    private Vector3 CalculateBoidSteering()
    {
        Vector3 separation = Vector3.zero;
        Vector3 centre = Vector3.zero;
        Vector3 alignment = Vector3.zero;
        int neighbours = 0;
        float radiusSquared = definition.neighbourRadius * definition.neighbourRadius;
        foreach (SpiderBotController other in ActiveBots)
        {
            if (other == this || other == null || other._corpse) continue;
            Vector3 offset = Vector3.ProjectOnPlane(transform.position - other.transform.position, Vector3.up);
            float squared = offset.sqrMagnitude;
            if (squared <= .001f || squared > radiusSquared) continue;
            separation += offset.normalized / Mathf.Max(.25f, Mathf.Sqrt(squared));
            centre += other.transform.position;
            alignment += other.PlanarVelocity;
            neighbours++;
        }
        if (neighbours == 0) return Vector3.zero;
        centre /= neighbours;
        alignment /= neighbours;
        Vector3 cohesion = Vector3.ProjectOnPlane(centre - transform.position, Vector3.up).normalized;
        return separation * definition.separationWeight
            + cohesion * definition.cohesionWeight
            + alignment.normalized * definition.alignmentWeight;
    }

    private Vector3 CalculateObstacleAvoidance(Vector3 forward)
    {
        if (forward.sqrMagnitude <= .01f) return Vector3.zero;
        Vector3 origin = transform.position + Vector3.up * .2f;
        int count = Physics.SphereCastNonAlloc(origin, .8f, forward, _probeHits,
            definition.obstacleProbeDistance, worldMask, QueryTriggerInteraction.Ignore);
        RaycastHit nearestHit = default;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = _probeHits[i];
            if (candidate.collider == null || candidate.collider.transform.IsChildOf(transform)
                || (legs != null && legs.OwnsCollider(candidate.collider))
                || candidate.distance >= nearestDistance) continue;
            nearestHit = candidate;
            nearestDistance = candidate.distance;
        }
        if (nearestHit.collider == null) return Vector3.zero;
        return Vector3.ProjectOnPlane(nearestHit.normal, Vector3.up).normalized
            * definition.obstacleAvoidanceWeight;
    }

    private IEnumerator StabAttack()
    {
        BeginAttack();
        legs?.PlayStab(_target.position + Vector3.up, definition.telegraphDuration * 2f);
        yield return new WaitForSeconds(definition.telegraphDuration);
        if (TargetWithin(definition.stabRange))
            DamagePlayer(definition.stabDamageType, definition.stabDamage, "leg stab");
        EndAttack();
    }

    private IEnumerator LeapAttack()
    {
        BeginAttack();
        _nextLeapTime = Time.time + definition.leapCooldown;
        legs?.SetLocomotion(Vector3.zero, _target.position - transform.position);
        legs?.SetBraced(true);
        yield return new WaitForSeconds(definition.telegraphDuration);
        Vector3 direction = Vector3.ProjectOnPlane(_target.position - transform.position, Vector3.up).normalized;
        legs?.SetBraced(false);
        legs?.SetAirborneTuck(1f);
        body.AddForce(direction * definition.leapHorizontalSpeed
            + Vector3.up * definition.leapVerticalSpeed, ForceMode.VelocityChange);

        bool dealtDamage = false;
        float impactWindowEnd = Time.time + 1.15f;
        while (Time.time < impactWindowEnd)
        {
            if (!dealtDamage && TargetWithin(definition.leapImpactRange))
            {
                DamagePlayer(definition.leapDamageType, definition.leapDamage, "physics leap");
                dealtDamage = true;
            }
            yield return new WaitForFixedUpdate();
        }

        legs?.SetAirborneTuck(0f);
        float recoveryEnd = Time.time + 1.2f;
        while (legs != null && !legs.IsSupported && Time.time < recoveryEnd)
            yield return new WaitForFixedUpdate();
        EndAttack();
    }

    private IEnumerator BurnAttack()
    {
        BeginAttack();
        _nextBurnTime = Time.time + definition.burnCooldown;
        if (_burnParticles != null) _burnParticles.Play();
        yield return new WaitForSeconds(definition.telegraphDuration);
        for (int i = 0; i < definition.burnPulses; i++)
        {
            if (TargetInsideBurnCone())
                DamagePlayer(DamageType.Burn, definition.burnDamagePerPulse, "burn cone");
            yield return new WaitForSeconds(definition.burnPulseInterval);
        }
        if (_burnParticles != null) _burnParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        EndAttack();
    }

    private void BeginAttack()
    {
        _attacking = true;
        _nextAttackTime = Time.time + definition.attackCooldown;
    }

    private void EndAttack() => _attacking = false;

    private bool TargetWithin(float range) => _target != null
        && Vector3.Distance(transform.position, _target.position) <= range;

    private bool TargetInsideBurnCone()
    {
        if (_target == null) return false;
        Vector3 origin = muzzle != null ? muzzle.position : transform.position;
        Vector3 toTarget = _target.position + Vector3.up - origin;
        return toTarget.magnitude <= definition.burnRange
            && Vector3.Angle(transform.forward, toTarget) <= definition.burnConeAngle * .5f;
    }

    private void DamagePlayer(DamageType type, float amount, string attackName)
    {
        if (_targetHealth == null) return;
        BodyPart part = AttackableParts[Random.Range(0, AttackableParts.Length)];
        _targetHealth.ReceiveDamageOnPart(part,
            new List<(DamageType type, float amount)> { (type, amount) });
        Debug.Log($"[SpiderBot] {attackName} hit {part}: {amount:F1} {type}");
    }

    private void BuildBurnParticles()
    {
        if (muzzle == null || definition == null) return;
        GameObject particles = new("BurnConeParticles", typeof(ParticleSystem));
        particles.transform.SetParent(muzzle, false);
        particles.transform.localRotation = Quaternion.identity;
        _burnParticles = particles.GetComponent<ParticleSystem>();
        var main = _burnParticles.main;
        main.loop = true;
        main.startLifetime = .45f;
        main.startSpeed = definition.burnRange * 1.6f;
        main.startSize = .5f;
        main.startColor = definition.burnColor;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = _burnParticles.emission;
        emission.rateOverTime = 45f;
        var shape = _burnParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = definition.burnConeAngle * .5f;
        shape.radius = .15f;
        _burnParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public void EnterCorpseState()
    {
        if (_corpse) return;
        _corpse = true;
        StopAllCoroutines();
        legs?.SetBraced(false);
        legs?.SetAirborneTuck(0f);
        if (_burnParticles != null)
            _burnParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ActiveBots.Remove(this);
        enabled = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (definition == null) return;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, definition.stabRange);
        Gizmos.color = new Color(1f, .25f, 0f, 1f);
        Gizmos.DrawWireSphere(transform.position, definition.burnRange);
    }
}
