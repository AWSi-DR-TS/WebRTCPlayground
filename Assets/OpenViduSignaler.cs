// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using Unity3dAzure.WebSockets;
using System.Collections.Specialized;
using OpenVidu;
using Newtonsoft.Json;
using Retrofit;
using Demo.Scripts;
using UniRx;
using System.Threading;

namespace Microsoft.MixedReality.WebRTC.Unity
{

    public class OpenViduSignaler : Signaler, IDataReceiver
    {



        /// <summary>
        /// Automatically log all errors to the Unity console.
        /// </summary>
        [Tooltip("Automatically log all errors to the Unity console")]
        public bool AutoLogErrors = true;
        private OpenViduJoinRoomAnswer joinRoomAnswer;

        /// <summary>
        /// Unique identifier of the local peer.
        /// </summary>
        [Tooltip("Unique identifier of the local peer")]
        public string LocalPeerId;



        /// <summary>
        /// Unique identifier of the remote peer.
        /// </summary>
        [Tooltip("Unique identifier of the remote peer")]
        public string RemotePeerId;

        /// <summary>
        /// The Open vidu server to connect to
        /// </summary>
        [Header("Server")]
        [Tooltip("The server to connect to")]
        public string Server = "127.0.0.1";

        [Tooltip("The secret")]
        public string Secret = "secret";

        [Tooltip("The room")]
        public string Room = "room";

        /// <summary>
        /// The interval (in ms) that the server is polled at
        /// </summary>
        [Tooltip("The interval (in ms) that the server is polled at")]
        public float PollTimeMs = 500f;



        /// <summary>
        /// Internal timing helper
        /// </summary>
        private float timeSincePollMs = 0f;

        /// <summary>
        /// Internal last poll response status flag
        /// </summary>
        private bool lastGetComplete = true;
        private string EncodedSecret;


        private UnityWebSocket webSocket;
        private long idMessage = 0;
        private SdpMessage lastOffer;
        #region ISignaler interface


        private OrderedDictionary messages;
        private OpenViduSessionInfo session;
        private SdpMessage sdpAnswer;

        private SdpMessage sdpAnswerReceiveVideo;
        private bool startConnection = false;

        private IZweitblickInterface httpService;



        /// <inheritdoc/>
        public override Task SendMessageAsync(SdpMessage message)
        {

            //Debug.Log("<color=cyan>SdpMessage</color>: " + message.Content);
            if (message.Type == SdpMessageType.Offer)
                lastOffer = message;

            long i = idMessage++;
            var rpcMessage = "{\"jsonrpc\": \"2.0\"," +
                "\"method\": \"publishVideo\", " +
                "\"params\": { " +
                "\"sdpOffer\": \"" +
                message.Content +
                "\"," +
                "\"doLoopback\": false," +
                "\"hasAudio\": false," +
                "\"hasVideo\": true," +
                "\"audioActive\": false," +
                "\"videoActive\": true," +
                "\"typeOfVideo\": \"CAMERA\"," +
                "\"frameRate\": 30," +
                "\"videoDimensions\": \"{\\\"width\\\":640,\\\"height\\\":480}\"" + //TODO setup video dimensions according to capabilites
                "}, \"id\": " +
               i +
                " }";

            //Debug.Log("SdpMessage: " + rpcMessage);

            webSocket.SendText(rpcMessage);

            Debug.Log("---> added publish video message");
            messages.Add(i, OpenViduType.PublishVideo);



            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(true);

            return tcs.Task;
            //return SendMessageImplAsync(new OpenViduMessage(message));
        }

        /// <inheritdoc/>
        public override Task SendMessageAsync(IceCandidate candidate)
        {

            long i = idMessage++;
            string iceMessage = "{\"jsonrpc\": \"2.0\"," +
                "\"method\": \"onIceCandidate\", " +
                "\"params\": { " +
                "\"endpointName\":\"" + this.LocalPeerId + "\"," +
                "\"candidate\": \"" + candidate.Content + "\"," +
                "\"sdpMid\": \"" + candidate.SdpMid + "\"," +
                "\"sdpMLineIndex\": " + candidate.SdpMlineIndex +
                "}, \"id\": " + i + " }";
            //Debug.Log("<color=cyan>IceCandidate:</color> " + iceMessage);
            webSocket.SendText(iceMessage);

            Debug.Log("---> added on ice candidate message");
            messages.Add(i, OpenViduType.OnIceCandidate);
            var tcs = new TaskCompletionSource<bool>();
            tcs.SetResult(true);

            return tcs.Task;
        }

