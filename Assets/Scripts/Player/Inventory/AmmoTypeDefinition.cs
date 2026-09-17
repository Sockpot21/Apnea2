using UnityEngine;

/// <summary>Shared ammunition identity selected by ammo items and ranged weapons.</summary>
[CreateAssetMenu(fileName = "NewAmmoType", menuName = "Inventory/Ammo Type")]
public class AmmoTypeDefinition : ScriptableObject
{
    public string ammoTypeID;
    public string displayName;
    [Tooltip("Projectile spawned whenever a weapon chambered for this ammo fires.")]
    public GameObject projectilePrefab;
}
