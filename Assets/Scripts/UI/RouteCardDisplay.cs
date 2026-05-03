using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UI card for a single route row in the route list panel.
/// Shows route name, assigned bus count, route color swatch, and an edit button.
/// Clicking the card body (not buttons) triggers selection/highlighting.
/// </summary>
public class RouteCardDisplay : MonoBehaviour, IPointerClickHandler
{
    [Header("UI References")]
    public TMP_Text routeNameText;
    public TMP_Text busCountText;
    public Image colorSwatch;
    public Button editButton;
    public Image backgroundImage;

    [Header("Selection Colors")]
    public Color normalBackground = new Color(0.18f, 0.18f, 0.22f, 1f);
    public Color selectedBackground = new Color(0.28f, 0.35f, 0.50f, 1f);

    private Route _route;
    private Action<Route> _onCardClicked;
    private Action<Route> _onEditClicked;
    private bool _isSelected;

    public Route CurrentRoute => _route;

    public void Setup(Route route, Action<Route> onCardClicked, Action<Route> onEditClicked)
    {
        _route = route;
        _onCardClicked = onCardClicked;
        _onEditClicked = onEditClicked;

        routeNameText.text = route.RouteName;
        busCountText.text = RouteVisualizer.GetBusCountForRoute(route.RouteID).ToString();

        if (colorSwatch != null)
            colorSwatch.color = route.RouteColor;

        editButton.onClick.RemoveAllListeners();
        editButton.onClick.AddListener(OnEditPressed);

        SetSelected(false);
    }

    public void SetSelected(bool selected)
    {
        _isSelected = selected;
        if (backgroundImage != null)
            backgroundImage.color = selected ? selectedBackground : normalBackground;
    }

    private void OnEditPressed()
    {
        _onEditClicked?.Invoke(_route);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Only trigger card click if the click wasn't on the edit button
        if (eventData.pointerPress != null && eventData.pointerPress.GetComponent<Button>() != null)
            return;

        _onCardClicked?.Invoke(_route);
    }
}
