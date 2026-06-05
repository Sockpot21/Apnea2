// WeaponMuzzle.cs
// Attach to an empty GameObject that is a child of your weapon prefab.
// Position it at the barrel tip. PlayerEquipment finds it automatically
// via GetComponentInChildren<WeaponMuzzle>() on the instantiated prefab.
// You set it once on the prefab — it travels with the weapon forever.

using UnityEngine;

public class WeaponMuzzle : MonoBehaviour
{
    // No fields needed — this component exists purely as a positional marker.
    // Its transform.position and transform.forward are used as the bullet
    // spawn point and initial direction.
}
