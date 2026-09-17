using UnityEngine;

/// <summary>
/// Defines a world pickup containing a fixed quantity of an existing stackable item.
/// The definition disappears on collection; inventory receives the underlying item.
/// </summary>
[CreateAssetMenu(fileName = "NewItemStack", menuName = "Inventory/Item Stack")]
public class ItemStackDefinition : ScriptableObject
{
    public ItemDefinition item;
    [Min(1)] public int stackSize = 1;
    [Tooltip("World object used to represent this stack pickup.")]
    public GameObject worldPrefab;

    public bool IsValid => item != null && item.isStackable && stackSize > 0;

    public ItemInstance CreateItemInstance()
    {
        if (!IsValid) return null;
        return new ItemInstance(item, Mathf.Clamp(stackSize, 1, Mathf.Max(1, item.maxStackSize)));
    }

    public GameObject Spawn(Vector3 position, Quaternion rotation)
    {
        if (!IsValid || worldPrefab == null)
        {
            Debug.LogWarning($"[ItemStack] '{name}' needs a stackable item and world prefab.");
            return null;
        }
        GameObject instance = Instantiate(worldPrefab, position, rotation);
        PickupItem pickup = instance.GetComponent<PickupItem>() ?? instance.AddComponent<PickupItem>();
        pickup.ConfigureStack(this);
        return instance;
    }

    private void OnValidate()
    {
        int maximum = item != null && item.isStackable ? Mathf.Max(1, item.maxStackSize) : int.MaxValue;
        stackSize = Mathf.Clamp(stackSize, 1, maximum);
    }
}
