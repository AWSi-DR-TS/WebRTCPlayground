// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using System.Collections;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using Unity3dAzure.WebSockets;
using System.Collections.Specialized;
using OpenVidu;
using Newtonsoft.Json;
using Retrofit;
using Demo.Scripts;
using UniRx;
using System.Threading;
using System.Runtime.InteropServices;
using AOT;
using NatML.Devices;
using NatML.Devices.Outputs;
using Unity.Collections;

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

        #region WebRTC Camera to unity


        [Header(@"UI")]
        public RenderTexture renderTexture;

        #endregion



        /// <summary>
        /// Unity Engine Start() hook
        /// </summary>
        /// <remarks>
        /// https://docs.unity3d.com/ScriptReference/MonoBehaviour.Start.html
        /// </remarks>
        private void Start()
        {
         //   if (string.IsNullOrEmpty(Secret))
         //   {
         //       throw new ArgumentNullException("Secret");
         //   }

         //   byte[] bytesToEncode = Encoding.UTF8.GetBytes("OPENVIDUAPP:" + Secret);
         //   EncodedSecret = Convert.ToBase64String(bytesToEncode);


         //   if (string.IsNullOrEmpty(Server))
         //   {
         //       throw new ArgumentNullException("ServerAddress");
         //   }


         //   // If not explicitly set, default local ID to some unique ID generated by Unity
         //   if (string.IsNullOrEmpty(LocalPeerId))
         //   {
         //       LocalPeerId = SystemInfo.deviceName;
         //   }

         //   messages = new OrderedDictionary();

         //   // Retrofit implementation
         //   RetrofitAdapter adapter = new RetrofitAdapter.Builder()
         //.SetEndpoint("https://zweitblick.awsi.cloud:7443")
         //.Build();
         //   httpService = adapter.Create<IZweitblickInterface>();

         //   var ob = httpService.GetKeycloakAccessToken("zweitblick_client", "password", "openid", "glass_user", "4MHIyOZq$NU=TchA<PGZ");
         //   ob.SubscribeOn(Scheduler.ThreadPool)//send request in sub thread
         //        .ObserveOn(Scheduler.MainThread)//receive response in main thread
         //        .Subscribe(data =>
         //        {
         //            // onSuccess
         //            Debug.Log("Fetched keycloak token. Now fetching open vidu token...");

         //            RetrofitAdapter openViduAdapter = new RetrofitAdapter.Builder()
         //.SetEndpoint("https://zweitblick.awsi.cloud:9001")
         //.Build();
         //            IZweitblickInterface openViduService = openViduAdapter.Create<IZweitblickInterface>();

         //            var openViduRequest = openViduService.GetOpenViduToken("Bearer" + data.accessToken, new OpenViduTokenBody(Room));

         //            openViduRequest.SubscribeOn(Scheduler.ThreadPool)
         //            .ObserveOn(Scheduler.MainThread)
         //            .Subscribe(openViduResponse =>
         //            {
         //                string token = openViduResponse.response.token;
         //                Debug.Log("Open vidu response status: " + openViduResponse.status);
         //                Debug.Log("Open vidu token: " + token);

         //                StartCoroutine(Connect(token));

         //            }, error =>
         //            {
         //                Debug.Log("Open vidu token error: " + error);
         //            }
         //            );
         //        },
         //            error =>
         //            {
         //                Debug.Log("Retrofit Error:" + error);
         //            }
         //            )
         //        ;

            StartCamera();

        }

        async void StartCamera()
        {
            // Check camera permissions
            PermissionStatus status = await MediaDeviceQuery.RequestPermissions<CameraDevice>();
            // Log
            Debug.Log($"Camera permission status is {status}");

            // Check if granted
            if (status == PermissionStatus.Authorized)
            {
                Debug.Log("------------> Will start preview now");
                // Create a device query for the front camera 
                var filter = MediaDeviceCriteria.RearCamera;
                var query = new MediaDeviceQuery(filter);
                // Get the camera device
                var device = query.current as CameraDevice;
                // Start the camera preview
                var textureOutput = new RenderTextureOutput(); // stick around for an explainer
                device.StartRunning(textureOutput);
                // Display the preview in our UI
                renderTexture = await textureOutput.NextFrame();

                //rawImage.texture = previewTexture;
                //aspectFitter.aspectRatio = (float)previewTexture.width / previewTexture.height;

            }            
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
                //PeerConnection.HandleConnectionMessageAsync(sdpAnswer); // If i call this I publish my video but I'm not able to subscribe

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

        void ConvertYUVToRGBAndFillTexture(byte[] yuvData, Texture2D texture)
        {
            int width = texture.width;
            int height = texture.height;
            Color[] rgbData = new Color[width * height];

            int frameSize = width * height;
            int yIndex = 0;
            int uvIndex = frameSize;

            for (int j = 0, yp = 0; j < height; j++)
            {
                int uvp = uvIndex, u = 0, v = 0;
                for (int i = 0; i < width; i++, yIndex++, yp++)
                {
                    int y = (0xff & ((int)yuvData[yIndex])) - 16;
                    if (y < 0) y = 0;
                    if ((i & 1) == 0)
                    {
                        v = (0xff & yuvData[uvp++]) - 128;
                        u = (0xff & yuvData[uvp++]) - 128;
                    }

                    int y1192 = 1192 * y;
                    int r = (y1192 + 1634 * v);
                    int g = (y1192 - 833 * v - 400 * u);
                    int b = (y1192 + 2066 * u);

                    if (r < 0) r = 0; else if (r > 262143) r = 262143;
                    if (g < 0) g = 0; else if (g > 262143) g = 262143;
                    if (b < 0) b = 0; else if (b > 262143) b = 262143;

                    rgbData[yp] = new Color((r >> 10) & 0xff, (g >> 10) & 0xff, (b >> 10) & 0xff);
                }
                if ((j & 1) == 0 && j < height - 1)
                {
                    uvIndex += width;
                }
            }

            texture.SetPixels(rgbData);
            texture.Apply();
        }

        byte[] ConvertTextureToByteArray(Texture2D texture)
        {
            // Get the raw texture data
            Color32[] pixelData = texture.GetPixels32();

            // Create a byte array to hold the data
            byte[] byteArray = new byte[pixelData.Length * 4];

            // Populate the byte array
            for (int i = 0; i < pixelData.Length; ++i)
            {
                byteArray[i * 4] = pixelData[i].r;
                byteArray[i * 4 + 1] = pixelData[i].g;
                byteArray[i * 4 + 2] = pixelData[i].b;
                byteArray[i * 4 + 3] = pixelData[i].a;
            }

            return byteArray;
        }
    }
}
