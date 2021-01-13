using RosMessageGeneration;
using RosMessageTypes.RosTcpEndpoint;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;
using UnityEngine.Serialization;

public class ROSConnection : MonoBehaviour
{
    // Variables required for ROS communication
    [FormerlySerializedAs("hostName")]
    public string rosIPAddress = "127.0.0.1";
    [FormerlySerializedAs("hostPort")]
    public int rosPort = 10000;

    [Tooltip("If blank, determine IP automatically.")]
    public string overrideUnityIP = "";
    public int unityPort = 5005;
    [Tooltip("Try to keep the connections open")]
    public bool keepConnections = true;
    bool alreadyStartedServer = false;

    TcpListener tcpListener;
    [Tooltip("Network tiemout (in ms)")]
    public int networkTimeout = 2000;

    [Tooltip("While waiting for a service to respond, check this many times before giving up.")]
    public int awaitDataMaxRetries = 10;
    [Tooltip("While waiting for a service to respond, wait this many seconds between checks.")]
    public float awaitDataSleepSeconds = 1.0f;


    // Variables required to keep the publishers connection open
    TcpClient persistantPublisherClient;
    NetworkStream persistantPublisherNetworkStream;

    static object _lock = new object(); // sync lock 
    static readonly SemaphoreSlim _openPublisherConnectionAsyncLock = new SemaphoreSlim(1, 1);
    // Cancellation token for publisher connection
    static CancellationTokenSource publisherTokenStore = new CancellationTokenSource();
    static List<Task> activeConnectionTasks = new List<Task>(); // pending connections

    const string ERROR_TOPIC_NAME = "__error";
    const string SYSCOMMAND_TOPIC_NAME = "__syscommand";
    const string HANDSHAKE_TOPIC_NAME = "__handshake";

    const string SYSCOMMAND_SUBSCRIBE = "subscribe";
    const string SYSCOMMAND_PUBLISH = "publish";
    const string SYSCOMMAND_CONNECTIONS_PARAMETERS = "connections_parameters";

    Dictionary<string, SubscriberCallback> subscribers = new Dictionary<string, SubscriberCallback>();

    struct SubscriberCallback
    {
        public ConstructorInfo messageConstructor;
        public List<Action<Message>> callbacks;
    }

    public void Subscribe<T>(string topic, Action<T> callback) where T : Message, new()
    {
        SubscriberCallback subCallbacks;
        if (!subscribers.TryGetValue(topic, out subCallbacks))
        {
            subCallbacks = new SubscriberCallback
            {
                messageConstructor = typeof(T).GetConstructor(new Type[0]),
                callbacks = new List<Action<Message>> { }
            };
            subscribers.Add(topic, subCallbacks);
        }

        subCallbacks.callbacks.Add((Message msg) => { callback((T)msg); });
    }