        #endregion

        #region IDataReceiver interface
        private enum OpenViduType
        {
            Ping,
            JoinRoom,
            PublishVideo,
            VideoData,
            PrepareRecieveVideoFrom,
            ReceiveVideoFrom,
            OnIceCandidate,
            UnpublishVideo,
            UnsubscripeFromVideo,
            LeaveRoom,
            SendMessage,
            ForceUnpublish,
            ForceDisconnect,
            ApplyFilter,
            RemoveFilter,
            ExecFilterMethod,
            AddFilterEventListener,
            RemoveFilterEventListener,
            Connect,
            ReconnectStream
        }




        public void OnReceivedData(object sender, EventArgs args)
        {
            if (args == null)
            {
                return;
            }

            // return early if wrong type of EventArgs
            var myArgs = args as TextEventArgs;
            if (myArgs == null)
            {

                Debug.Log("Got somethin elseg from ws:" + args.ToString());
                return;
            }

            var json = myArgs.Text;

            var msg = JsonConvert.DeserializeObject<OpenViduMessageJson>(json);

            Debug.Log("<color=red>json: </color>" + json);
            Debug.Log("<color=green>message id: </color>" + msg.id);
            Debug.Log("<color=green>message jsonRPC: </color>" + msg.Jsonrpc);
            Debug.Log("<color=green>message method: </color>" + msg.Method);

            // if the message is good
            if (msg != null)
            {

                if (!String.IsNullOrEmpty(msg.Method))
                {

                    if (msg.Method.Equals("iceCandidate"))
                    {
                        OpenViduIceCandidateEvent msg2 = JsonConvert.DeserializeObject<OpenViduIceCandidateEvent>(json);
                        var ic = new IceCandidate
                        {
                            SdpMid = msg2.Params.SdpMid,
                            SdpMlineIndex = msg2.Params.SdpMLineIndex,
                            Content = msg2.Params.Candidate,

                        };
                        //Debug.Log("<color=white>IceCandidate</color>(SdpMid=" + ic.SdpMid +
                            //", SdpMlineIndex=" + ic.SdpMlineIndex +
                            //", Content=" + ic.Content +
                            //")");
                        _nativePeer.AddIceCandidate(ic);


                    }
                    //else
                        //Debug.Log("<color=red>" + json + "</color>");

                }
                else if (messages.Contains(msg.id))
                {
                    //var id = Int32.Parse(msg.Id);
                    long id = msg.id;
                    OpenViduType messageType = (OpenViduType)messages[id];


                    switch (messageType)
                    {
                        case OpenViduType.Ping:
                            break;
                        case OpenViduType.JoinRoom:
                            joinRoomAnswer = JsonConvert.DeserializeObject<OpenViduJoinRoomAnswer>(json);

                            Debug.Log("<color=blue>joinRoomAnswer: </color>" + json);
                            Debug.Log("<color=blue>connection id: </color>" + joinRoomAnswer.result.id);
                            Debug.Log("<color=blue>session id: </color>" + joinRoomAnswer.result.sessionId);
                            Debug.Log("<color=blue>session: </color>" + joinRoomAnswer.result.session);


                            LocalPeerId = joinRoomAnswer.result.id;

                            startConnection = true;

                            break;
                        case OpenViduType.PublishVideo:
                            //Debug.Log("<color=yellow>" + json + "</color>");
                            var msg2 = JsonConvert.DeserializeObject<OpenViduPublishVideoAnswer>(json);

                            Debug.Log("<color=yellow>publish video answer: </color>" + json);
                            Debug.Log("<color=yellow>sdp answer: </color>" + msg2.Result.SdpAnswer);
                            Debug.Log("<color=yellow>session id: </color>" + msg2.Result.SessionId);

                            sdpAnswer = new WebRTC.SdpMessage { Type = SdpMessageType.Answer, Content = msg2.Result.SdpAnswer };

                            break;
                        case OpenViduType.ReceiveVideoFrom:
                            //Debug.Log("<color=yellow>" + json + "</color>");
                            var msg3 = JsonConvert.DeserializeObject<OpenViduReceiveVideoAnswer>(json);

                            Debug.Log("<color=orange>publish video answer: </color>" + json);
                            Debug.Log("<color=orange>sdp answer: </color>" + msg3.Result.SdpAnswer);
                            Debug.Log("<color=orange>session id: </color>" + msg3.Result.SessionId);

                            sdpAnswerReceiveVideo = new WebRTC.SdpMessage { Type = SdpMessageType.Answer, Content = msg3.Result.SdpAnswer };

                            _mainThreadWorkQueue.Enqueue(() =>
                            {
                                PeerConnection.HandleConnectionMessageAsync(sdpAnswerReceiveVideo);
                                /*PeerConnection.HandleConnectionMessageAsync(sdpAnswerReceiveVideo).ContinueWith(_ =>
                                {
                                    _nativePeer.CreateAnswer(); //this only works if local video is not published
                                }, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.RunContinuationsAsynchronously);*/
                            });

                            break;
                        case OpenViduType.OnIceCandidate:
                            msg = JsonConvert.DeserializeObject<OpenViduOnIceCandidateAnswer>(json);
                            break;
                        default:
                            break;
                    }

                    timeSincePollMs = PollTimeMs + 1f; //fast forward next request
                }
            }
            else if (AutoLogErrors)
            {
                //Debug.LogError($"Failed to deserialize JSON message : {json}");
            }



        }
        #endregion

