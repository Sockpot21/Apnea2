using UnityEngine;

/// <summary>Vector-based swept projectile configured by PlayerEquipment.</summary>
public class Bullet : MonoBehaviour
{
    [HideInInspector] public float speed = 30f;
    [HideInInspector] public float drop = 9.81f;
    [HideInInspector] public float lifetime = 5f;
    [HideInInspector] public float damage = 20f;
    [HideInInspector] public DamageType damageType = DamageType.Pierce;

    private Vector3 _velocity;
    private float _elapsed;
    private GameObject _source;

    public void Launch(Vector3 direction, Vector3 inheritedVelocity, GameObject source = null)
    {
        _velocity = direction.normalized * speed + inheritedVelocity;
        _elapsed = 0f;
        _source = source;
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        if (lifetime > 0f)
        {
            _elapsed += deltaTime;
            if (_elapsed >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
        }

        _velocity += Vector3.down * drop * deltaTime;
        Vector3 delta = _velocity * deltaTime;
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