    public async void SendServiceMessage<RESPONSE>(string rosServiceName, Message serviceRequest, Action<RESPONSE> callback) where RESPONSE : Message, new()
    {
        // Serialize the message in service name, message size, and message bytes format
        byte[] messageBytes = GetMessageBytes(rosServiceName, serviceRequest);

        TcpClient client = new TcpClient();
        await client.ConnectAsync(rosIPAddress, rosPort);

        NetworkStream networkStream = client.GetStream();
        networkStream.ReadTimeout = networkTimeout;

        RESPONSE serviceResponse = new RESPONSE();

        // Send the message
        try
        {
            networkStream.Write(messageBytes, 0, messageBytes.Length);
        }
        catch (Exception e)
        {
            Debug.LogError("SocketException: " + e);
            goto finish;
        }

        if (!networkStream.CanRead)
        {
            Debug.LogError("Sorry, you cannot read from this NetworkStream.");
            goto finish;
        }

        // Poll every 1 second(s) for available data on the stream
        int attempts = 0;
        while (!networkStream.DataAvailable && attempts <= this.awaitDataMaxRetries)
        {
            if (attempts == this.awaitDataMaxRetries)
            {
                Debug.LogError("No data available on network stream after " + awaitDataMaxRetries + " attempts.");
                goto finish;
            }
            attempts++;
            await Task.Delay((int)(awaitDataSleepSeconds * 1000));
        }

        int numberOfBytesRead = 0;
        try
        {
            // Get first bytes to determine length of service name
            byte[] rawServiceBytes = new byte[4];
            networkStream.Read(rawServiceBytes, 0, rawServiceBytes.Length);
            int topicLength = BitConverter.ToInt32(rawServiceBytes, 0);

            // Create container and read service name from network stream
            byte[] serviceNameBytes = new byte[topicLength];
            networkStream.Read(serviceNameBytes, 0, serviceNameBytes.Length);
            string serviceName = Encoding.ASCII.GetString(serviceNameBytes, 0, topicLength);

            // Get leading bytes to determine length of remaining full message
            byte[] full_message_size_bytes = new byte[4];
            networkStream.Read(full_message_size_bytes, 0, full_message_size_bytes.Length);
            int full_message_size = BitConverter.ToInt32(full_message_size_bytes, 0);

            // Create container and read message from network stream
            byte[] readBuffer = new byte[full_message_size];
            while (networkStream.DataAvailable && numberOfBytesRead < full_message_size)
            {
                var readBytes = networkStream.Read(readBuffer, 0, readBuffer.Length);
                numberOfBytesRead += readBytes;
            }

            serviceResponse.Deserialize(readBuffer, 0);
        }
        catch (Exception e)
        {
            Debug.LogError("Exception raised!! " + e);
        }

        finish:
        callback(serviceResponse);
        if (client.Connected)
            client.Close();
    }

    public void RegisterSubscriber(string topic, string rosMessageName)
    {
        SendSysCommand(SYSCOMMAND_SUBSCRIBE, new SysCommand_Subscribe { topic = topic, message_name = rosMessageName });
    }

    public void RegisterPublisher(string topic, string rosMessageName)
    {
        SendSysCommand(SYSCOMMAND_PUBLISH, new SysCommand_Publish { topic = topic, message_name = rosMessageName });
    }