        #region Unity lifecycle

        protected override void OnEnable()
        {
            base.OnEnable();

            DataHandler.OnReceivedData += OnReceivedData;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            DataHandler.OnReceivedData -= OnReceivedData;
        }

        #endregion



        /// <summary>
        /// Unity Engine Start() hook
        /// </summary>
        /// <remarks>
        /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.Start.html
        /// </remarks>
        private void Start()
        {


            if (string.IsNullOrEmpty(Secret))
            {
                throw new ArgumentNullException("Secret");
            }

            byte[] bytesToEncode = Encoding.UTF8.GetBytes("OPENVIDUAPP:" + Secret);
            EncodedSecret = Convert.ToBase64String(bytesToEncode);


            if (string.IsNullOrEmpty(Server))
            {
                throw new ArgumentNullException("ServerAddress");
            }


            // If not explicitly set, default local ID to some unique ID generated by Unity
            if (string.IsNullOrEmpty(LocalPeerId))
            {
                LocalPeerId = SystemInfo.deviceName;
            }

            messages = new OrderedDictionary();

            // Retrofit implementation
            RetrofitAdapter adapter = new RetrofitAdapter.Builder()
         .SetEndpoint("https://zweitblick.awsi.cloud:7443")
         .Build();
            httpService = adapter.Create<IZweitblickInterface>();

            var ob = httpService.GetKeycloakAccessToken("zweitblick_client", "password", "openid", "glass_user", "4MHIyOZq$NU=TchA<PGZ");
            ob.SubscribeOn(Scheduler.ThreadPool)//send request in sub thread
                 .ObserveOn(Scheduler.MainThread)//receive response in main thread
                 .Subscribe(data =>
                 {
                     // onSuccess
                     Debug.Log("Fetched keycloak token. Now fetching open vidu token...");

                     RetrofitAdapter openViduAdapter = new RetrofitAdapter.Builder()
         .SetEndpoint("https://zweitblick.awsi.cloud:9001")
         .Build();
                     IZweitblickInterface openViduService = openViduAdapter.Create<IZweitblickInterface>();

                     var openViduRequest = openViduService.GetOpenViduToken("Bearer" + data.accessToken, new OpenViduTokenBody(Room));

                     openViduRequest.SubscribeOn(Scheduler.ThreadPool)
                     .ObserveOn(Scheduler.MainThread)
                     .Subscribe(openViduResponse =>
                     {
                         string token = openViduResponse.response.token;
                         Debug.Log("Open vidu response status: " + openViduResponse.status);
                         Debug.Log("Open vidu token: " + token);

                         StartCoroutine(Connect(token));

                     }, error =>
                     {
                         Debug.Log("Open vidu token error: " + error);
                     }
                     );
                 },
                     error =>
                     {
                         Debug.Log("Retrofit Error:" + error);
                     }
                     )
                 ;




        }

