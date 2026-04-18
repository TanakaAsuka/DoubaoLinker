using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using TMPro;
using UnityEngine;

public class LanTextInputServer : MonoBehaviour
{
    enum MessageFrameMode
    {
        LineDelimited,
        LengthPrefixed
    }

    [Header("Server")]
    [SerializeField] bool startOnAwake = true;
    [SerializeField] int listenPort = 23333;
    [SerializeField] MessageFrameMode frameMode = MessageFrameMode.LineDelimited;
    [SerializeField] bool appendNewLineAfterEachMessage;
    [SerializeField] bool logReceivedText = true;
    [SerializeField] int maxMessageBytes = 1024 * 1024;

    readonly Queue<ReceivedMessage> pendingMessages = new Queue<ReceivedMessage>();
    readonly object pendingMessagesLock = new object();
    readonly List<TcpClient> clients = new List<TcpClient>();
    readonly object clientsLock = new object();

    TcpListener listener;
    Thread acceptThread;
    volatile bool serverRunning;

    [SerializeField] private TextMeshProUGUI text;

    void Start()
    {
        if (startOnAwake)
        {
            StartServer();
        }
    }

    void Update()
    {
        FlushPendingMessages();
    }

    void OnDestroy()
    {
        StopServer();
    }

    public void StartServer()
    {
        if (serverRunning)
        {
            return;
        }

        try
        {
            listener = new TcpListener(IPAddress.Any, listenPort);
            listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            listener.Start();

            serverRunning = true;
            acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "DoubaoLinkerAcceptLoop"
            };
            acceptThread.Start();

            Debug.Log($"[LanTextInputServer] Listening on port {listenPort}, frameMode={frameMode}. IPs: {string.Join(", ", GetLocalIPv4Addresses())}");
            text.text=$"IPs: {string.Join(", ", GetLocalIPv4Addresses())}";
        }
        catch (Exception ex)
        {
            serverRunning = false;
            Debug.LogError($"[LanTextInputServer] Failed to start listener on port {listenPort}. {ex}");
        }
    }

    public void StopServer()
    {
        serverRunning = false;

        try
        {
            listener?.Stop();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[LanTextInputServer] Error while stopping listener. {ex.Message}");
        }

        listener = null;

        lock (clientsLock)
        {
            foreach (TcpClient client in clients)
            {
                try
                {
                    client.Close();
                }
                catch
                {
                }
            }

            clients.Clear();
        }

        if (acceptThread != null && acceptThread.IsAlive && !acceptThread.Join(500))
        {
            Debug.LogWarning("[LanTextInputServer] Accept thread did not stop within 500ms.");
        }

        acceptThread = null;
    }

    void AcceptLoop()
    {
        while (serverRunning)
        {
            try
            {
                TcpClient client = listener.AcceptTcpClient();

                lock (clientsLock)
                {
                    clients.Add(client);
                }

                string remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";
                Debug.Log($"[LanTextInputServer] Client connected: {remoteEndpoint}");

                Thread clientThread = new Thread(ClientReceiveLoop)
                {
                    IsBackground = true,
                    Name = $"DoubaoLinkerClient-{remoteEndpoint}"
                };
                clientThread.Start(client);
            }
            catch (SocketException ex)
            {
                if (serverRunning)
                {
                    Debug.LogError($"[LanTextInputServer] Accept loop socket error. {ex}");
                }
            }
            catch (ObjectDisposedException)
            {
            }
            catch (Exception ex)
            {
                Debug.LogError($"[LanTextInputServer] Accept loop error. {ex}");
            }
        }
    }

    void ClientReceiveLoop(object state)
    {
        TcpClient client = (TcpClient)state;
        string remoteEndpoint = client.Client.RemoteEndPoint?.ToString() ?? "unknown";

        try
        {
            using NetworkStream stream = client.GetStream();
            while (serverRunning && client.Connected)
            {
                string message = ReadNextMessage(stream);
                if (message == null)
                {
                    break;
                }

                if (appendNewLineAfterEachMessage)
                {
                    message += "\n";
                }

                if (message.Length == 0)
                {
                    continue;
                }

                EnqueueMessage(new ReceivedMessage
                {
                    RemoteEndpoint = remoteEndpoint,
                    Text = message
                });
            }
        }
        catch (IOException)
        {
        }
        catch (Exception ex)
        {
            if (serverRunning)
            {
                Debug.LogError($"[LanTextInputServer] Client loop error for {remoteEndpoint}. {ex}");
            }
        }
        finally
        {
            RemoveClient(client);
            Debug.Log($"[LanTextInputServer] Client disconnected: {remoteEndpoint}");
        }
    }

    string ReadNextMessage(NetworkStream stream)
    {
        switch (frameMode)
        {
            case MessageFrameMode.LineDelimited:
                return ReadLineDelimitedMessage(stream);
            case MessageFrameMode.LengthPrefixed:
                return ReadLengthPrefixedMessage(stream);
            default:
                throw new InvalidOperationException($"Unsupported frame mode: {frameMode}");
        }
    }

    string ReadLineDelimitedMessage(NetworkStream stream)
    {
        using var builder = new MemoryStream();

        while (true)
        {
            int nextByte = stream.ReadByte();
            if (nextByte < 0)
            {
                if (builder.Length == 0)
                {
                    return null;
                }

                break;
            }

            if (nextByte == '\n')
            {
                break;
            }

            if (nextByte != '\r')
            {
                builder.WriteByte((byte)nextByte);
            }

            if (builder.Length > maxMessageBytes)
            {
                throw new InvalidDataException($"Line-delimited message exceeded maxMessageBytes={maxMessageBytes}.");
            }
        }

        return Encoding.UTF8.GetString(builder.ToArray());
    }

    string ReadLengthPrefixedMessage(NetworkStream stream)
    {
        byte[] lengthBytes = ReadExact(stream, 4);
        if (lengthBytes == null)
        {
            return null;
        }

        int messageLength =
            (lengthBytes[0] << 24) |
            (lengthBytes[1] << 16) |
            (lengthBytes[2] << 8) |
            lengthBytes[3];

        if (messageLength < 0 || messageLength > maxMessageBytes)
        {
            throw new InvalidDataException($"Invalid length-prefixed message length: {messageLength}. maxMessageBytes={maxMessageBytes}.");
        }

        if (messageLength == 0)
        {
            return string.Empty;
        }

        byte[] payload = ReadExact(stream, messageLength);
        if (payload == null)
        {
            throw new EndOfStreamException("Connection closed while reading a length-prefixed message payload.");
        }

        return Encoding.UTF8.GetString(payload);
    }

    byte[] ReadExact(NetworkStream stream, int byteCount)
    {
        byte[] buffer = new byte[byteCount];
        int totalRead = 0;

        while (totalRead < byteCount)
        {
            int read = stream.Read(buffer, totalRead, byteCount - totalRead);
            if (read <= 0)
            {
                if (totalRead == 0)
                {
                    return null;
                }

                return null;
            }

            totalRead += read;
        }

        return buffer;
    }

    void EnqueueMessage(ReceivedMessage message)
    {
        lock (pendingMessagesLock)
        {
            pendingMessages.Enqueue(message);
        }
    }

    void FlushPendingMessages()
    {
        while (true)
        {
            ReceivedMessage message;

            lock (pendingMessagesLock)
            {
                if (pendingMessages.Count == 0)
                {
                    return;
                }

                message = pendingMessages.Dequeue();
            }

            bool ok = SendInputMono.SendText(message.Text);
            if (logReceivedText)
            {
                Debug.Log($"[LanTextInputServer] Applied text from {message.RemoteEndpoint}, success={ok}, text={message.Text}");
            }
            else
            {
                Debug.Log($"[LanTextInputServer] Applied text from {message.RemoteEndpoint}, success={ok}, length={message.Text.Length}");
            }
        }
    }

    void RemoveClient(TcpClient client)
    {
        lock (clientsLock)
        {
            clients.Remove(client);
        }

        try
        {
            client.Close();
        }
        catch
        {
        }
    }

    static string[] GetLocalIPv4Addresses()
    {
        var addresses = new List<string>();

        foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
        {
            if (address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
            {
                addresses.Add(address.ToString());
            }
        }

        if (addresses.Count == 0)
        {
            addresses.Add("127.0.0.1");
        }

        return addresses.ToArray();
    }

    struct ReceivedMessage
    {
        public string RemoteEndpoint;
        public string Text;
    }
}
