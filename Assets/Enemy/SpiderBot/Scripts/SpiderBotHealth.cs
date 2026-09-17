using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpiderBotHealth : MonoBehaviour, ICombatDamageReceiver
{
    [SerializeField] private SpiderBotDefinition definition;
    [SerializeField] private SpiderBotController controller;
    [SerializeField] private ProceduralSpiderLegs legs;
    [SerializeField] private Rigidbody body;

    public event Action<float, float> Damaged;
    public event Action Died;

    public float CurrentHealth { get; private set; }
    public float MaximumHealth => definition != null ? definition.maximumHealth : 100f;
    public bool IsDead { get; private set; }

    public void Configure(SpiderBotDefinition value, SpiderBotController movement,
        ProceduralSpiderLegs proceduralLegs, Rigidbody rigidbody)
    {
        definition = value;
        controller = movement;
        legs = proceduralLegs;
        body = rigidbody;
        CurrentHealth = MaximumHealth;
    }

    private void Awake()
    {
        if (controller == null) controller = GetComponent<SpiderBotController>();
        if (legs == null) legs = GetComponent<ProceduralSpiderLegs>();
        if (body == null) body = GetComponent<Rigidbody>();
        CurrentHealth = MaximumHealth;
    }

    public void ReceiveCombatDamage(CombatDamage damage)
    {
        if (IsDead || damage.amount <= 0f) return;
        float multiplier = damage.type switch
        {
            DamageType.Shock => 1.35f,
            DamageType.Burn => 0.7f,
            DamageType.Pierce => 1.1f,
            _ => 1f
        };
        float dealt = damage.amount * multiplier;
        CurrentHealth = Mathf.Max(0f, CurrentHealth - dealt);
        if (body != null && damage.impulse.sqrMagnitude > 0f)
            body.AddForceAtPosition(damage.impulse, damage.point, ForceMode.Impulse);
        Damaged?.Invoke(CurrentHealth, MaximumHealth);
        Debug.Log($"[SpiderBot] {damage.type} hit for {dealt:F1}. " +
                  $"Health {CurrentHealth:F1}/{MaximumHealth:F1}");
        if (CurrentHealth <= 0f) Die(damage);
    }

    private void Die(CombatDamage finalDamage)
    {
        if (IsDead) return;
        IsDead = true;
        controller?.EnterCorpseState();
        legs?.EnterRagdoll(body);
        if (body != null)
        {
            body.useGravity = true;
            body.constraints = RigidbodyConstraints.None;
            body.AddForce(finalDamage.impulse
                * (definition != null ? definition.corpseImpulseMultiplier : 0.08f),
                ForceMode.Impulse);
        }
        Died?.Invoke();
        Debug.Log("[SpiderBot] Destroyed — procedural control released to physics corpse.");
    }
}
