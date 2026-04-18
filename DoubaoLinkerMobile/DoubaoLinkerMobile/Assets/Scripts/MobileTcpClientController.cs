using System;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MobileTcpClientController : MonoBehaviour
{
    enum MessageFrameMode
    {
        LineDelimited,
        LengthPrefixed
    }

    [Header("UI")]
    [SerializeField] InputField serverAddressInputField;
    [SerializeField] TMP_InputField messageInputField;
    [SerializeField] Toggle connectionToggle;
    [SerializeField] Button sendButton;
    [SerializeField] Text statusText;

    [Header("Connection")]
    [SerializeField] string defaultServerAddress = "127.0.0.1:23333";
    [SerializeField] MessageFrameMode frameMode = MessageFrameMode.LineDelimited;
    [SerializeField] bool appendNewLineInLineMode = true;
    [SerializeField] bool clearMessageAfterSend;

    readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);

    TcpClient client;
    NetworkStream stream;
    bool isConnected;
    bool isBusy;
    bool suppressToggleCallback;

    void Awake()
    {
        if (serverAddressInputField != null && string.IsNullOrWhiteSpace(serverAddressInputField.text))
        {
            serverAddressInputField.text = defaultServerAddress;
        }

        SetStatus("\u672A\u8FDE\u63A5");
        UpdateUiState();
    }

    void OnEnable()
    {
        if (connectionToggle != null)
        {
            connectionToggle.onValueChanged.AddListener(OnConnectionToggleChanged);
        }

        if (sendButton != null)
        {
            sendButton.onClick.AddListener(OnSendButtonClicked);
        }

        UpdateUiState();
    }

    void OnDisable()
    {
        if (connectionToggle != null)
        {
            connectionToggle.onValueChanged.RemoveListener(OnConnectionToggleChanged);
        }

        if (sendButton != null)
        {
            sendButton.onClick.RemoveListener(OnSendButtonClicked);
        }
    }

    void OnDestroy()
    {
        Disconnect();
        sendLock.Dispose();
    }

    void OnConnectionToggleChanged(bool shouldConnect)
    {
        if (suppressToggleCallback)
        {
            return;
        }

        if (shouldConnect)
        {
            _ = ConnectFromUiAsync();
        }
        else
        {
            Disconnect();
        }
    }

    void OnSendButtonClicked()
    {
        _ = SendCurrentMessageAsync();
    }

    async Task ConnectFromUiAsync()
    {
        if (isConnected || isBusy)
        {
            return;
        }

        if (!TryParseEndpoint(serverAddressInputField != null ? serverAddressInputField.text : null, out string host, out int port))
        {
            SetStatus("\u670D\u52A1\u5668\u5730\u5740\u683C\u5F0F\u9519\u8BEF\uFF0C\u8BF7\u4F7F\u7528 host:port");
            Debug.LogError("[MobileTcpClientController] Invalid server address. Use host:port, for example 192.168.1.10:23333");
            SetToggleWithoutNotify(false);
            return;
        }

        isBusy = true;
        SetStatus($"\u6B63\u5728\u8FDE\u63A5 {host}:{port} ...");
        UpdateUiState();

        try
        {
            var tcpClient = new TcpClient
            {
                NoDelay = true
            };

            await tcpClient.ConnectAsync(host, port);

            client = tcpClient;
            stream = client.GetStream();
            isConnected = true;

            SetStatus($"\u5DF2\u8FDE\u63A5 {host}:{port}");
            Debug.Log($"[MobileTcpClientController] Connected to {host}:{port}, frameMode={frameMode}");
        }
        catch (Exception ex)
        {
            SetStatus($"\u8FDE\u63A5\u5931\u8D25: {ex.GetType().Name}");
            Debug.LogError($"[MobileTcpClientController] Connect failed. {ex}");
            Disconnect();
            SetToggleWithoutNotify(false);
        }
        finally
        {
            isBusy = false;
            UpdateUiState();
        }
    }

    async Task SendCurrentMessageAsync()
    {
        if (!isConnected || stream == null)
        {
            SetStatus("\u53D1\u9001\u5931\u8D25\uFF1A\u5F53\u524D\u672A\u8FDE\u63A5");
            Debug.LogWarning("[MobileTcpClientController] Send skipped because client is not connected.");
            return;
        }

        string text = messageInputField != null ? messageInputField.text : string.Empty;
        if (string.IsNullOrEmpty(text))
        {
            SetStatus("\u53D1\u9001\u5931\u8D25\uFF1A\u5185\u5BB9\u4E3A\u7A7A");
            Debug.LogWarning("[MobileTcpClientController] Send skipped because message is empty.");
            return;
        }

        isBusy = true;
        SetStatus($"\u6B63\u5728\u53D1\u9001 {text.Length} \u4E2A\u5B57\u7B26...");
        UpdateUiState();

        await sendLock.WaitAsync();
        try
        {
            await SendMessageAsync(text);
            SetStatus($"\u53D1\u9001\u6210\u529F\uFF1A{text.Length} \u4E2A\u5B57\u7B26");
            Debug.Log($"[MobileTcpClientController] Sent {text.Length} chars.");

            if (clearMessageAfterSend && messageInputField != null)
            {
                messageInputField.text = string.Empty;
            }
        }
        catch (Exception ex)
        {
            SetStatus($"\u53D1\u9001\u5931\u8D25: {ex.GetType().Name}");
            Debug.LogError($"[MobileTcpClientController] Send failed. {ex}");
            Disconnect();
            SetToggleWithoutNotify(false);
        }
        finally
        {
            sendLock.Release();
            isBusy = false;
            UpdateUiState();
        }
    }

    async Task SendMessageAsync(string text)
    {
        if (stream == null)
        {
            throw new InvalidOperationException("NetworkStream is not available.");
        }

        switch (frameMode)
        {
            case MessageFrameMode.LineDelimited:
            {
                string payloadText = text;
                if (appendNewLineInLineMode && !payloadText.EndsWith("\n", StringComparison.Ordinal))
                {
                    payloadText += "\n";
                }

                byte[] payload = Encoding.UTF8.GetBytes(payloadText);
                await stream.WriteAsync(payload, 0, payload.Length);
                await stream.FlushAsync();
                break;
            }
            case MessageFrameMode.LengthPrefixed:
            {
                byte[] payload = Encoding.UTF8.GetBytes(text);
                byte[] header =
                {
                    (byte)((payload.Length >> 24) & 0xFF),
                    (byte)((payload.Length >> 16) & 0xFF),
                    (byte)((payload.Length >> 8) & 0xFF),
                    (byte)(payload.Length & 0xFF)
                };

                await stream.WriteAsync(header, 0, header.Length);
                await stream.WriteAsync(payload, 0, payload.Length);
                await stream.FlushAsync();
                break;
            }
            default:
                throw new InvalidOperationException($"Unsupported frame mode: {frameMode}");
        }
    }

    void Disconnect()
    {
        bool wasConnected = isConnected;
        isConnected = false;

        try
        {
            stream?.Close();
        }
        catch
        {
        }

        try
        {
            client?.Close();
        }
        catch
        {
        }

        stream = null;
        client = null;

        SetStatus(wasConnected ? "\u5DF2\u65AD\u5F00\u8FDE\u63A5" : "\u672A\u8FDE\u63A5");
        UpdateUiState();
    }

    void UpdateUiState()
    {
        bool canEditAddress = !isConnected && !isBusy;

        if (serverAddressInputField != null)
        {
            serverAddressInputField.interactable = canEditAddress;
        }

        if (connectionToggle != null)
        {
            connectionToggle.interactable = !isBusy;
        }

        if (sendButton != null)
        {
            sendButton.interactable = isConnected && !isBusy;
        }
    }

    void SetToggleWithoutNotify(bool value)
    {
        if (connectionToggle == null)
        {
            return;
        }

        suppressToggleCallback = true;
        connectionToggle.isOn = value;
        suppressToggleCallback = false;
    }

    void SetStatus(string message)
    {
        if (statusText != null)
        {
            statusText.text = message;
        }
    }

    static bool TryParseEndpoint(string endpoint, out string host, out int port)
    {
        host = null;
        port = 0;

        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return false;
        }

        int separatorIndex = endpoint.LastIndexOf(':');
        if (separatorIndex <= 0 || separatorIndex >= endpoint.Length - 1)
        {
            return false;
        }

        host = endpoint.Substring(0, separatorIndex).Trim();
        string portText = endpoint.Substring(separatorIndex + 1).Trim();

        return
            !string.IsNullOrWhiteSpace(host) &&
            int.TryParse(portText, out port) &&
            port > 0 &&
            port <= 65535;
    }
}
