using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class StanceVinette : MonoBehaviour
{
    [SerializeField] private float min = 0.1f;
    [SerializeField] private float max = 0.35f;
    [SerializeField, Range(0f, 1f)] private float proneIntensity = 0.55f;
    [SerializeField] private float response = 10f;

    private VolumeProfile _profile;
    private Vignette _vignette;
    private Color _baseColor;

    public void Initialize(VolumeProfile profile)
    {
        _profile = profile;

        if (!profile.TryGet(out _vignette))
            _vignette = profile.Add<Vignette>();

        _baseColor = _vignette.color.value;
        _vignette.intensity.Override(min);
    }

    public void UpdateVignette(float deltaTime, Stance stance) =>
        UpdateVignette(deltaTime, stance, 0f, _baseColor, 0f);

    public void UpdateVignette(float deltaTime, Stance stance,
        float healthIntensity, Color healthColor, float healthBlend)
    {
        float stanceIntensity = stance switch
        {
            Stance.Prone => proneIntensity,
            Stance.Crouch or Stance.Slide => max,
            _ => min
        };
        // Stance and health are independent inputs to one URP vignette. Taking
        // the stronger value prevents prone/crouch from adding to critical
        // health until the view is almost completely obscured.
        float targetIntensity = Mathf.Clamp01(Mathf.Max(stanceIntensity,
            Mathf.Max(0f, healthIntensity)));
        Color targetColor = Color.Lerp(_baseColor, healthColor, Mathf.Clamp01(healthBlend));
        float lerp = 1f - Mathf.Exp(-response * deltaTime);

        _vignette.intensity.value = Mathf.Lerp(_vignette.intensity.value,
            targetIntensity, lerp);
        _vignette.color.value = Color.Lerp(_vignette.color.value, targetColor, lerp);
    }
}
