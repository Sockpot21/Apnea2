using UnityEngine;

public struct CameraInput
{
    public Vector2 Look;
}
public class PlayerCamera : MonoBehaviour
{
    [SerializeField] private float sensitivity = 0.1f;
    [Tooltip("Maximum angle the player can look upward from level.")]
    [Range(1f, 89f)] [SerializeField] private float maxLookUpAngle = 85f;
    [Tooltip("Maximum angle the player can look downward from level.")]
    [Range(1f, 89f)] [SerializeField] private float maxLookDownAngle = 85f;

    private Vector3 _eulerAngles;
    private Vector2 _recoilOffset;
    private float _recoilRecoverySpeed;
    public void Initialize(Transform target)
    {
        transform.position = target.position;
        _eulerAngles = target.eulerAngles;
        _eulerAngles.x = NormalizeAngle(_eulerAngles.x);
        _eulerAngles.x = Mathf.Clamp(_eulerAngles.x, -maxLookUpAngle, maxLookDownAngle);
        _recoilOffset = Vector2.zero;
        transform.eulerAngles = _eulerAngles;
    }

    public void UpdateRotation(CameraInput input)
    {
        _eulerAngles += new Vector3(-input.Look.y, input.Look.x) * sensitivity;
        _eulerAngles.x = Mathf.Clamp(_eulerAngles.x, -maxLookUpAngle, maxLookDownAngle);
        _recoilOffset = Vector2.MoveTowards(_recoilOffset, Vector2.zero,
            _recoilRecoverySpeed * Time.deltaTime);
        float pitch = Mathf.Clamp(_eulerAngles.x + _recoilOffset.x,
            -maxLookUpAngle, maxLookDownAngle);
        transform.eulerAngles = new Vector3(pitch, _eulerAngles.y + _recoilOffset.y, 0f);
    }

    public void AddRecoil(float verticalDegrees, float horizontalDegrees, float recoverySeconds)
    {
        _recoilOffset.x -= Mathf.Max(0f, verticalDegrees);
        _recoilOffset.y += Random.Range(-Mathf.Max(0f, horizontalDegrees),
            Mathf.Max(0f, horizontalDegrees));
        _recoilRecoverySpeed = _recoilOffset.magnitude / Mathf.Max(0.01f, recoverySeconds);
    }

    private static float NormalizeAngle(float angle) => Mathf.Repeat(angle + 180f, 360f) - 180f;

    public void UpdatePosition(Transform target)
    {
        transform.position = target.position;
    }
}
