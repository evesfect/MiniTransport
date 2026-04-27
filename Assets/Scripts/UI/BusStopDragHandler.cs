using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Drag handler for bus stop cards in the route edit panel.
/// Allows reordering stops by dragging, using the same pattern as WorkItemDragHandler.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class BusStopDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public event Action OnOrderChanged;

    private CanvasGroup _canvasGroup;
    private RectTransform _rectTransform;
    private Canvas _rootCanvas;
    private Transform _originalParent;
    private Vector2 _dragOffset;

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rectTransform = GetComponent<RectTransform>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _originalParent = transform.parent;
        _rootCanvas = GetComponentInParent<Canvas>().rootCanvas;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)_rootCanvas.transform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 pointerInCanvas);

        Vector2 cardInCanvas = _rootCanvas.transform.InverseTransformPoint(_rectTransform.position);
        _dragOffset = cardInCanvas - pointerInCanvas;

        transform.SetParent(_rootCanvas.transform, true);
        transform.SetAsLastSibling();
        _canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            (RectTransform)_rootCanvas.transform,
            eventData.position,
            eventData.pressEventCamera,
            out Vector2 pointerInCanvas);

        _rectTransform.localPosition = pointerInCanvas + _dragOffset;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _canvasGroup.blocksRaycasts = true;

        transform.SetParent(_originalParent, true);
        transform.SetSiblingIndex(FindInsertionIndex());

        OnOrderChanged?.Invoke();
    }

    private int FindInsertionIndex()
    {
        float draggedWorldY = _rectTransform.position.y;
        int childCount = _originalParent.childCount;

        for (int i = 0; i < childCount; i++)
        {
            Transform sibling = _originalParent.GetChild(i);
            if (sibling == transform || !sibling.gameObject.activeSelf) continue;

            if (draggedWorldY > sibling.position.y)
                return i;
        }

        return childCount - 1;
    }
}