        private void Connection_IceStateChanged(IceConnectionState newState)
        {
            //Debug.LogWarning("IceGatheringStateChanged");
        }

        private void Connection_IceGatheringStateChanged(IceGatheringState newState)
        {
            //Debug.LogWarning("IceGatheringStateChanged");

        }

        private void Connection_RenegotiationNeeded()
        {
            //Debug.LogWarning("RenegotiationNEeded");
        }

        public void OnInitialized()
        {
            //Debug.Log("<color=pink>OnInitialized</color>");
        }

        public void OnShutdown()
        {
            //Debug.Log("<color=pink>OnShutdown</color>");
        }

        public void OnError(string s)
        {
            //Debug.Log("<color=pink>OnError </color>" + s);
        }


        private IEnumerator Connect(string token)
        {
        //    var www = UnityWebRequest.Get($"https://{Server}/api/sessions/" + Room);
        //    www.SetRequestHeader("Authorization", "Basic " + EncodedSecret);
        //    yield return www.SendWebRequest();
        //    bool sessionOk = false;
        //    if (www.isNetworkError)
        //    {
        //        Debug.Log("Error While Sending: " + www.error);
        //    }
        //    else
        //    {
        //        Debug.Log($"Received{www.responseCode}: {www.downloadHandler.text}");
        //        Debug.Log("JSON string: " + www.downloadHandler.text);
        //        //session = JsonConvert.DeserializeObject<OpenViduSessionInfo>(www.downloadHandler.text);

        //        sessionOk = true;
        //    }


        //    if (www.responseCode == 404)
        //    {
        //        Debug.Log("Creating Session");

        //        www = new UnityWebRequest($"https://{Server}/api/sessions", "POST");
        //        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes("{\"customSessionId\": \"" + Room + "\"}");
        //        www.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);

        //        www.SetRequestHeader("Authorization", "Basic " + EncodedSecret);
        //        www.SetRequestHeader("Content-Type", "application/json");
        //        www.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        //        yield return www.SendWebRequest();

        //        if (www.isNetworkError)
        //        {
        //            Debug.Log("Error While Sending: " + www.error);
        //        }
        //        else
        //        {
        //            Debug.Log($"Received{www.responseCode}: {www.downloadHandler.text}");
        //            sessionOk = true;
        //        }
        //    }

        //    if (sessionOk)
        //    {
        //        Debug.Log("Asking for a token");
        //        www = new UnityWebRequest($"https://{Server}/api/tokens", "POST");
        //        byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes("{\"session\": \"" + Room + "\"}");// default to publisher
        //                                                                                                       //byte[] jsonToSend = new System.Text.UTF8Encoding().GetBytes("{\"session\": \"Aresibo\", \"role\": \"SUBSCRIBER\"}");

        //        www.uploadHandler = (UploadHandler)new UploadHandlerRaw(jsonToSend);
        //    www.SetRequestHeader("Authorization", "Basic " + EncodedSecret);
        //    www.SetRequestHeader("Content-Type", "application/json");
        //    www.downloadHandler = (DownloadHandler)new DownloadHandlerBuffer();
        //    yield return www.SendWebRequest();

        //    if (www.isNetworkError)
        //    {
        //        Debug.Log("Error While Sending: " + www.error);
        //    }
        //    else
        //    {
        //        Debug.Log($"Received{www.responseCode}: {www.downloadHandler.text}");
        //        var t = JsonConvert.DeserializeObject<OpenViduToken>(www.downloadHandler.text);
        //        token = t.token;
        //        Debug.Log($"Token :{token}");
        //    }
        //}

        //connect Websocket
        webSocket = gameObject.GetComponent<UnityWebSocket>();

            webSocket.Connect();
            //wait for the socket to be ready
            yield return new WaitForSeconds(1f);
            long i = idMessage++;
            webSocket.SendText("{\"jsonrpc\": \"2.0\"," +
             "\"method\": \"joinRoom\"," +
             "\"params\": {" +
             "\"token\": \"" + token + "\"," +
             "\"session\": \"" + Room + "\"," +
             "\"platform\": \"Chrome 76.0.3809.132 on Linux 64-bit\"," +
             //"\"platform\": \"Unity\"," +
             "\"metadata\": \"{clientData: TestClient}\"," +
            "\"secret\": \"" + Secret + "\", " +
            "\"recorder\": false  }," +
            "\"id\": " + i + " }");

            Debug.Log("---> added join room message");
            messages.Add(i, OpenViduType.JoinRoom);
        }



