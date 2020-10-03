using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Authentication;
using UnityEngine;

namespace Mirror.SimpleWeb
{
    public class SimpleWebTransport : Transport
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        readonly bool isWebGL = true;
#else
        readonly bool isWebGL = false;
#endif

        public const string NormalScheme = "ws";
        public const string SecureScheme = "wss";

        [Tooltip("Port to use for server and client")]
        public short port = 7778;


        [Tooltip("Protect against allocation attacks by keeping the max message size small. Otherwise an attacker might send multiple fake packets with 2GB headers, causing the server to run out of memory after allocating multiple large packets.")]
        public int maxMessageSize = 16 * 1024;

        [Tooltip("disables nagle algorithm. lowers CPU% and latency but increases bandwidth")]
        public bool noDelay = true;

        [Header("Server Settings")]

        [Tooltip("Send would stall forever if the network is cut off during a send, so we need a timeout (in milliseconds)")]
        public int sendTimeout = 5000;

        [Tooltip("How long without a message before disconnecting (in milliseconds)")]
        public int receiveTimeout = 20000;

        [Tooltip("Caps the number of messages the server will process per tick. Allows LateUpdate to finish to let the reset of unity contiue incase more messages arrive before they are processed")]
        public int serverMaxMessagesPerTick = 10000;

        [Header("Ssl Settings")]
        public bool sslEnabled;
        [Tooltip("Path to json file that contains path to cert and its password\n\nUse Json file so that cert password is not included in client builds\n\nSee cert.example.Json")]
        public string sslCertJson = "./cert.Json";
        public SslProtocols sslProtocols = SslProtocols.Ssl3 | SslProtocols.Tls12;

        [Header("Debug")]
        [Tooltip("Log functions uses Conditional(\"DEBUG\") so are only included in Editor and Development builds")]
        public bool enableLogs;

        private void OnValidate()
        {
            if (maxMessageSize > ushort.MaxValue)
            {
                Debug.LogWarning($"max supported value for maxMessageSize is {ushort.MaxValue}");
                maxMessageSize = ushort.MaxValue;
            }

            Log.enabled = enableLogs;
        }

        SimpleWebClient client;
        readonly Queue<ArraySegment<byte>> clientDataQueue = new Queue<ArraySegment<byte>>();

        SimpleWebServer server;

        public override bool Available()
        {
            return isWebGL;
        }
        public override int GetMaxPacketSize(int channelId = 0)
        {
            return maxMessageSize;
        }

        private void Awake()
        {
            Log.enabled = enableLogs;
        }
        public override void Shutdown()
        {
            client?.Disconnect();
            server?.Stop();
        }

        private void LateUpdate()
        {
            ProcessMessages();
        }

        /// <summary>
        /// Processes message in server and client queues
        /// <para>Invokes OnData events allowing mirror to handle messages (Cmd/SyncVar/etc)</para>
        /// <para>Called within LateUpdate, Can be called by user to process message before important logic</para>
        /// </summary>
        public void ProcessMessages()
        {
            server?.ProcessMessageQueue(this);
            ClientUpdate();
        }

        #region Client
        string GetScheme() => sslEnabled ? SecureScheme : NormalScheme;
        public override bool ClientConnected()
        {
            return client != null && client.IsConnected;
        }

        public override void ClientConnect(string address)
        {
            if (!isWebGL)
            {
                Debug.LogError("SimpleWebTransport client is only available on WebGL");
            }

            // connecting or connected
            if (client != null)
            {
                Debug.LogError("Already Connected");
            }

            UriBuilder builder = new UriBuilder
            {
                Scheme = GetScheme(),
                Host = address,
                Port = port
            };

            client = SimpleWebClient.Create();
            if (client == null) { return; }

            client.onConnect += OnClientConnected.Invoke;
            client.onDisconnect += OnClientDisconnected.Invoke;
            client.onData += (ArraySegment<byte> data) => clientDataQueue.Enqueue(data);
            client.onError += () =>
            {
                ClientDisconnect();
                OnClientError.Invoke(new Exception("SimpleWebClient Error"));
            };


            // make sure queue is cleared in case of previous client
            clientDataQueue.Clear();

            // TODO can this just be builder.ToString()
            client.Connect(builder.Uri.ToString());
        }

        public override void ClientDisconnect()
        {
            if (client != null)
            {
                Debug.LogError("Not Connected");
            }

            client?.Disconnect();
            client = null;
        }

        public override bool ClientSend(int channelId, ArraySegment<byte> segment)
        {
            if (!ClientConnected())
            {
                Debug.LogError("Already Connected");
                return false;
            }

            if (segment.Count > maxMessageSize)
            {
                Debug.LogError("Message greater than max size");
                return false;
            }

            client.Send(segment);
            return true;
        }

        public void ClientUpdate()
        {
            while (enabled && clientDataQueue.Count > 0)
            {
                ArraySegment<byte> data = clientDataQueue.Dequeue();
                OnClientDataReceived.Invoke(data, Channels.DefaultReliable);
            }
        }
        #endregion

        #region Server
        public override bool ServerActive()
        {
            return server != null && server.Active;
        }

        public override void ServerStart()
        {
            if (isWebGL)
            {
                Debug.LogError("SimpleWebTransport server is only available on standalone");
            }

            if (ServerActive())
            {
                Debug.LogError("SimpleWebServer Already Started");
            }

            SslConfig config = SslConfigLoader.Load(this);
            server = new SimpleWebServer(port, serverMaxMessagesPerTick, noDelay, sendTimeout, receiveTimeout, maxMessageSize, config);

            server.onConnect += OnServerConnected.Invoke;
            server.onDisconnect += OnServerDisconnected.Invoke;
            server.onData += (int connId, ArraySegment<byte> data) => OnServerDataReceived.Invoke(connId, data, Channels.DefaultReliable);
            server.onError += OnServerError.Invoke;

            server.Start();
        }

        public override void ServerStop()
        {
            if (!ServerActive())
            {
                Debug.LogError("SimpleWebServer Not Active");
            }

            server.Stop();
            server = null;
        }

        public override bool ServerDisconnect(int connectionId)
        {
            if (!ServerActive())
            {
                Debug.LogError(" SimpleWebServer Not Active");
                return false;
            }

            return server.KickClient(connectionId);
        }

        public override bool ServerSend(List<int> connectionIds, int channelId, ArraySegment<byte> segment)
        {
            if (!ServerActive())
            {
                Debug.LogError("SimpleWebServer Not Active");
                return false;
            }

            if (segment.Count > maxMessageSize)
            {
                Debug.LogError("Message greater than max size");
                return false;
            }

            server.SendAll(connectionIds, segment);
            return true;
        }

        public override string ServerGetClientAddress(int connectionId)
        {
            return server.GetClientAddress(connectionId);
        }

        public override Uri ServerUri()
        {
            UriBuilder builder = new UriBuilder
            {
                Scheme = GetScheme(),
                Host = Dns.GetHostName(),
                Port = port
            };
            return builder.Uri;
        }
        #endregion
    }
}
