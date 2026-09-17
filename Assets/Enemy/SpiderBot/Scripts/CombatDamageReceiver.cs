using UnityEngine;

public readonly struct CombatDamage
{
    public readonly float amount;
    public readonly DamageType type;
    public readonly Vector3 point;
    public readonly Vector3 impulse;
    public readonly GameObject source;

    public CombatDamage(float amount, DamageType type, Vector3 point,
        Vector3 impulse, GameObject source)
    {
        this.amount = amount;
        this.type = type;
        this.point = point;
        this.impulse = impulse;
        this.source = source;
    }
}

public interface ICombatDamageReceiver
{
    void ReceiveCombatDamage(CombatDamage damage);
}
