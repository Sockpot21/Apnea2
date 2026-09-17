using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Six-leg active-ragdoll rig. Every visible limb is a Rigidbody connected by
/// powered joints. Foot collisions and joint reaction forces carry the chassis;
/// no animation transform is used to hold the body above the floor.
/// </summary>
[DisallowMultipleComponent]
public sealed class ProceduralSpiderLegs : MonoBehaviour
{
    private sealed class Leg
    {
        public Rigidbody upper;
        public Rigidbody lower;
        public Rigidbody foot;
        public ConfigurableJoint hipJoint;
        public ConfigurableJoint kneeJoint;
        public ConfigurableJoint ankleJoint;
        public SpiderBotFootContact contact;
        public Vector3 hipAnchorLocal;
        public Vector3 restLocal;
        public Vector3 plantedTarget;
        public Vector3 stepFrom;
        public Vector3 stepTo;
        public Quaternion hipStartLocalRotation;
        public Quaternion kneeStartLocalRotation;
        public Quaternion ankleStartLocalRotation;
        public float stepProgress = 1f;
        public int tripod;
    }

    [SerializeField] private SpiderBotDefinition definition;
    [SerializeField] private Material shellMaterial;
    [SerializeField] private Material jointMaterial;
    [SerializeField] private Material eyeMaterial;
    [SerializeField] private PhysicsMaterial limbPhysicsMaterial;
    [SerializeField] private PhysicsMaterial footPhysicsMaterial;
    [SerializeField] private LayerMask groundMask = ~0;
    [SerializeField, Min(.05f)] private float segmentThickness = .24f;

    private readonly List<Leg> _legs = new();
    private readonly List<Collider> _ownColliders = new();
    private readonly RaycastHit[] _groundHits = new RaycastHit[16];
    private Rigidbody _body;
    private SpiderBotHealth _health;
    private Vector3 _desiredVelocity;
    private Vector3 _desiredFacing;
    private bool _ragdolled;
    private bool _braced;
    private float _airborneTuck;
    private int _attackLeg = -1;
    private float _attackStart;
    private float _attackEnd;
    private Vector3 _attackTarget;

    public int GroundedFootCount
    {
        get
        {
            int count = 0;
            foreach (Leg leg in _legs)
                if (leg.contact != null && leg.contact.IsGrounded) count++;
            return count;
        }
    }

    public bool IsSupported => GroundedFootCount >= 3;

    public bool OwnsCollider(Collider candidate) => candidate != null
        && _ownColliders.Contains(candidate);