    private static ROSConnection _instance;
    public static ROSConnection instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject prefab = Resources.Load<GameObject>("ROSConnectionPrefab");
                if (prefab == null)
                {
                    Debug.LogWarning("No settings for ROSConnection.instance! Open \"ROS Settings\" from the Robotics menu to configure it.");
                    GameObject instance = new GameObject("ROSConnection");
                    _instance = instance.AddComponent<ROSConnection>();
                }
                else
                {
                    Instantiate(prefab);
                }
            }
            return _instance;
        }
    }

    void OnEnable()
    {
        if (_instance == null)
            _instance = this;
    }

    private void Start()
    {
        Subscribe<RosUnityError>(ERROR_TOPIC_NAME, RosUnityErrorCallback);

        if (overrideUnityIP != "")
        {
            StartMessageServer(overrideUnityIP, unityPort); // no reason to wait, if we already know the IP
        }

        SendServiceMessage<UnityHandshakeResponse>(HANDSHAKE_TOPIC_NAME, new UnityHandshakeRequest(overrideUnityIP, (ushort)unityPort), RosUnityHandshakeCallback);
    }

    void RosUnityHandshakeCallback(UnityHandshakeResponse response)
    {
        StartMessageServer(response.ip, unityPort);
    }

    void RosUnityErrorCallback(RosUnityError error)
    {
        Debug.LogError("ROS-Unity error: " + error.message);
    }

    public async void SendServiceMessage<RESPONSE>(string rosServiceName, Message serviceRequest, Action<RESPONSE> callback) where RESPONSE : Message, new()
    {
        // Serialize the message in service name, message size, and message bytes format
        byte[] messageBytes = GetMessageBytes(rosServiceName, serviceRequest);

        NetworkStream networkStream = null;
        TcpClient client = null;

        RESPONSE serviceResponse = new RESPONSE();

        // Send the message
        try
        {
            if (keepConnections)
            {
                await Connect();
                client = persistantPublisherClient;
                networkStream = persistantPublisherNetworkStream;
            }
            else
            {
                client = new TcpClient();
                await client.ConnectAsync(hostName, hostPort);

                networkStream = client.GetStream();
                networkStream.ReadTimeout = networkTimeout;
            }

            networkStream.Write(messageBytes, 0, messageBytes.Length);
        }
        catch (OperationCanceledException)
        {
            // The operation was cancelled
        }
        catch (Exception e)
        {
            Debug.LogError("SocketException: " + e);
            goto finish;
        }

        if (!networkStream.CanRead)
        {
            Debug.LogError("Sorry, you cannot read from this NetworkStream.");
            goto finish;
        }

        // Poll every 1 second(s) for available data on the stream
        int attempts = 0;
        while (!networkStream.DataAvailable && attempts <= this.awaitDataMaxRetries)
        {
            if (attempts == this.awaitDataMaxRetries)
            {
                Debug.LogError("No data available on network stream after " + awaitDataMaxRetries + " attempts.");
                goto finish;
            }
            attempts++;
            await Task.Delay((int)(awaitDataSleepSeconds * 1000));
        }

        int numberOfBytesRead = 0;
        try
        {
            // Get first bytes to determine length of service name
            byte[] rawServiceBytes = new byte[4];
            networkStream.Read(rawServiceBytes, 0, rawServiceBytes.Length);
            int topicLength = BitConverter.ToInt32(rawServiceBytes, 0);

            // Create container and read service name from network stream
            byte[] serviceNameBytes = new byte[topicLength];
            networkStream.Read(serviceNameBytes, 0, serviceNameBytes.Length);
            string serviceName = Encoding.ASCII.GetString(serviceNameBytes, 0, topicLength);

            // Get leading bytes to determine length of remaining full message
            byte[] full_message_size_bytes = new byte[4];
            networkStream.Read(full_message_size_bytes, 0, full_message_size_bytes.Length);
            int full_message_size = BitConverter.ToInt32(full_message_size_bytes, 0);

            // Create container and read message from network stream
            byte[] readBuffer = new byte[full_message_size];
            while (networkStream.DataAvailable && numberOfBytesRead < full_message_size)
            {
                var readBytes = networkStream.Read(readBuffer, 0, readBuffer.Length);
                numberOfBytesRead += readBytes;
            }

            serviceResponse.Deserialize(readBuffer, 0);
        }
        catch (Exception e)
        {
            Debug.LogError("Exception raised!! " + e);
        }

        finish:
        callback(serviceResponse);
        if (!keepConnections && client.Connected)
            client.Close();
    }

    void Awake()
    {
        Subscribe<RosUnityError>(ERROR_TOPIC_NAME, RosUnityErrorCallback);

        if (overrideUnityIP != "")
        {
            StartMessageServer(overrideUnityIP, unityPort); // no reason to wait, if we already know the IP
        }
        // Must be send first as it may change how connections are handled
        SendSysCommand(SYSCOMMAND_CONNECTIONS_PARAMETERS, new SysCommand_ConnectionsParameters { keep_connections = this.keepConnections, timeout_in_s = networkTimeout / 1000.0f });

        SendServiceMessage<UnityHandshakeResponse>(HANDSHAKE_TOPIC_NAME, new UnityHandshakeRequest(overrideUnityIP, (ushort)unityPort), RosUnityHandshakeCallback);

    }

    void RosUnityHandshakeCallback(UnityHandshakeResponse response)
    {
        StartMessageServer(response.ip, unityPort);
    }

    void RosUnityErrorCallback(RosUnityError error)
    {
        Debug.LogError("ROS-Unity error: " + error.message);
    }

    /// <summary>
    /// 	Function is meant to be overridden by inheriting classes to specify how to handle read messages.
    /// </summary>
    /// <param name="tcpClient"></param> TcpClient to read byte stream from.
    protected async Task HandleConnectionAsync(TcpClient tcpClient)
    {
        await Task.Yield();
        // continue asynchronously on another threads

        ReadMessage(tcpClient.GetStream());
    }

    void ReadMessage(NetworkStream networkStream)
    {
        try
        {
            if (networkStream.CanRead)
            {
                int offset = 0;

                // Get first bytes to determine length of topic name
                byte[] rawTopicBytes = new byte[4];
                networkStream.Read(rawTopicBytes, 0, rawTopicBytes.Length);
                offset += 4;
                int topicLength = BitConverter.ToInt32(rawTopicBytes, 0);

                // Read and convert topic name
                byte[] topicNameBytes = new byte[topicLength];
                networkStream.Read(topicNameBytes, 0, topicNameBytes.Length);
                offset += topicNameBytes.Length;
                string topicName = Encoding.ASCII.GetString(topicNameBytes, 0, topicLength);
                // TODO: use topic name to confirm proper received location

                byte[] full_message_size_bytes = new byte[4];
                networkStream.Read(full_message_size_bytes, 0, full_message_size_bytes.Length);
                offset += 4;
                int full_message_size = BitConverter.ToInt32(full_message_size_bytes, 0);

                byte[] readBuffer = new byte[full_message_size];
                int numberOfBytesRead = 0;

                while (networkStream.DataAvailable && numberOfBytesRead < full_message_size)
                {
                    int bytesRead = networkStream.Read(readBuffer, 0, readBuffer.Length);
                    offset += bytesRead;
                    numberOfBytesRead += bytesRead;
                }

                SubscriberCallback subs;
                if (subscribers.TryGetValue(topicName, out subs))
                {
                    Message msg = (Message)subs.messageConstructor.Invoke(new object[0]);
                    msg.Deserialize(readBuffer, 0);
                    foreach (Action<Message> callback in subs.callbacks)
                    {
                        callback(msg);
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Exception raised!! " + e);
        }
    }

    /// <summary>
    /// 	Handles multiple connections and locks.
    /// </summary>
    /// <param name="tcpClient"></param> TcpClient to read byte stream from.
    private async Task StartHandleConnectionAsync(TcpClient tcpClient)
    {
        var connectionTask = HandleConnectionAsync(tcpClient);

        lock (_lock)
            activeConnectionTasks.Add(connectionTask);

        try
        {
            await connectionTask;
            // we may be on another thread after "await"
        }
        catch (Exception ex)
        {
            Debug.LogError(ex.ToString());
        }
        finally
        {
            lock (_lock)
                activeConnectionTasks.Remove(connectionTask);
        }
    }

    TcpListener tcpListener;

    protected async void StartMessageServer(string ip, int port)
    {
        if (alreadyStartedServer)
            return;

        alreadyStartedServer = true;
        while (true)
        {
            try
            {
                if (!Application.isPlaying)
                    break;
                tcpListener = new TcpListener(IPAddress.Parse(ip), port);
                tcpListener.Start();

                Debug.Log("ROS-Unity server listening on " + ip + ":" + port);

                while (tcpListener != null)   //we wait for a connection
                {
                    var tcpClient = await tcpListener.AcceptTcpClientAsync();

                    var task = StartHandleConnectionAsync(tcpClient);
                    // if already faulted, re-throw any error on the calling context
                    if (task.IsFaulted)
                        await task;

                    // try to get through the message queue before doing another await
                    // but if messages are arriving faster than we can process them, don't freeze up
                    float abortAtRealtime = Time.realtimeSinceStartup + 0.1f;
                    while (tcpListener != null && tcpListener.Pending() && Time.realtimeSinceStartup < abortAtRealtime)
                    {
                        tcpClient = tcpListener.AcceptTcpClient();
                        task = StartHandleConnectionAsync(tcpClient);
                        if (task.IsFaulted)
                            await task;
                    }
                }
            }
            catch (ObjectDisposedException e)
            {
                if (!Application.isPlaying)
                {
                    // This only happened because we're shutting down. Not a problem.
                }
                else
                {
                    Debug.LogError("Exception raised!! " + e);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Exception raised!! " + e);
            }

            // to avoid infinite loops, wait a frame before trying to restart the server
            await Task.Yield();
        }
    }

    private void OnApplicationQuit()
    {
        // Cancel publishing related tasks
        publisherTokenStore.Cancel();
        if (tcpListener != null)
            tcpListener.Stop();
        tcpListener = null;
        if (persistantPublisherClient != null)
        {
            if (persistantPublisherClient.Connected)
                persistantPublisherClient.Close();
            persistantPublisherClient = null;
        }
    }


    /// <summary>
    ///    Given some input values, fill a byte array in the desired format to use with
    ///     https://github.com/Unity-Technologies/Robotics-Tutorials/tree/master/catkin_ws/src/tcp_endpoint
    ///
    /// 	All messages are expected to come in the format of:
    /// 		first four bytes: int32 of the length of following string value
    /// 		next N bytes determined from previous four bytes: ROS topic name as a string
    /// 		next four bytes: int32 of the length of the remaining bytes for the ROS Message
    /// 		last N bytes determined from previous four bytes: ROS Message variables
    /// </summary>
    /// <param name="offset"></param> Index of where to start writing output data
    /// <param name="serviceName"></param> The name of the ROS service or topic that the message data is meant for
    /// <param name="fullMessageSizeBytes"></param> The full size of the already serialized message in bytes
    /// <param name="messageToSend"></param> The serialized ROS message to send to ROS network
    /// <returns></returns>
    public int GetPrefixBytes(int offset, byte[] serviceName, byte[] fullMessageSizeBytes, byte[] messagBuffer)
    {
        // Service Name bytes
        System.Buffer.BlockCopy(serviceName, 0, messagBuffer, 0, serviceName.Length);
        offset += serviceName.Length;

        // Full Message size bytes
        System.Buffer.BlockCopy(fullMessageSizeBytes, 0, messagBuffer, offset, fullMessageSizeBytes.Length);
        offset += fullMessageSizeBytes.Length;

        return offset;
    }

    /// <summary>
    ///    Serialize a ROS message in the expected format of
    ///     https://github.com/Unity-Technologies/Robotics-Tutorials/tree/master/catkin_ws/src/tcp_endpoint
    ///
    /// 	All messages are expected to come in the format of:
    /// 		first four bytes: int32 of the length of following string value
    /// 		next N bytes determined from previous four bytes: ROS topic name as a string
    /// 		next four bytes: int32 of the length of the remaining bytes for the ROS Message
    /// 		last N bytes determined from previous four bytes: ROS Message variables
    /// </summary>
    /// <param name="topicServiceName"></param> The ROS topic or service name that is receiving the messsage
    /// <param name="message"></param> The ROS message to send to a ROS publisher or service
    /// <returns> byte array with serialized ROS message in appropriate format</returns>
    public byte[] GetMessageBytes(string topicServiceName, Message message)
    {
        byte[] topicName = message.SerializeString(topicServiceName);
        byte[] bytesMsg = message.Serialize();
        byte[] fullMessageSizeBytes = BitConverter.GetBytes(bytesMsg.Length);

        byte[] messageBuffer = new byte[topicName.Length + fullMessageSizeBytes.Length + bytesMsg.Length];
        // Copy topic name and message size in bytes to message buffer
        int offset = GetPrefixBytes(0, topicName, fullMessageSizeBytes, messageBuffer);
        // ROS message bytes
        System.Buffer.BlockCopy(bytesMsg, 0, messageBuffer, offset, bytesMsg.Length);

        return messageBuffer;
    }

    struct SysCommand_Subscribe
    {
        public string topic;
        public string message_name;
    }

    struct SysCommand_Publish
    {
        public string topic;
        public string message_name;
    }

    struct SysCommand_ConnectionsParameters
    {
        public bool keep_connections;
        public float timeout_in_s;
    }

    void SendSysCommand(string command, object param)
    {
        Send(SYSCOMMAND_TOPIC_NAME, new RosUnitySysCommand(command, JsonUtility.ToJson(param)));
    }

    protected async Task Connect()
    {
        // prevent concurrent persistant connection opening
        await _openPublisherConnectionAsyncLock.WaitAsync(publisherTokenStore.Token);
        try
        {
            publisherTokenStore.Token.ThrowIfCancellationRequested();
            if (persistantPublisherNetworkStream == null || persistantPublisherClient == null || !persistantPublisherClient.Connected)
            {
                persistantPublisherClient = new TcpClient();
                Debug.Log("Connecting persistent publisher client ...");
                await persistantPublisherClient.ConnectAsync(hostName, hostPort);
                persistantPublisherNetworkStream = persistantPublisherClient.GetStream();
                persistantPublisherNetworkStream.ReadTimeout = networkTimeout;
                Debug.Log("Connected persistent publisher client");
            }
        }

        finally
        {
            _openPublisherConnectionAsyncLock.Release();
        }
    }

    public async void Send(string rosTopicName, Message message)
    {
        TcpClient client = null;
        try
        {
            NetworkStream networkStream = null;
            if (keepConnections)
            {
                await Connect();
                client = persistantPublisherClient;
                networkStream = persistantPublisherNetworkStream;
            }
            else
            {
                client = new TcpClient();
                await client.ConnectAsync(hostName, hostPort);

                networkStream = client.GetStream();
                networkStream.ReadTimeout = networkTimeout;
            }
            WriteDataStaggered(networkStream, rosTopicName, message);
        }
        catch (OperationCanceledException)
        {
            // The operation was cancelled
        }
        catch (NullReferenceException e)
        {
            Debug.LogError("TCPConnector.SendMessage Null Reference Exception: " + e);
        }
        catch (Exception e)
        {
            Debug.LogError("TCPConnector Exception: " + e);
        }
        finally
        {
            if (!keepConnections && client != null && client.Connected)
            {
                try
                {
                    client.Close();
                }
                catch (Exception)
                {
                    //Ignored.
                }
            }
        }
    }

    /// <summary>
    ///    Serialize a ROS message in the expected format of
    ///     https://github.com/Unity-Technologies/Robotics-Tutorials/tree/master/catkin_ws/src/tcp_endpoint
    ///
    /// 	All messages are expected to come in the format of:
    /// 		first four bytes: int32 of the length of following string value
    /// 		next N bytes determined from previous four bytes: ROS topic name as a string
    /// 		next four bytes: int32 of the length of the remaining bytes for the ROS Message
    /// 		last N bytes determined from previous four bytes: ROS Message variables
    /// </summary>
    /// <param name="networkStream"></param> The network stream that is transmitting the messsage
    /// <param name="rosTopicName"></param> The ROS topic or service name that is receiving the messsage
    /// <param name="message"></param> The ROS message to send to a ROS publisher or service
    private void WriteDataStaggered(NetworkStream networkStream, string rosTopicName, Message message)
    {
        byte[] topicName = message.SerializeString(rosTopicName);
        List<byte[]> segments = message.SerializationStatements();
        int messageLength = segments.Select(s => s.Length).Sum();
        byte[] fullMessageSizeBytes = BitConverter.GetBytes(messageLength);

        networkStream.Write(topicName, 0, topicName.Length);
        networkStream.Write(fullMessageSizeBytes, 0, fullMessageSizeBytes.Length);
        foreach (byte[] segmentData in segments)
        {
            networkStream.Write(segmentData, 0, segmentData.Length);
        }
    }
}
