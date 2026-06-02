// PickupItem.cs
// Attach to any world GameObject to make it a pickup.
// PlayerInteraction will find it via raycast and call Collect().

using UnityEngine;

public class PickupItem : MonoBehaviour
{
    [Header("Item Data")]
    [Tooltip("The item this pickup represents.")]
    [SerializeField] public ItemDefinition item;

    /// <summary>
    /// Called by PlayerInteraction when the player collects this item.
    /// Returns the ItemDefinition and destroys the world object.
    /// </summary>
    public ItemDefinition Collect()
    {
        var collected = item;
        Destroy(gameObject);
        return collected;
    }
}
