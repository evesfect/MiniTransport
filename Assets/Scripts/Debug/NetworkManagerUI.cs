using Unity.Netcode;
using UnityEngine;

public class NetworkManagerUI : MonoBehaviour
{
    private NetworkManager m_NetworkManager;

    void Awake()
    {
        m_NetworkManager = GetComponent<NetworkManager>();
    }

    void OnGUI()
    {
        // Define the size of the area
        float areaWidth = 300;
        float areaHeight = 300;
        float padding = 10;

        // Calculate position: Screen Width - Area Width - Padding
        float xPosition = Screen.width - areaWidth - padding;
        float yPosition = padding;

        GUILayout.BeginArea(new Rect(xPosition, yPosition, areaWidth, areaHeight));

        if (!m_NetworkManager.IsClient && !m_NetworkManager.IsServer)
        {
            StartButtons();
        }
        else
        {
            StatusLabels();
        }

        GUILayout.EndArea();
    }

    void StartButtons()
    {
        if (GUILayout.Button("Host")) m_NetworkManager.StartHost();
        if (GUILayout.Button("Client")) m_NetworkManager.StartClient();
        if (GUILayout.Button("Server")) m_NetworkManager.StartServer();
    }

    void StatusLabels()
    {
        var mode = m_NetworkManager.IsHost ?
            "Host" : m_NetworkManager.IsServer ? "Server" : "Client";

        GUILayout.Label("Transport: " +
            m_NetworkManager.NetworkConfig.NetworkTransport.GetType().Name);
        GUILayout.Label("Mode: " + mode);
    }
}