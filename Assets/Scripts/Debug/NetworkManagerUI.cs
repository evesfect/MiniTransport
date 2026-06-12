using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkManagerUI : MonoBehaviour
{
    private NetworkManager m_NetworkManager;
    private string m_IpAddress = "127.0.0.1";

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

        GUILayout.Space(5);
        GUILayout.Label("Server IP:");
        m_IpAddress = GUILayout.TextField(m_IpAddress, GUILayout.Width(200));

        if (GUILayout.Button("Client"))
        {
            var transport = m_NetworkManager.GetComponent<UnityTransport>();
            if (transport != null)
                transport.SetConnectionData(m_IpAddress, transport.ConnectionData.Port);
            m_NetworkManager.StartClient();
        }

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