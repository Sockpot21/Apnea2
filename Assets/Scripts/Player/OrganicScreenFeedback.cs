using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>Composes death-critical health and stamina feedback over the existing HUD.</summary>
public sealed class OrganicScreenFeedback : MonoBehaviour
{
    private PlayerCharacter _character;
    private HealthManager _health;
    private ColorAdjustments _colorAdjustments;
    private Image _healthOverlay;
    private GameObject _overlayRoot;
    private float _baseSaturation;
    private float _healthVignetteFactor;
    private float _healthOverlayFactor;
    private float _staminaGreyscaleFactor;

    public float HealthVignetteIntensity => _character == null
        ? 0f
        : _healthVignetteFactor * _character.CriticalHealthVignetteIntensity;
    public float HealthVignetteBlend => _healthVignetteFactor;
    public Color HealthVignetteColor => _character == null
        ? Color.red
        : _character.CriticalHealthVignetteColor;

    public void Initialize(VolumeProfile profile, PlayerCharacter character, HealthManager health)
    {
        _character = character;
        _health = health;
        if (profile != null)
        {
            if (!profile.TryGet(out _colorAdjustments))
                _colorAdjustments = profile.Add<ColorAdjustments>();
            _baseSaturation = _colorAdjustments.saturation.value;
            _colorAdjustments.saturation.Override(_baseSaturation);
        }
        BuildOverlay();
    }

    public void UpdateFeedback(float deltaTime)
    {
        if (_character == null) return;
        float response = Mathf.Max(0.01f, _character.OrganicFeedbackResponse);
        float lerp = 1f - Mathf.Exp(-response * deltaTime);

        float survival = _health != null ? _health.GetSurvivalRatio() : 1f;
        bool dead = _health != null && _health.IsFatal;
        float targetVignette = _character.HealthScreenFeedbackEnabled
            ? Mathf.InverseLerp(.3f, .1f, survival)
            : 0f;
        float targetOverlay = _character.HealthScreenFeedbackEnabled
            ? Mathf.InverseLerp(.1f, 0f, survival)
            : 0f;
        if (dead && _character.HealthScreenFeedbackEnabled)
        {
            targetVignette = 1f;
            targetOverlay = 1f;
        }
        _healthVignetteFactor = Mathf.Lerp(_healthVignetteFactor, targetVignette, lerp);
        _healthOverlayFactor = Mathf.Lerp(_healthOverlayFactor, targetOverlay, lerp);

        if (_healthOverlay != null)
        {
            Color configured = _character.CriticalHealthOverlayColor;
            float alpha = dead && _character.HealthScreenFeedbackEnabled
                ? _healthOverlayFactor
                : configured.a * _healthOverlayFactor;
            _healthOverlay.color = new Color(configured.r, configured.g, configured.b,
                Mathf.Clamp01(alpha));
        }

        float staminaRatio = _character.MaxStamina > Mathf.Epsilon
            ? _character.CurrentStamina / _character.MaxStamina
            : 0f;
        float targetGreyscale = _character.StaminaScreenFeedbackEnabled
            ? Mathf.InverseLerp(.3f, 0f, staminaRatio)
            : 0f;
        _staminaGreyscaleFactor = Mathf.Lerp(_staminaGreyscaleFactor, targetGreyscale, lerp);
        if (_colorAdjustments != null)
            _colorAdjustments.saturation.value = Mathf.Lerp(_baseSaturation, -100f,
                _staminaGreyscaleFactor);
    }

    private void BuildOverlay()
    {
        if (_overlayRoot != null) return;
        _overlayRoot = new GameObject("OrganicHealthOverlay", typeof(RectTransform),
            typeof(Canvas), typeof(CanvasScaler));
        _overlayRoot.transform.SetParent(transform, false);
        Canvas canvas = _overlayRoot.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 900;
        CanvasScaler scaler = _overlayRoot.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);

        GameObject wash = new("HealthWash", typeof(RectTransform), typeof(Image));
        wash.transform.SetParent(_overlayRoot.transform, false);
        RectTransform rect = wash.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        _healthOverlay = wash.GetComponent<Image>();
        _healthOverlay.color = Color.clear;
        _healthOverlay.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (_colorAdjustments != null)
            _colorAdjustments.saturation.value = _baseSaturation;
        if (_overlayRoot != null) Destroy(_overlayRoot);
    }
}