    public void Configure(SpiderBotDefinition value, Material shell, Material joints,
        PhysicsMaterial limbMaterial = null, PhysicsMaterial footMaterial = null,
        Material eyes = null)
    {
        definition = value;
        shellMaterial = shell;
        jointMaterial = joints;
        limbPhysicsMaterial = limbMaterial;
        footPhysicsMaterial = footMaterial;
        eyeMaterial = eyes;
    }

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        _health = GetComponent<SpiderBotHealth>();
        CreateFallbackPhysicsMaterials();
        BuildRuntimeAppearance();
        if (_legs.Count == 0) BuildPhysicsRig();
    }

    private void Start()
    {
        foreach (Leg leg in _legs)
        {
            Vector3 desired = RestPointWorld(leg);
            leg.plantedTarget = ProbeGround(desired, out Vector3 ground) ? ground : desired;
            leg.stepFrom = leg.stepTo = leg.plantedTarget;
        }
    }

    private void FixedUpdate()
    {
        if (_ragdolled || definition == null || _body == null) return;
        for (int i = 0; i < _legs.Count; i++) DriveLeg(i, Time.fixedDeltaTime);
        ApplyPhysicalLegForces();
    }

    private void OnDestroy()
    {
        foreach (Leg leg in _legs)
        {
            if (leg.upper != null) Destroy(leg.upper.gameObject);
            if (leg.lower != null) Destroy(leg.lower.gameObject);
            if (leg.foot != null) Destroy(leg.foot.gameObject);
        }
    }

    public void SetLocomotion(Vector3 desiredWorldVelocity, Vector3 desiredWorldFacing)
    {
        _desiredVelocity = Vector3.ProjectOnPlane(desiredWorldVelocity, Vector3.up);
        _desiredFacing = Vector3.ProjectOnPlane(desiredWorldFacing, Vector3.up).normalized;
    }

    public void SetBraced(bool braced) => _braced = braced;

    public void SetAirborneTuck(float amount) => _airborneTuck = Mathf.Clamp01(amount);

    public void PlayStab(Vector3 worldTarget, float duration)
    {
        if (_ragdolled || _legs.Count == 0) return;
        float closest = float.PositiveInfinity;
        for (int i = 0; i < _legs.Count; i++)
        {
            float distance = (_legs[i].upper.position - worldTarget).sqrMagnitude;
            if (distance >= closest) continue;
            closest = distance;
            _attackLeg = i;
        }
        _attackStart = Time.time;
        _attackEnd = Time.time + Mathf.Max(.1f, duration);
        _attackTarget = worldTarget;
    }

    private void DriveLeg(int index, float deltaTime)
    {
        Leg leg = _legs[index];
        Vector3 footTarget;
        if (index == _attackLeg && Time.time < _attackEnd)
        {
            float phase = Mathf.InverseLerp(_attackStart, _attackEnd, Time.time);
            float thrust = phase < .5f ? phase * 2f : (1f - phase) * 2f;
            footTarget = Vector3.Lerp(leg.plantedTarget, _attackTarget, thrust)
                + Vector3.up * Mathf.Sin(thrust * Mathf.PI) * .3f;
        }
        else
        {
            if (index == _attackLeg) _attackLeg = -1;
            Vector3 desired = RestPointWorld(leg);
            if (ProbeGround(desired, out Vector3 ground)) desired = ground;
            bool wantsStep = Vector3.Distance(leg.plantedTarget, desired) > definition.stepDistance;
            if (leg.stepProgress >= 1f && wantsStep && CanTripodStep(leg.tripod))
            {
                leg.stepFrom = leg.foot.position;
                leg.stepTo = desired;
                leg.stepProgress = 0f;
            }

            if (leg.stepProgress < 1f)
            {
                leg.stepProgress = Mathf.Min(1f, leg.stepProgress
                    + deltaTime / Mathf.Max(.02f, definition.stepDuration));
                float eased = leg.stepProgress * leg.stepProgress * (3f - 2f * leg.stepProgress);
                footTarget = Vector3.Lerp(leg.stepFrom, leg.stepTo, eased)
                    + Vector3.up * (Mathf.Sin(eased * Mathf.PI) * definition.stepHeight);
                if (leg.stepProgress >= 1f) leg.plantedTarget = leg.stepTo;
            }
            else
            {
                footTarget = leg.plantedTarget;
            }
        }

        if (_airborneTuck > 0f)
        {
            Vector3 tucked = _body.position + _body.rotation
                * new Vector3(Mathf.Sign(leg.restLocal.x) * 1.45f, -1.05f, leg.restLocal.z * .45f);
            footTarget = Vector3.Lerp(footTarget, tucked, _airborneTuck);
        }
        else if (_braced)
        {
            footTarget += Vector3.down * .25f;
        }

        PoseMotors(leg, footTarget);
    }

    private Vector3 RestPointWorld(Leg leg)
    {
        Quaternion yaw = Quaternion.Euler(0f, _body.rotation.eulerAngles.y, 0f);
        Vector3 localLead = Quaternion.Inverse(yaw) * _desiredVelocity;
        if (localLead.sqrMagnitude > .01f)
        {
            float speedRatio = definition.moveSpeed > .01f
                ? Mathf.Clamp01(localLead.magnitude / definition.moveSpeed)
                : 0f;
            localLead = localLead.normalized * definition.strideLead * speedRatio;
        }
        return _body.position + yaw * (leg.restLocal + localLead);
    }

    private bool CanTripodStep(int tripod)
    {
        foreach (Leg other in _legs)
            if (other.tripod != tripod && other.stepProgress < 1f) return false;
        return true;
    }

    private void PoseMotors(Leg leg, Vector3 footTarget)
    {
        Vector3 hip = _body.transform.TransformPoint(leg.hipAnchorLocal);
        Vector3 outward = Vector3.ProjectOnPlane(footTarget - _body.position, Vector3.up).normalized;
        Vector3 knee = Vector3.Lerp(hip, footTarget, .48f) + outward * .72f + Vector3.up * .62f;

        Quaternion upperWorld = SegmentRotation(hip, knee);
        Quaternion lowerWorld = SegmentRotation(knee, footTarget);
        Quaternion footWorld = Quaternion.LookRotation(
            _desiredFacing.sqrMagnitude > .01f ? _desiredFacing : _body.transform.forward,
            Vector3.up);

        SetJointTarget(leg.hipJoint, upperWorld, _body.rotation, leg.hipStartLocalRotation);
        SetJointTarget(leg.kneeJoint, lowerWorld, leg.upper.rotation, leg.kneeStartLocalRotation);
        SetJointTarget(leg.ankleJoint, footWorld, leg.lower.rotation, leg.ankleStartLocalRotation);
    }

    private void ApplyPhysicalLegForces()
    {
        int groundedFeet = GroundedFootCount;
        if (groundedFeet == 0) return;

        Quaternion yaw = Quaternion.Euler(0f, _body.rotation.eulerAngles.y, 0f);
        float desiredHipHeight = definition.standingHeight - .89f;
        float weightShare = _body.mass * Physics.gravity.magnitude
            / Mathf.Max(3, groundedFeet);

        foreach (Leg leg in _legs)
        {
            if (leg.contact == null || !leg.contact.IsGrounded) continue;
            Vector3 hip = _body.transform.TransformPoint(leg.hipAnchorLocal);
            Vector3 foot = leg.foot.worldCenterOfMass;
            Vector3 relativeVelocity = _body.GetPointVelocity(hip)
                - leg.foot.GetPointVelocity(foot);

            float height = Vector3.Dot(hip - foot, Vector3.up);
            float supportMagnitude = weightShare
                + (desiredHipHeight - height) * definition.legSupportSpring
                - Vector3.Dot(relativeVelocity, Vector3.up) * definition.legSupportDamper;
            supportMagnitude = Mathf.Clamp(supportMagnitude, 0f,
                definition.maximumLegActuatorForce);

            Vector3 restOffset = yaw * new Vector3(leg.restLocal.x, 0f, leg.restLocal.z);
            Vector3 desiredBodyPosition = foot - restOffset;
            Vector3 planarError = Vector3.ProjectOnPlane(
                desiredBodyPosition - _body.worldCenterOfMass, Vector3.up);
            Vector3 planarVelocity = Vector3.ProjectOnPlane(relativeVelocity, Vector3.up);
            Vector3 traction = planarError * definition.legPropulsionSpring
                - planarVelocity * definition.legPropulsionDamper;
            traction = Vector3.ClampMagnitude(traction,
                definition.maximumLegActuatorForce * .55f);

            Vector3 actuatorForce = Vector3.up * supportMagnitude + traction;
            _body.AddForceAtPosition(actuatorForce, hip, ForceMode.Force);
            leg.foot.AddForceAtPosition(-actuatorForce, foot, ForceMode.Force);
        }
    }

    private static void SetJointTarget(ConfigurableJoint joint, Quaternion desiredWorld,
        Quaternion connectedWorld, Quaternion startLocal)
    {
        Quaternion desiredLocal = Quaternion.Inverse(connectedWorld) * desiredWorld;
        Quaternion jointSpace = Quaternion.LookRotation(joint.axis, joint.secondaryAxis);
        joint.targetRotation = Quaternion.Inverse(jointSpace)
            * Quaternion.Inverse(desiredLocal) * startLocal
            * jointSpace;
    }

    private static Quaternion SegmentRotation(Vector3 a, Vector3 b)
    {
        Vector3 direction = b - a;
        return direction.sqrMagnitude > .0001f
            ? Quaternion.FromToRotation(Vector3.up, direction.normalized)
            : Quaternion.identity;
    }

    private bool ProbeGround(Vector3 desired, out Vector3 point)
    {
        Vector3 origin = desired + Vector3.up * definition.footProbeHeight;
        int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits,
            definition.footProbeDistance, groundMask, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        point = desired;
        bool found = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = _groundHits[i];
            if (hit.collider == null || _ownColliders.Contains(hit.collider)
                || hit.distance >= nearest) continue;
            nearest = hit.distance;
            point = hit.point + hit.normal * .24f;
            found = true;
        }
        return found;
    }

    private void BuildPhysicsRig()
    {
        Collider bodyCollider = GetComponent<Collider>();
        if (bodyCollider != null) _ownColliders.Add(bodyCollider);
        float[] z = { 1.05f, 0f, -1.05f };
        float[] footZ = { 1.8f, 0f, -1.8f };
        for (int side = -1; side <= 1; side += 2)
        {
            for (int row = 0; row < 3; row++)
            {
                Vector3 hipLocal = new(side * 1.05f, -.65f, z[row]);
                Vector3 restLocal = new(side * definition.stanceWidth,
                    -definition.standingHeight + .24f, footZ[row]);
                Vector3 hipWorld = transform.TransformPoint(hipLocal);
                Vector3 footWorld = transform.TransformPoint(restLocal);
                Vector3 outward = Vector3.ProjectOnPlane(footWorld - transform.position, Vector3.up).normalized;
                Vector3 kneeWorld = Vector3.Lerp(hipWorld, footWorld, .46f)
                    + outward * .78f + Vector3.up * .55f;

                Rigidbody upper = CreateSegment($"{SideName(side)}_{row}_Upper",
                    hipWorld, kneeWorld, definition.upperLegMass, shellMaterial, limbPhysicsMaterial);
                Rigidbody lower = CreateSegment($"{SideName(side)}_{row}_Lower",
                    kneeWorld, footWorld, definition.lowerLegMass, jointMaterial, limbPhysicsMaterial);
                Rigidbody foot = CreateFoot($"{SideName(side)}_{row}_Foot", footWorld);

                ConfigurableJoint hipJoint = CreatePoweredJoint(upper, _body, hipWorld);
                ConfigurableJoint kneeJoint = CreatePoweredJoint(lower, upper, kneeWorld);
                ConfigurableJoint ankleJoint = CreatePoweredJoint(foot, lower, footWorld);
                Leg leg = new()
                {
                    upper = upper,
                    lower = lower,
                    foot = foot,
                    hipJoint = hipJoint,
                    kneeJoint = kneeJoint,
                    ankleJoint = ankleJoint,
                    contact = foot.GetComponent<SpiderBotFootContact>(),
                    hipAnchorLocal = hipLocal,
                    restLocal = restLocal,
                    plantedTarget = footWorld,
                    stepFrom = footWorld,
                    stepTo = footWorld,
                    tripod = (row + (side > 0 ? 1 : 0)) % 2,
                    hipStartLocalRotation = Quaternion.Inverse(_body.rotation) * upper.rotation,
                    kneeStartLocalRotation = Quaternion.Inverse(upper.rotation) * lower.rotation,
                    ankleStartLocalRotation = Quaternion.Inverse(lower.rotation) * foot.rotation
                };
                _legs.Add(leg);
            }
        }
        IgnoreSelfCollisions();
    }

    private void CreateFallbackPhysicsMaterials()
    {
        if (limbPhysicsMaterial == null)
        {
            limbPhysicsMaterial = new PhysicsMaterial("Runtime Spider Limb")
            {
                staticFriction = .08f,
                dynamicFriction = .05f,
                bounciness = 0f
            };
        }
        if (footPhysicsMaterial == null)
        {
            footPhysicsMaterial = new PhysicsMaterial("Runtime Spider Foot")
            {
                staticFriction = 1.25f,
                dynamicFriction = 1.15f,
                bounciness = 0f
            };
        }
    }

    private void BuildRuntimeAppearance()
    {
        if (transform.Find("Tall Industrial Chassis") != null) return;
        if (eyeMaterial == null)
        {
            foreach (Renderer candidate in GetComponentsInChildren<Renderer>(true))
            {
                if (!candidate.name.StartsWith("Eye")) continue;
                eyeMaterial = candidate.sharedMaterial;
                break;
            }
        }
        foreach (Renderer oldRenderer in GetComponentsInChildren<Renderer>(true))
            oldRenderer.enabled = false;

        Transform appearance = new GameObject("Active Physics Chassis").transform;
        appearance.SetParent(transform, false);
        CreateVisual("Tall Industrial Chassis", PrimitiveType.Cube, appearance,
            new Vector3(0f, .15f, 0f), new Vector3(2.2f, 2.5f, 1.75f), shellMaterial);
        CreateVisual("Lower Leg Mount", PrimitiveType.Cube, appearance,
            new Vector3(0f, -.98f, 0f), new Vector3(2.5f, .35f, 2.05f), jointMaterial);
        CreateVisual("Top Armour Cap", PrimitiveType.Cube, appearance,
            new Vector3(0f, 1.46f, -.03f), new Vector3(2.05f, .28f, 1.65f), shellMaterial);
        CreateVisual("Front Sensor Plate", PrimitiveType.Cube, appearance,
            new Vector3(0f, .22f, .94f), new Vector3(1.7f, 1.45f, .16f), jointMaterial);
        CreateVisual("Optical Visor", PrimitiveType.Cube, appearance,
            new Vector3(0f, .58f, 1.05f), new Vector3(1.08f, .34f, .12f),
            eyeMaterial != null ? eyeMaterial : jointMaterial);

        for (int side = -1; side <= 1; side += 2)
        {
            CreateVisual($"Side Power Cell {side}", PrimitiveType.Cylinder, appearance,
                new Vector3(side * 1.23f, .2f, -.18f), new Vector3(.42f, .72f, .42f), jointMaterial);
            CreateVisual($"Hip Housing Front {side}", PrimitiveType.Cube, appearance,
                new Vector3(side * 1.2f, -.62f, .72f), new Vector3(.42f, .5f, .58f), shellMaterial);
            CreateVisual($"Hip Housing Rear {side}", PrimitiveType.Cube, appearance,
                new Vector3(side * 1.2f, -.62f, -.72f), new Vector3(.42f, .5f, .58f), shellMaterial);
            CreateVisual($"Antenna {side}", PrimitiveType.Cylinder, appearance,
                new Vector3(side * .67f, 2.15f, -.25f), new Vector3(.055f, .72f, .055f), jointMaterial);
            CreateVisual($"Antenna Tip {side}", PrimitiveType.Sphere, appearance,
                new Vector3(side * .67f, 2.88f, -.25f), Vector3.one * .13f, jointMaterial);
        }
    }

    private static void CreateVisual(string name, PrimitiveType primitive, Transform parent,
        Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject visual = GameObject.CreatePrimitive(primitive);
        visual.name = name;
        visual.transform.SetParent(parent, false);
        visual.transform.localPosition = localPosition;
        visual.transform.localScale = localScale;
        Collider collider = visual.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = false;
            Destroy(collider);
        }
        Renderer renderer = visual.GetComponent<Renderer>();
        if (renderer != null && material != null) renderer.sharedMaterial = material;
    }

    private Rigidbody CreateSegment(string name, Vector3 a, Vector3 b, float mass,
        Material material, PhysicsMaterial physicsMaterial)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        part.name = $"{name} [Physics]";
        part.transform.SetPositionAndRotation((a + b) * .5f, SegmentRotation(a, b));
        float length = Mathf.Max(.25f, Vector3.Distance(a, b));
        part.transform.localScale = new Vector3(segmentThickness, length * .5f, segmentThickness);
        Renderer renderer = part.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = material;
        Collider collider = part.GetComponent<Collider>();
        collider.sharedMaterial = physicsMaterial;
        _ownColliders.Add(collider);
        Rigidbody rigidbody = part.AddComponent<Rigidbody>();
        ConfigureLimbBody(rigidbody, mass);
        part.AddComponent<SpiderBotLimbHitProxy>().Configure(_health);
        return rigidbody;
    }

    private Rigidbody CreateFoot(string name, Vector3 position)
    {
        GameObject foot = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        foot.name = $"{name} [Physics]";
        foot.transform.position = position;
        foot.transform.localScale = Vector3.one * .48f;
        Renderer renderer = foot.GetComponent<Renderer>();
        if (renderer != null) renderer.sharedMaterial = jointMaterial;
        Collider collider = foot.GetComponent<Collider>();
        collider.sharedMaterial = footPhysicsMaterial;
        _ownColliders.Add(collider);
        Rigidbody rigidbody = foot.AddComponent<Rigidbody>();
        ConfigureLimbBody(rigidbody, definition.footMass);
        foot.AddComponent<SpiderBotFootContact>();
        foot.AddComponent<SpiderBotLimbHitProxy>().Configure(_health);
        return rigidbody;
    }

    private static void ConfigureLimbBody(Rigidbody rigidbody, float mass)
    {
        rigidbody.mass = mass;
        rigidbody.linearDamping = .08f;
        rigidbody.angularDamping = .15f;
        rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        rigidbody.maxAngularVelocity = 20f;
        rigidbody.solverIterations = 10;
        rigidbody.solverVelocityIterations = 4;
    }

    private ConfigurableJoint CreatePoweredJoint(Rigidbody ownBody, Rigidbody connectedBody,
        Vector3 worldAnchor)
    {
        ConfigurableJoint joint = ownBody.gameObject.AddComponent<ConfigurableJoint>();
        joint.connectedBody = connectedBody;
        joint.autoConfigureConnectedAnchor = false;
        joint.anchor = ownBody.transform.InverseTransformPoint(worldAnchor);
        joint.connectedAnchor = connectedBody.transform.InverseTransformPoint(worldAnchor);
        joint.xMotion = ConfigurableJointMotion.Locked;
        joint.yMotion = ConfigurableJointMotion.Locked;
        joint.zMotion = ConfigurableJointMotion.Locked;
        joint.angularXMotion = ConfigurableJointMotion.Limited;
        joint.angularYMotion = ConfigurableJointMotion.Limited;
        joint.angularZMotion = ConfigurableJointMotion.Limited;
        SoftJointLimit wide = new() { limit = 100f };
        joint.lowAngularXLimit = new SoftJointLimit { limit = -100f };
        joint.highAngularXLimit = wide;
        joint.angularYLimit = wide;
        joint.angularZLimit = wide;
        joint.rotationDriveMode = RotationDriveMode.Slerp;
        joint.slerpDrive = PoweredDrive();
        joint.projectionMode = JointProjectionMode.PositionAndRotation;
        joint.projectionDistance = .08f;
        joint.projectionAngle = 8f;
        joint.enableCollision = false;
        joint.enablePreprocessing = true;
        return joint;
    }

    private JointDrive PoweredDrive() => new()
    {
        positionSpring = definition.jointSpring,
        positionDamper = definition.jointDamper,
        maximumForce = definition.jointMaximumForce
    };

    private void IgnoreSelfCollisions()
    {
        for (int i = 0; i < _ownColliders.Count; i++)
            for (int j = i + 1; j < _ownColliders.Count; j++)
                if (_ownColliders[i] != null && _ownColliders[j] != null)
                    Physics.IgnoreCollision(_ownColliders[i], _ownColliders[j], true);
    }

    public void EnterRagdoll(Rigidbody ignoredBody)
    {
        if (_ragdolled) return;
        _ragdolled = true;
        JointDrive released = new() { positionSpring = 0f, positionDamper = 2f, maximumForce = 0f };
        foreach (Leg leg in _legs)
        {
            leg.hipJoint.slerpDrive = released;
            leg.kneeJoint.slerpDrive = released;
            leg.ankleJoint.slerpDrive = released;
        }
        enabled = false;
    }

    private static string SideName(int side) => side < 0 ? "Left" : "Right";
}
