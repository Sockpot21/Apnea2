// Bullet.cs
// Pure vector-based bullet. No Rigidbody needed on the prefab.
// Stats are set by PlayerEquipment after instantiation.
// Attach this to your bullet prefab.

using UnityEngine;

public class Bullet : MonoBehaviour
{
    // Set by PlayerEquipment after Instantiate
    [HideInInspector] public float speed = 30f;
    [HideInInspector] public float drop = 9.81f; // units/s² downward acceleration
    [HideInInspector] public float lifetime = 5f;    // 0 = never despawn

    private Vector3 _velocity;
    private float _elapsed;

    public void Launch(Vector3 direction, Vector3 inheritedVelocity)
    {
        // Inherit the shooter's velocity so bullets feel natural when moving
        _velocity = direction.normalized * speed + inheritedVelocity;
        _elapsed = 0f;
    }

    private void Update()
    {
        float dt = Time.deltaTime;

        // Lifetime check — 0 means never despawn
        if (lifetime > 0f)
        {
            _elapsed += dt;
            if (_elapsed >= lifetime) { Destroy(gameObject); return; }
        }

        // Apply bullet drop (pure downward acceleration, independent of physics)
        _velocity += Vector3.down * drop * dt;

        // Sweep raycast so fast bullets don't tunnel through thin geometry
        var delta = _velocity * dt;
        if (Physics.Raycast(transform.position, delta.normalized, out var hit, delta.magnitude))
        {
            Debug.Log($"[Bullet] Hit: {hit.collider.name}");
            // TODO: call HealthManager.ReceiveDamage when damage system is wired up
            Destroy(gameObject);
            return;
        }

        transform.position += delta;

        // Orient along travel direction
        if (_velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_velocity);
    }
}
