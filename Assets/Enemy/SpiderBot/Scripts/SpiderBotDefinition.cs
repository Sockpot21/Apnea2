using UnityEngine;

[CreateAssetMenu(fileName = "SpiderBotDefinition", menuName = "Enemies/Spider Bot Definition")]
public sealed class SpiderBotDefinition : ScriptableObject
{
    [Header("Identity")]
    public string enemyID = "prototype_spider_bot";
    public string displayName = "Prototype Spider Bot";

    [Header("Health")]
    [Min(1f)] public float maximumHealth = 260f;
    [Min(0f)] public float corpseImpulseMultiplier = 0.08f;

    [Header("Movement")]
    [Min(0f)] public float detectionRange = 36f;
    [Min(0f)] public float moveSpeed = 4.5f;
    [Min(0f)] public float turnResponse = 5f;
    [Min(0f)] public float preferredCombatRange = 4.2f;
    [Min(1f)] public float bodyMass = 58f;
    public Vector3 centreOfMassOffset = new(0f, -0.55f, 0f);
    [Min(0f)] public float yawTorque = 4f;

    [Header("Boid Steering")]
    [Min(0f)] public float neighbourRadius = 9f;
    [Min(0f)] public float separationWeight = 4.5f;
    [Min(0f)] public float cohesionWeight = 0.35f;
    [Min(0f)] public float alignmentWeight = 0.55f;
    [Min(0f)] public float obstacleProbeDistance = 5f;
    [Min(0f)] public float obstacleAvoidanceWeight = 4f;

    [Header("Legs")]
    [Min(0.1f)] public float stanceWidth = 3.55f;
    [Min(0.1f)] public float standingHeight = 2.55f;
    [Min(0.05f)] public float stepDistance = .8f;
    [Min(0.01f)] public float stepDuration = .34f;
    [Min(0f)] public float stepHeight = .9f;
    [Min(0f)] public float strideLead = 1.15f;
    [Min(0f)] public float footProbeHeight = 2.5f;
    [Min(0f)] public float footProbeDistance = 6f;
    [Min(0.1f)] public float upperLegMass = 2f;
    [Min(0.1f)] public float lowerLegMass = 1.5f;
    [Min(0.1f)] public float footMass = .6f;
    [Min(0f)] public float jointSpring = 6500f;
    [Min(0f)] public float jointDamper = 220f;
    [Min(0f)] public float jointMaximumForce = 25000f;
    [Min(0f)] public float legSupportSpring = 950f;
    [Min(0f)] public float legSupportDamper = 120f;
    [Min(0f)] public float legPropulsionSpring = 160f;
    [Min(0f)] public float legPropulsionDamper = 25f;
    [Min(1f)] public float maximumLegActuatorForce = 1400f;

    [Header("Combat Rhythm")]
    [Min(0f)] public float attackCooldown = 1.4f;
    [Min(0f)] public float telegraphDuration = 0.42f;

    [Header("Leg Stab")]
    [Min(0f)] public float stabRange = 5f;
    [Min(0f)] public float stabDamage = 18f;
    public DamageType stabDamageType = DamageType.Pierce;

    [Header("Physics Leap")]
    [Min(0f)] public float leapMinimumRange = 4.5f;
    [Min(0f)] public float leapRange = 14f;
    [Min(0f)] public float leapHorizontalSpeed = 13f;
    [Min(0f)] public float leapVerticalSpeed = 7.5f;
    [Min(0f)] public float leapDamage = 32f;
    [Min(0f)] public float leapImpactRange = 3.3f;
    [Min(0f)] public float leapCooldown = 4f;
    public DamageType leapDamageType = DamageType.Blunt;

    [Header("Burn Cone")]
    [Min(0f)] public float burnRange = 12f;
    [Range(1f, 179f)] public float burnConeAngle = 38f;
    [Min(0f)] public float burnDamagePerPulse = 8f;
    [Min(1)] public int burnPulses = 3;
    [Min(0.01f)] public float burnPulseInterval = 0.35f;
    [Min(0f)] public float burnCooldown = 6f;

    [Header("Presentation")]
    public Color shellColor = new(0.12f, 0.16f, 0.2f, 1f);
    public Color jointColor = new(0.28f, 0.34f, 0.4f, 1f);
    public Color eyeColor = new(1f, 0.08f, 0.02f, 1f);
    public Color burnColor = new(1f, 0.22f, 0.01f, 0.8f);
}
