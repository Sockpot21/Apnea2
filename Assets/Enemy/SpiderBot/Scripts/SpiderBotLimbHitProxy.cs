using UnityEngine;

[DisallowMultipleComponent]
public sealed class SpiderBotLimbHitProxy : MonoBehaviour, ICombatDamageReceiver
{
    private SpiderBotHealth _owner;

    public void Configure(SpiderBotHealth owner) => _owner = owner;

    public void ReceiveCombatDamage(CombatDamage damage)
    {
        _owner?.ReceiveCombatDamage(damage);
    }
}
