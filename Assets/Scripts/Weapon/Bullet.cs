using UnityEngine;

/// <summary>Vector-based swept projectile configured by PlayerEquipment.</summary>
public class Bullet : MonoBehaviour
{
    [HideInInspector] public float speed = 30f;
    [HideInInspector] public float maximumRangeMeters = 1000f;
    [HideInInspector] public float damage = 20f;
    [HideInInspector] public DamageType damageType = DamageType.Pierce;

    private Vector3 _velocity;
    private float _distanceTravelled;
    private GameObject _source;

    public void Launch(Vector3 direction, Vector3 inheritedVelocity, GameObject source = null)
    {
        _velocity = direction.normalized * speed + inheritedVelocity;
        _distanceTravelled = 0f;
        _source = source;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        // All conventional projectiles share the project's global gravitational
        // acceleration. Weapon assets no longer carry their own arbitrary drop.
        _velocity += Physics.gravity * deltaTime;
        Vector3 delta = _velocity * deltaTime;
        bool reachesMaximumRange = false;
        if (maximumRangeMeters > 0f)
        {
            float remainingRange = maximumRangeMeters - _distanceTravelled;
            if (remainingRange <= 0f)
            {
                Destroy(gameObject);
                return;
            }
            if (delta.magnitude >= remainingRange)
            {
                delta = delta.normalized * remainingRange;
                reachesMaximumRange = true;
            }
        }
        if (delta.sqrMagnitude > Mathf.Epsilon)
        {
            RaycastHit[] hits = Physics.RaycastAll(transform.position, delta.normalized,
                delta.magnitude, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit hit in hits)
            {
                if (_source != null && (hit.collider.transform == _source.transform
                    || hit.collider.transform.IsChildOf(_source.transform))) continue;
                ApplyDamage(hit);
                Debug.Log($"[Bullet] Hit: {hit.collider.name}");
                Destroy(gameObject);
                return;
            }
        }

        transform.position += delta;
        _distanceTravelled += delta.magnitude;
        if (reachesMaximumRange)
        {
            Destroy(gameObject);
            return;
        }
        if (_velocity.sqrMagnitude > .01f)
            transform.rotation = Quaternion.LookRotation(_velocity);
    }

    private void ApplyDamage(RaycastHit hit)
    {
        foreach (MonoBehaviour component in hit.collider.GetComponentsInParent<MonoBehaviour>())
        {
            if (component is not ICombatDamageReceiver receiver) continue;
            receiver.ReceiveCombatDamage(new CombatDamage(damage, damageType, hit.point,
                _velocity.normalized * Mathf.Max(1f, damage * .18f), _source));
            return;
        }
    }
}