        /// <summary>
        /// Internal coroutine helper for receiving HTTP data from the DSS server using GET
        /// and processing it as needed
        /// </summary>
        /// <returns>the message</returns>
        private void Ping()
        {


            if (webSocket != null)
            {
                webSocket.SendText("{\"jsonrpc\": \"2.0\"," +
                  "\"method\": \"ping\"," +
                  "\"params\": {" +
                  "\"interval\": 5000" +
                  "}, " +
                  "\"id\": " +
                idMessage++ + " }");

            }

            lastGetComplete = true;
        }

        /// <inheritdoc/>
        protected override void Update()
        {
            // Do not forget to call the base class Update(), which processes events from background
            // threads to fire the callbacks implemented in this class.
            base.Update();

            if (startConnection)
            {
                PeerConnection.StartConnection();
                _nativePeer.RenegotiationNeeded += Connection_RenegotiationNeeded;
                _nativePeer.IceGatheringStateChanged += Connection_IceGatheringStateChanged;
                _nativePeer.IceStateChanged += Connection_IceStateChanged;

                startConnection = false;
            }


            //if there's a pending sdpanswer, then connect and consume it
            if (sdpAnswer != null)
            {
                PeerConnection.HandleConnectionMessageAsync(sdpAnswer); // If i call this I publish my video but I'm not able to subscribe

                long i = idMessage++;

                // follow with a ReceiveVideoFrom on RPC
                RemotePeerId = joinRoomAnswer.result.value[0].id;
                string message = "{\"jsonrpc\": \"2.0\"," +
                 "\"method\": \"receiveVideoFrom\"," +
                 "\"params\": { \"sender\": \"" + joinRoomAnswer.result.value[0].streams[0].Id + "\"" +
                 ",\"sdpOffer\": \"" + lastOffer.Content + "\"" +
                 "},\"id\": " + i + " }";

                Debug.Log("ReceiveVideoFrom : " + message);

                webSocket.SendText(message);
                Debug.Log("---> added recieve video form message");
                messages.Add(i, OpenViduType.ReceiveVideoFrom);

                sdpAnswer = null;
            }




            // If we have not reached our PollTimeMs value...
            if (timeSincePollMs <= PollTimeMs)
            {
                // ...then we keep incrementing our local counter until we do.
                timeSincePollMs += Time.deltaTime * 1000.0f;
                return;
            }

            // If we have a pending request still going, don't queue another yet.
            if (!lastGetComplete)
            {
                return;
            }

            // When we have reached our PollTimeMs value...
            timeSincePollMs = 0f;

            // ...begin the poll and process.
            lastGetComplete = false;
            Ping();
        }

        private IEnumerator SdpAnswer()
        {
            yield return new WaitForSeconds(1f);
            _mainThreadWorkQueue.Enqueue(() =>
            {

                PeerConnection.HandleConnectionMessageAsync(sdpAnswerReceiveVideo).ContinueWith(_ =>
                {
                    _nativePeer.CreateAnswer();
                    sdpAnswerReceiveVideo = null;

                }, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.RunContinuationsAsynchronously);
            });

        }

        private void DebugLogLong(string str)
        {
#if UNITY_ANDROID
            // On Android, logcat truncates to ~1000 characters, so split manually instead.
            const int maxLineSize = 1000;
            int totalLength = str.Length;
            int numLines = (totalLength + maxLineSize - 1) / maxLineSize;
            for (int i = 0; i < numLines; ++i)
            {
                int start = i * maxLineSize;
                int length = Math.Min(start + maxLineSize, totalLength) - start;
                Debug.Log(str.Substring(start, length));
            }
#else
            //Debug.Log(str);
#endif
        }

    }
}
