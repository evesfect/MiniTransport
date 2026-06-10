using UnityEngine;

/// <summary>
/// Attach this to a PERSISTENT object (like your Canvas or Bottom Bar), 
/// NOT the button itself, so it stays active and listens to network events!
/// </summary>
public class RequestButtonAccess : MonoBehaviour
{
    [Header("UI References")]
    [Tooltip("Drag the Request Button GameObject here")]
    public GameObject requestButtonObject;

    private void Start()
    {
        // Hide by default at the start of the game
        if (requestButtonObject != null)
        {
            requestButtonObject.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (RoleManager.Instance != null)
        {
            RoleManager.Instance.OnRolesUpdated += CheckAccess;
        }
    }

    private void OnDisable()
    {
        if (RoleManager.Instance != null)
        {
            RoleManager.Instance.OnRolesUpdated -= CheckAccess;
        }
    }

    private void CheckAccess()
    {
        if (requestButtonObject == null) return;

        PlayerRole myRole = RoleManager.Instance.GetMyRole();

        // Only these two roles are allowed to see the Request button
        if (myRole == PlayerRole.MaintenanceManager || myRole == PlayerRole.TransportManager)
        {
            requestButtonObject.SetActive(true);
        }
        else
        {
            requestButtonObject.SetActive(false);
        }
    }
}