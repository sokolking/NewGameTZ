using UnityEngine;
using UnityEngine.EventSystems;

[DisallowMultipleComponent]
public sealed class InventoryCellHoverRelay : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    public InventoryUI Owner;
    public int SlotIndex;

    public void OnPointerEnter(PointerEventData eventData)
    {
        Owner?.OnInventoryCellHoverEnter(SlotIndex);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        Owner?.OnInventoryCellHoverExit(SlotIndex);
    }
}
