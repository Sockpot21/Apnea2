// Bullet.cs
// Attach to your bullet prefab.
// Travels at bulletSpeed and drops due to gravity scaled by bulletDrop.
// Destroys itself on collision or after lifetime expires.

using UnityEngine;

public class Bullet : MonoBehaviour
{
    [Header("Set by PlayerEquipment on spawn — do not set manually")]
    public float speed = 30f;
    public float drop = 1f;   // gravity scale: 0 = no drop, 1 = full gravity
    public float lifetime = 5f;

    private Vector3 _velocity;
    private float _elapsed;

    // ── Called by PlayerEquipment immediately after Instantiate ───────────────

    public void Launch(Vector3 direction)
    {
        _velocity = direction.normalized * speed;
        _elapsed = 0f;
    }

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Update()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= lifetime) { Destroy(gameObject); return; }

        // Apply bullet drop (scaled gravity)
        _velocity += Physics.gravity * drop * Time.deltaTime;

        // Move
        var delta = _velocity * Time.deltaTime;

        // Sweep with a raycast so fast bullets don't tunnel through thin objects
        if (Physics.Raycast(transform.position, delta.normalized, out var hit, delta.magnitude))
        {
            Debug.Log($"[Bullet] Hit: {hit.collider.name}");
            // TODO: call hit.collider.GetComponent<HealthManager>()?.ReceiveDamage(...)
            // when damage is wired up
            Destroy(gameObject);
            return;
        }

        transform.position += delta;

        // Orient bullet along travel direction
        if (_velocity.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(_velocity);
    }
}
