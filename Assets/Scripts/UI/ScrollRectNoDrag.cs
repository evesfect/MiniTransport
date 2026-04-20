using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// ScrollRect that responds only to scroll wheel — no click-drag, no elastic bounce.
/// Replace the standard ScrollRect component on the work queue panel with this.
/// </summary>
public class ScrollRectNoDrag : ScrollRect
{
    public override void OnBeginDrag(PointerEventData eventData) { }
    public override void OnDrag(PointerEventData eventData) { }
    public override void OnEndDrag(PointerEventData eventData) { }
}
