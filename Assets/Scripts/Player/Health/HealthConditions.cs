using System;
using System.Collections.Generic;
using UnityEngine;

public enum HealthConditionType
{
    Bleeding, InternalBleeding, IntrusiveObject, Fracture, Dislocation,
    Burned, OnFire, Malfunction, Severed, Crushed
}

[Serializable]
public class RuntimeHealthCondition
{
    public HealthConditionType type;
    public DamageType sourceDamageType;
    [Min(1)] public int severity = 1;
    public float remainingSeconds;
    public float biofluidLossPerSecond;
    public float tickDamage;
    public float maxHealthPenalty;
    public float movementPenalty;
    public bool requiresTreatment;

    public bool IsPermanent => remainingSeconds < 0f;
}

public static class HealthConditionRules
{
    public static int SeverityTier(float damage, float baseMaxHealth)
    {
        if (baseMaxHealth <= 0f) return 0;
        float ratio = damage / baseMaxHealth;
        if (ratio >= 1f) return 5;
        if (ratio >= .5f) return 4;
        if (ratio >= .3f) return 3;
        if (ratio >= .2f) return 2;
        if (ratio >= .1f) return 1;
        return 0;
    }

    public static float DurationForTier(int tier) => tier switch
    {
        1 => 5f, 2 => 15f, 3 => 30f, 4 => 60f, 5 => -1f, _ => 0f
    };
}
