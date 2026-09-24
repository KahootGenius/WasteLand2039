using UnityEngine;

[CreateAssetMenu(fileName = "New Item", menuName = "Inventory/Item")]
public class Item : ScriptableObject
{
    [Header("Basic Information")]
    public string itemName = "New Item";
    public Sprite icon;
    [TextArea(3, 5)]
    public string description = "Item Description";
    
    [Header("Item Properties")]
    public ItemType itemType;
    public int itemLevel = 1;
    public bool isStackable = true;
    [Range(1, 999)]
    public int maxStackSize = 64;
    public int currentAmount = 1;
    
    [Header("Prefab and Special Properties")]
    public GameObject itemPrefab;
    public bool isQuestItem = false;
    public bool isConsumable = false;
}

public enum ItemType
{
    Weapon,
    Armor,
    Consumable,
    Material,
    Quest,
    Misc,
    Tool,
    Blueprint
}