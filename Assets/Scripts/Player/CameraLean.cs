using UnityEngine;

public class CameraLean : MonoBehaviour
{
    [Header("Movement Lean")]
    [SerializeField] private float attackDamping = 0.5f;
    [SerializeField] private float decayDamping = 0.3f;
    [SerializeField] private float walkStrength = 0.075f;
    [SerializeField] private float slideStrength = 0.2f;
    [SerializeField] private float strengthResponse = 5f;

    [Header("Wall Run Tilt")]
    [SerializeField] private float wallRunTiltAngle = 15f;  // degrees to roll away from wall
    [SerializeField] private float wallRunTiltResponse = 8f;   // how quickly tilt transitions

    private Vector3 _dampedAcceleration;
    private Vector3 _dampedAccelerationVel;
    private float _smoothStrength;
    private float _currentWallTilt; // current roll angle in degrees

    public void Initialize()
    {
        _smoothStrength = walkStrength;
        _currentWallTilt = 0f;
    }

    /// <summary>Standard movement lean — call every LateUpdate.</summary>
    public void UpdateLean(float deltaTime, bool sliding, Vector3 acceleration, Vector3 up)
    {
        var planarAcceleration = Vector3.ProjectOnPlane(acceleration, up);
        var damping = planarAcceleration.magnitude > _dampedAcceleration.magnitude
            ? attackDamping : decayDamping;

        _dampedAcceleration = Vector3.SmoothDamp(
            _dampedAcceleration, planarAcceleration,
            ref _dampedAccelerationVel, damping, float.PositiveInfinity, deltaTime);

        var leanAxis = Vector3.Cross(_dampedAcceleration.normalized, up).normalized;

        transform.localRotation = Quaternion.identity;

        var targetStrength = sliding ? slideStrength : walkStrength;
        _smoothStrength = Mathf.Lerp(_smoothStrength, targetStrength,
            1f - Mathf.Exp(-strengthResponse * deltaTime));

        transform.rotation = Quaternion.AngleAxis(
            -_dampedAcceleration.magnitude * _smoothStrength, leanAxis) * transform.rotation;
    }

    /// <summary>
    /// Wall run camera roll tilt — call after UpdateLean every LateUpdate.
    /// Tilts camera away from the wall when wall running.
    /// wallNormal should be Vector3.zero when not wall running.
    /// </summary>
    public void UpdateWallRunTilt(float deltaTime, bool isWallRunning,
                                   Vector3 wallNormal, Vector3 characterUp)
    {
        float targetTilt = 0f;

        if (isWallRunning && wallNormal != Vector3.zero)
        {
            var camForward = transform.forward;
            var wallSide = Vector3.Dot(Vector3.Cross(camForward, wallNormal), characterUp);
            // wallSide > 0 = wall on left  → tilt top of camera RIGHT (away from wall) = negative roll
            // wallSide < 0 = wall on right → tilt top of camera LEFT  (away from wall) = positive roll
            targetTilt = wallSide > 0f ? -wallRunTiltAngle : wallRunTiltAngle;
        }

        _currentWallTilt = Mathf.Lerp(_currentWallTilt, targetTilt,
            1f - Mathf.Exp(-wallRunTiltResponse * deltaTime));

        // Apply roll around the camera's forward axis
        transform.rotation *= Quaternion.AngleAxis(_currentWallTilt, Vector3.forward);
    }
}
