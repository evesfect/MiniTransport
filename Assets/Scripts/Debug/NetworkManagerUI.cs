using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

public class NetworkManagerUI : MonoBehaviour
{
    private NetworkManager m_NetworkManager;
    private string m_IpAddress = "127.0.0.1";
    private string m_ErrorMessage = "";

    void Awake()
    {
        m_NetworkManager = GetComponent<NetworkManager>();
    }

    void OnEnable()
    {
        m_NetworkManager.OnClientDisconnectCallback += OnDisconnected;
        m_NetworkManager.OnTransportFailure += OnTransportFailure;
    }

    void OnDisable()
    {
        m_NetworkManager.OnClientDisconnectCallback -= OnDisconnected;
        m_NetworkManager.OnTransportFailure -= OnTransportFailure;
    }

    private void OnDisconnected(ulong clientId)
    {
        // On the client side, this fires with LocalClientId when connection fails/drops
        if (!m_NetworkManager.IsServer && clientId == m_NetworkManager.LocalClientId)
        {
            m_ErrorMessage = $"Failed to connect to {m_IpAddress}";
            Debug.LogWarning($"[NetworkManagerUI] Connection failed: {m_ErrorMessage}");
        }
    }

    private void OnTransportFailure()
    {
        m_ErrorMessage = $"Transport failure connecting to {m_IpAddress}";
        Debug.LogError($"[NetworkManagerUI] {m_ErrorMessage}");
    }

    void OnGUI()
    {
        float areaWidth = 300;
        float areaHeight = 300;
        float padding = 10;
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
        if (GUILayout.Button("Host"))
        {
            m_ErrorMessage = "";
            m_NetworkManager.StartHost();
        }

        GUILayout.Space(5);
        GUILayout.Label("Server IP:");
        m_IpAddress = GUILayout.TextField(m_IpAddress, GUILayout.Width(200));

        if (GUILayout.Button("Client"))
        {
            m_ErrorMessage = "";
            var transport = m_NetworkManager.NetworkConfig.NetworkTransport as UnityTransport;
            if (transport != null)
            {
                transport.SetConnectionData(m_IpAddress, transport.ConnectionData.Port);
                Debug.Log($"[NetworkManagerUI] Connecting to {m_IpAddress}:{transport.ConnectionData.Port}");
            }
            else
            {
                Debug.LogError("[NetworkManagerUI] UnityTransport not found on NetworkManager!");
            }
            m_NetworkManager.StartClient();
        }

        if (GUILayout.Button("Server")) m_NetworkManager.StartServer();

        if (!string.IsNullOrEmpty(m_ErrorMessage))
        {
            var errorStyle = new GUIStyle(GUI.skin.label);
            errorStyle.normal.textColor = Color.red;
            GUILayout.Label(m_ErrorMessage, errorStyle);
        }
    }

    void StatusLabels()
    {
        var mode = m_NetworkManager.IsHost ?
            "Host" : m_NetworkManager.IsServer ? "Server" : "Client";

        GUILayout.Label("Transport: " +
            m_NetworkManager.NetworkConfig.NetworkTransport.GetType().Name);
        GUILayout.Label("Mode: " + mode);

        var transport = m_NetworkManager.NetworkConfig.NetworkTransport as UnityTransport;
        if (transport != null)
            GUILayout.Label($"Address: {transport.ConnectionData.Address}:{transport.ConnectionData.Port}");
    }
}