using UnityEngine;
using Unity.Netcode;

public class RoleSelectionIMGUI : MonoBehaviour
{
    private void OnGUI()
    {
        // 1. Don't show the role menu if we aren't fully connected yet
        if (NetworkManager.Singleton == null) return;
        bool fullyConnected = NetworkManager.Singleton.IsServer || NetworkManager.Singleton.IsConnectedClient;
        if (!fullyConnected) return;

        // 2. Don't show the role menu if RoleManager isn't loaded
        if (RoleManager.Instance == null)
            return;

        // 3. If we already have a role, hide this menu!
        if (RoleManager.Instance.GetMyRole() != PlayerRole.None)
            return;

        // --- DRAW THE UI ---
        GUILayout.BeginArea(new Rect(Screen.width / 2f - 100f, Screen.height / 2f - 150f, 200f, 300f), GUI.skin.box);
        GUILayout.Label("Select Your Role", new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold });
        GUILayout.Space(10);

        DrawRoleButton(PlayerRole.GeneralManager, "General Manager");
        DrawRoleButton(PlayerRole.MaintenanceManager, "Maintenance Manager");
        DrawRoleButton(PlayerRole.TransportManager, "Transport Manager");
        DrawRoleButton(PlayerRole.FinanceManager, "Finance Manager");
        DrawRoleButton(PlayerRole.HRManager, "HR Manager");

        GUILayout.EndArea();
    }

    private void DrawRoleButton(PlayerRole role, string displayName)
    {
        // Check if someone else already took this role
        bool isTaken = RoleManager.Instance.IsRoleTaken(role);

        // If it is taken, gray out the button
        GUI.enabled = !isTaken;

        string buttonText = isTaken ? $"{displayName} (Taken)" : displayName;

        if (GUILayout.Button(buttonText, GUILayout.Height(40)))
        {
            RoleManager.Instance.SelectRole(role);
        }

        // Reset GUI enabled state for the next button
        GUI.enabled = true;
        GUILayout.Space(5);
    }
}