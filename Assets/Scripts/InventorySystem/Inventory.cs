using System.Collections.Generic;
using UnityEngine;
using System;

[System.Serializable]
public class InventorySlot
{
    public Item item;
    public int amount;
    
    public InventorySlot()
    {
        item = null;
        amount = 0;
    }
    
    public InventorySlot(Item item, int amount)
    {
        this.item = item;
        this.amount = amount;
    }
    
    public bool IsEmpty()
    {
        return item == null || amount <= 0;
    }
    
    public bool CanAddItem(Item itemToAdd, int amountToAdd)
    {
        if (IsEmpty()) return true;
        if (item == itemToAdd && item.isStackable)
        {
            return amount + amountToAdd <= item.maxStackSize;
        }
        return false;
    }
    
    public void AddItem(Item itemToAdd, int amountToAdd)
    {
        if (IsEmpty())
        {
            item = itemToAdd;
            amount = amountToAdd;
        }
        else if (item == itemToAdd && item.isStackable)
        {
            amount += amountToAdd;
        }
    }
    
    public void RemoveItem(int amountToRemove)
    {
        amount -= amountToRemove;
        if (amount <= 0)
        {
            item = null;
            amount = 0;
        }
    }
    
    public void ClearSlot()
    {
        item = null;
        amount = 0;
    }
}

public class Inventory : MonoBehaviour
{
    [Header("背包设置")]
    public int inventorySize = 20;
    
    [SerializeField]
    private List<InventorySlot> slots = new List<InventorySlot>();
    
    public static event Action<List<InventorySlot>> OnInventoryChanged;
    
    private void Awake()
    {
        InitializeInventory();
    }
    
    private void InitializeInventory()
    {
        slots.Clear();
        for (int i = 0; i < inventorySize; i++)
        {
            slots.Add(new InventorySlot());
        }
    }
    
    public bool AddItem(Item item, int amount = 1)
    {
        if (item == null) return false;
        
        // 如果物品可堆叠，先尝试添加到现有堆叠中
        if (item.isStackable)
        {
            for (int i = 0; i < slots.Count; i++)
            {
                if (!slots[i].IsEmpty() && slots[i].item == item)
                {
                    int canAdd = Mathf.Min(amount, item.maxStackSize - slots[i].amount);
                    if (canAdd > 0)
                    {
                        slots[i].AddItem(item, canAdd);
                        amount -= canAdd;
                        if (amount <= 0)
                        {
                            OnInventoryChanged?.Invoke(slots);
                            return true;
                        }
                    }
                }
            }
        }
        
        // 添加到空槽位
        while (amount > 0)
        {
            int emptySlotIndex = GetFirstEmptySlot();
            if (emptySlotIndex == -1) return false; // 背包已满
            
            int amountToAdd = item.isStackable ? Mathf.Min(amount, item.maxStackSize) : 1;
            slots[emptySlotIndex].AddItem(item, amountToAdd);
            amount -= amountToAdd;
        }
        
        OnInventoryChanged?.Invoke(slots);
        return true;
    }
    
    public bool RemoveItem(Item item, int amount = 1)
    {
        if (item == null) return false;
        
        int totalAmount = GetItemAmount(item);
        if (totalAmount < amount) return false;
        
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsEmpty() && slots[i].item == item)
            {
                int removeAmount = Mathf.Min(amount, slots[i].amount);
                slots[i].RemoveItem(removeAmount);
                amount -= removeAmount;
                
                if (amount <= 0) break;
            }
        }
        
        OnInventoryChanged?.Invoke(slots);
        return true;
    }
    
    public int GetItemAmount(Item item)
    {
        int totalAmount = 0;
        foreach (var slot in slots)
        {
            if (!slot.IsEmpty() && slot.item == item)
            {
                totalAmount += slot.amount;
            }
        }
        return totalAmount;
    }
    
    public bool HasItem(Item item, int amount = 1)
    {
        return GetItemAmount(item) >= amount;
    }
    
    public int GetItemCount(Item item)
    {
        return GetItemAmount(item);
    }
    
    public int GetFirstEmptySlot()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (slots[i].IsEmpty())
            {
                return i;
            }
        }
        return -1;
    }
    
    public InventorySlot GetSlot(int index)
    {
        if (index >= 0 && index < slots.Count)
        {
            return slots[index];
        }
        return null;
    }
    
    public List<InventorySlot> GetAllSlots()
    {
        return new List<InventorySlot>(slots);
    }
    
    public bool SwapSlots(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= slots.Count || toIndex < 0 || toIndex >= slots.Count)
            return false;
            
        var tempSlot = new InventorySlot(slots[fromIndex].item, slots[fromIndex].amount);
        slots[fromIndex] = new InventorySlot(slots[toIndex].item, slots[toIndex].amount);
        slots[toIndex] = tempSlot;
        
        OnInventoryChanged?.Invoke(slots);
        return true;
    }
    
    /// <summary>
    /// 触发背包变化事件
    /// </summary>
    public void TriggerInventoryChanged()
    {
        OnInventoryChanged?.Invoke(slots);
    }
}