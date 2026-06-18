using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;

[RequireComponent(typeof(CanvasGroup))]
public class DraggablePartItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Header("UI References")]
    public TMP_Text partNameText;

    public BusPartType PartType { get; private set; }

    private PriorityListManager _manager;
    private Transform _parentToReturnTo = null;
    private GameObject _placeholder = null;
    private CanvasGroup _canvasGroup;
    private RectTransform _rectTransform; // Added reference

    private void Awake()
    {
        _canvasGroup = GetComponent<CanvasGroup>();
        _rectTransform = GetComponent<RectTransform>();
    }

    public void Setup(BusPartType type, PriorityListManager manager)
    {
        PartType = type;
        _manager = manager;
        if (partNameText != null) partNameText.text = type.ToString();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        _parentToReturnTo = transform.parent;
        Vector2 exactSize = _rectTransform.rect.size;

        // 1. Create the placeholder and EXPLICITLY make it a UI RectTransform
        _placeholder = new GameObject("Placeholder", typeof(RectTransform));
        _placeholder.transform.SetParent(_parentToReturnTo, false);

        // 2. Add the Layout Element and FORCE it to hold the gap open
        LayoutElement le = _placeholder.AddComponent<LayoutElement>();

        le.preferredWidth = exactSize.x;
        le.preferredHeight = exactSize.y;

        // --- THE FIX ---
        // minHeight/minWidth completely prevents the VerticalLayoutGroup from crushing the gap!
        le.minWidth = exactSize.x;
        le.minHeight = exactSize.y;

        // Ensure it doesn't accidentally stretch into infinity
        le.flexibleWidth = 0;
        le.flexibleHeight = 0;

        _placeholder.transform.SetSiblingIndex(transform.GetSiblingIndex());

        // 3. Pop the actual item out
        transform.SetParent(_parentToReturnTo.parent, true);
        _rectTransform.sizeDelta = exactSize;
        transform.SetAsLastSibling();

        // Flatten the Z!
        Vector3 beginPos = transform.localPosition;
        beginPos.z = 0;
        transform.localPosition = beginPos;

        _canvasGroup.alpha = 0.8f;
        _canvasGroup.blocksRaycasts = false;
    }

    public void OnDrag(PointerEventData eventData)
    {
        // 1. Move the UI element flawlessly, regardless of your Canvas settings
        if (RectTransformUtility.ScreenPointToWorldPointInRectangle(
            (RectTransform)_parentToReturnTo.parent, // The canvas/panel we are floating inside
            eventData.position,
            eventData.pressEventCamera,
            out Vector3 globalMousePos))
        {
            transform.position = globalMousePos;
        }

        // Force the card to stay completely flat against the canvas
        Vector3 flatPosition = transform.localPosition;
        flatPosition.z = 0;
        transform.localPosition = flatPosition;

        // --- THE GOLD STANDARD PLACEMENT MATH ---

        int newSiblingIndex = 0;

        for (int i = 0; i < _parentToReturnTo.childCount; i++)
        {
            Transform child = _parentToReturnTo.GetChild(i);

            // Ignore the placeholder
            if (child == _placeholder.transform) continue;

            // CRITICAL FIX: Convert the static child's World Position into Screen Pixels!
            // (eventData.pressEventCamera handles the math whether your Canvas is Overlay or Camera)
            Vector2 childScreenPos = RectTransformUtility.WorldToScreenPoint(eventData.pressEventCamera, child.position);

            // Now we are safely comparing Mouse Screen Pixels to Child Screen Pixels.
            // It literally cannot fail, no matter your resolution or canvas scaling!
            if (eventData.position.y < childScreenPos.y)
            {
                newSiblingIndex++;
            }
        }

        // Snap the gap exactly where it belongs
        _placeholder.transform.SetSiblingIndex(newSiblingIndex);
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        
        transform.SetParent(_parentToReturnTo, false);

        // Safety net: Force the scale back to 1 in case it got squashed
        transform.localScale = Vector3.one;

        Vector3 endPos = transform.localPosition;
        endPos.z = 0;
        transform.localPosition = endPos;

        transform.SetSiblingIndex(_placeholder.transform.GetSiblingIndex());

        // Clean up
        Destroy(_placeholder);
        _canvasGroup.alpha = 1f;
        _canvasGroup.blocksRaycasts = true;

        // 4. Tell the manager the order has changed!
        _manager.OnListReordered();
    }
}