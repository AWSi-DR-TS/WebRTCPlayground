using NatML.Devices;
using NatML.Devices.Outputs;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Unity.WebRTC;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.UI;

public class OpenViduHandler : MonoBehaviour
{
    RTCPeerConnection localConnection;
    RTCPeerConnection remoteConnection;

    List<RTCRtpSender> localSenders = new();

    HttpClient httpClient = new();

    Texture2D previewTexture;

    RenderTexture renderTexture;

    public AudioSource audioSource;
    public RawImage rawImage;
    public string userName = "TestUser";
    public string sessionName = "AWSiTest";

    // private WebCamTexture webCamTexture;
    private CameraDevice cameraDevice;
    private long idMessage = 0;
    private long joinId = -1;
    private long publishVideoId = -1;
    private long prepareReceiveVideoId = -1;
    private long receiveVideoId = -1;
    private long participantJoinedId = -1;
    private bool videoUpdateStarted = false;
    private bool webSocketConnected = false;
    private bool videoPublishSend = false;
    private bool descAdded = false;
    private bool joined = false;
    private bool published = false;
    private bool videoReceiveSend = false;
    private bool videoReceivingPrepared = false;
    private bool videoReceived = false;
    private bool isTorchOn = false;
    private bool cameraInitialized = false;
    private bool zoomSet = false;
    private bool remoteLocalDescriptionSend = false;
    private bool answerCreated = false;
    private bool remoteOfferInit = false;

    private WebSocketUser[] webSocketUsers;
    public AudioSource micAudioSource;

    private float zoomRatio;

    private string videoSenderId;
    private string remoteVideoSenderId = null;
    private string localEndpointName;
    private string remoteEndpointName;
    private RTCSessionDescription remoteOffer = new();
    private RTCSessionDescriptionAsyncOperation operation;
    private RTCSessionDescriptionAsyncOperation operationRemote;
    private RTCSetSessionDescriptionAsyncOperation operationRemoteAnswer;
    private RTCSetSessionDescriptionAsyncOperation test;
    private WebSocketBridge webSocket;

    AudioClip mic;
    int lastPos, pos;

    // Start is called before the first frame update
    async void Start()
    {
        webSocket = gameObject.GetComponent<WebSocketBridge>();
        webSocket.OnReceived += e =>
        {
            HandleWebSocketAnswer(e);
        };

        renderTexture = new RenderTexture(256, 256, 0, GraphicsFormat.B8G8R8A8_UNorm);
        //rawImage.texture = renderTexture;

        KeycloakResponse kcResponseObj = await GetKeyCloakToken();
        OpenViduResponse ovResponseObj = await GetOpenViduToken(kcResponseObj);

        StartMicrophone();
        await StartCamera();
    }

    // Update is called once per frame
    void Update()
    {
        Graphics.Blit(previewTexture, renderTexture);

        if ((pos = Microphone.GetPosition(null)) > 0)
        {
            if (lastPos > pos) lastPos = 0;

            if (pos - lastPos > 0)
            {
                // Allocate the space for the sample.
                float[] sample = new float[(pos - lastPos) * mic.channels];

                // Get the data from microphone.
                mic.GetData(sample, lastPos);

                // Put the data in the audio source.
                micAudioSource.clip.SetData(sample, lastPos);

                if (!micAudioSource.isPlaying) micAudioSource.Play();

                lastPos = pos;
            }
        }

        if (cameraInitialized && !zoomSet)
        {
            Debug.Log("Focusmode: " + cameraDevice.focusMode);
            zoomRatio = cameraDevice.zoomRatio;

            Debug.Log("Zoom Range from " + cameraDevice.zoomRange.min + " to " + cameraDevice.zoomRange.max);
            Debug.Log("Zoom Ratio now " + zoomRatio);

            zoomSet = true;
        }
        else if (published && !remoteOfferInit) // && prepareReceiveVideoId == -1)
        {
            //operationRemote = remoteConnection.CreateOffer();
            remoteOfferInit = true;
            // PrepareReceiveVideo();
        }
        else if (!videoPublishSend && webSocketConnected && descAdded)
        {
            PublishVideo();
        }
        else if (operationRemote != null && operationRemote.IsDone && !answerCreated)
        {
            var desc = operationRemote.Desc;
            Debug.Log("remote connection local description: " + desc.sdp);
            operationRemoteAnswer = remoteConnection.SetLocalDescription(ref desc);
            answerCreated = true;
        }
        else if (operationRemoteAnswer != null && operationRemoteAnswer.IsDone && receiveVideoId == -1 && remoteVideoSenderId != null)
        {
            Debug.Log("about to recieve remote video...");
            ReceiveVideo();
        }
        else if (videoReceivingPrepared && published && !videoReceiveSend && operation.IsDone && !remoteLocalDescriptionSend)
        {
            remoteLocalDescriptionSend = true;
        }
        else if (operationRemoteAnswer != null && operationRemoteAnswer.IsDone && videoReceived)
        {
            Debug.Log("video is recieved...");
            operationRemoteAnswer = null;
        }
    }

    /*void OnDestroy()
    {
        sendChannel.Close();
        receiveChannel.Close();

        localConnection.Close();
        remoteConnection.Close();
    }*/

    public void SetTorch()
    {
        isTorchOn = !isTorchOn;
        Debug.Log("Setting torch to " + isTorchOn);

        if (isTorchOn)
            cameraDevice.torchMode = CameraDevice.TorchMode.Maximum;
        else
            cameraDevice.torchMode = CameraDevice.TorchMode.Off;
    }

    public void SetZoom(int value)
    {
        zoomRatio += value;

        if (zoomRatio > cameraDevice.zoomRange.max)
            zoomRatio = cameraDevice.zoomRange.max;
        else if (zoomRatio < cameraDevice.zoomRange.min)
            zoomRatio = cameraDevice.zoomRange.min;

        Debug.Log("Setting zoom to " + zoomRatio);

        cameraDevice.zoomRatio = zoomRatio;
    }

    async Task StartCamera()
    {
        // Get VideoStream with Unity WebCamTexture
        /*webCamTexture = new WebCamTexture();

        webCamTexture.requestedWidth = 1280;
        webCamTexture.requestedHeight = 720;

        rawImage.texture = webCamTexture;
        webCamTexture.Play();*/
        
        // Check camera permissions
        PermissionStatus status = await MediaDeviceQuery.RequestPermissions<CameraDevice>();
        // Log
        Debug.Log($"Camera permission status is {status}");

        // Check if granted
        if (status == PermissionStatus.Authorized)
        {
            Debug.Log("------------> Will start preview now");
            // Create a device query for the rear camera 
            var filter = MediaDeviceCriteria.RearCamera;
            var query = new MediaDeviceQuery(filter);
            // Get the camera device
            cameraDevice = query.current as CameraDevice;
            // Start the camera preview
            var textureOutput = new TextureOutput();
            cameraDevice.StartRunning(textureOutput);
            // Display the preview in our UI
            previewTexture = await textureOutput.NextFrame();

            cameraInitialized = true;
        }
    }

    private void StartMicrophone()
    {
        mic = Microphone.Start(null, true, 10, 44100);

        micAudioSource.clip = AudioClip.Create("mic", 10 * 44100, mic.channels, 44100, false);
        micAudioSource.loop = false;
    }

    private void AddTracks()
    {
        // Create a track from the RenderTexture
        var streamTrack = new VideoStreamTrack(renderTexture);
        var audioTrack = new AudioStreamTrack(micAudioSource);

        localSenders.Add(localConnection.AddTrack(streamTrack));
        localSenders.Add(localConnection.AddTrack(audioTrack));

        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
        }
    }

    private void PublishVideo()
    {
        long i = idMessage++;
        _ = webSocket.Send("{\"jsonrpc\": \"2.0\"," +
            "\"method\": \"publishVideo\"," +
            "\"params\": {" +
            "\"audioActive\": true," +
            "\"videoActive\": true," +
            "\"doLoopback\": false," +
            "\"frameRate\": 30," +
            "\"hasAudio\": true," +
            "\"hasVideo\": true," +
            "\"typeOfVideo\": \"CAMERA\"," +
            "\"videoDimensions\": \"{\\\"width\\\":1280, \\\"height\\\":720}\"," +
            "\"sdpOffer\": \"" + localConnection.LocalDescription.sdp + "\"  }," +
            "\"id\": " + i + "}");

        publishVideoId = i;

        Debug.Log("---> added publish video message with id: " + i);
        videoPublishSend = true;
        descAdded = false;
    }

    private void PrepareReceiveVideo()
    {
        OnSetRemoteSuccess(localConnection);

        foreach (WebSocketUser user in webSocketUsers)
        {
            if (user.streams != null)
            {
                // Prepare receiving stream
                long i = idMessage++;
                _ = webSocket.Send("{\"jsonrpc\": \"2.0\"," +
                    "\"method\": \"prepareReceiveVideoFrom\"," +
                    "\"params\": {" +
                    "\"reconnect\": false," +
                    "\"sender\": \"" + videoSenderId + "\" }," +
                    "\"id\": " + i + "}");

                prepareReceiveVideoId = i;

                break;
            }
        }
    }

    private void ReceiveVideo()
    {
        // Receive stream
        long i = idMessage++;
        _ = webSocket.Send("{\"jsonrpc\": \"2.0\"," +
            "\"method\": \"receiveVideoFrom\"," +
            "\"params\": {" +
            "\"sdpOffer\": \"" + remoteConnection.LocalDescription.sdp + "\"," +
            "\"sender\": \"" + remoteVideoSenderId + "\" }," +
            "\"id\": " + i + " }");

        Debug.Log("---> added receive message with id: " + i);

        receiveVideoId = i;
        operationRemote = null;
        test = null;
    }

    private async Task<KeycloakResponse> GetKeyCloakToken()
    {
        // Get KeyCloakToken
        var kcContent = new FormUrlEncodedContent(
            new Dictionary<string, string>{
                { "client_id", "zweitblick_client" },
                { "grant_type", "password" },
                { "scope", "openid" },
                { "username", "glass_user" },
                { "password", "4MHIyOZq$NU=TchA<PGZ" },
            }
        );
        var kcResponse = await httpClient.PostAsync(
            "https://zweitblick.awsi.cloud:7443/auth/realms/zweitblick/protocol/openid-connect/token",
            kcContent
        );
        string kcResponseString = await kcResponse.Content.ReadAsStringAsync();
        return JsonConvert.DeserializeObject<KeycloakResponse>(kcResponseString);
    }

    private async Task<OpenViduResponse> GetOpenViduToken(KeycloakResponse kcResponseObj)
    {
        // Get Open Vidu Token
        var ovContent = new StringContent(
            JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                { "sessionId", sessionName }
            })
        );
        ovContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", kcResponseObj.accessToken);
        var ovResponse = await httpClient.PostAsync(
            "https://zweitblick.awsi.cloud:9001/api/openvidu/token",
            ovContent
        );
        var ovResponseString = await ovResponse.Content.ReadAsStringAsync();
        OpenViduResponse ovResponseObj = JsonConvert.DeserializeObject<OpenViduResponse>(ovResponseString);

        StartCoroutine(Connect(ovResponseObj.response.token));

        return ovResponseObj;
    }

    private void HandleWebSocketAnswer(string e)
    {
        WebSocketAnswer webSocketAnswer = JsonConvert.DeserializeObject<WebSocketAnswer>(e);

        // Response to Join Message
        if (webSocketAnswer.id == joinId && !joined)
        {
            Debug.Log("user joined with message: " + e);

            joined = true;

            localEndpointName = webSocketAnswer.result.id;
            remoteEndpointName = webSocketAnswer.result.value[0].id;

            var config = GetSelectedSdpSemantics();

            // Create local peer
            localConnection = new(ref config);
            localConnection.OnIceCandidate = candidate => { OnIceCandidate(localEndpointName, candidate); };
            localConnection.OnIceConnectionChange = state => { OnIceConnectionChange(localConnection, state); };
            localConnection.OnNegotiationNeeded = () => { StartCoroutine(PeerNegotiationNeeded(localConnection)); };

            webSocketUsers = webSocketAnswer.result.value;
            foreach (WebSocketUser user in webSocketUsers)
            {
                if (user.streams != null)
                {
                    Debug.Log("User has joined with connection ID: " + user.id);
                    remoteVideoSenderId = user.streams[0].id;
                    break;
                }
            }

            // remoteVideoSenderId = webSocketAnswer.result.value[0]?.streams[0]?.id;

            if (!string.IsNullOrEmpty(remoteVideoSenderId))
            {
                // Create remote peer
                remoteConnection = new(ref config);
                remoteConnection.OnIceCandidate = candidate => { OnIceCandidate(remoteEndpointName, candidate); };
                remoteConnection.OnIceConnectionChange = state => { OnIceConnectionChange(remoteConnection, state); };
                remoteConnection.OnTrack = (RTCTrackEvent e) =>
                {
                    if (e.Track is VideoStreamTrack video)
                    {
                        video.OnVideoReceived += tex =>
                        {
                            rawImage.texture = tex;
                        };
                    }
                    if (e.Track is AudioStreamTrack t)
                    {
                        audioSource.SetTrack(t);
                        audioSource.loop = true;
                        audioSource.Play();
                    }
                };

                var transceiverDirection = new RTCRtpTransceiverInit();
                transceiverDirection.direction = RTCRtpTransceiverDirection.RecvOnly;

                remoteConnection.AddTransceiver(TrackKind.Video, transceiverDirection);
                remoteConnection.AddTransceiver(TrackKind.Audio, transceiverDirection);

                operationRemote = remoteConnection.CreateOffer();
            }

            AddTracks();
        }
        // Handle ICECandidate Message
        else if (webSocketAnswer.method == "iceCandidate")
        {
            RTCIceCandidateInit rTCIceCandidateInit = new RTCIceCandidateInit();
            rTCIceCandidateInit.candidate = webSocketAnswer.@params.candidate;
            rTCIceCandidateInit.sdpMid = webSocketAnswer.@params.sdpMid;
            rTCIceCandidateInit.sdpMLineIndex = webSocketAnswer.@params.sdpMLineIndex;

            if (webSocketAnswer.@params.endpointName.Contains(remoteEndpointName))
            {
                remoteConnection.AddIceCandidate(new RTCIceCandidate(rTCIceCandidateInit));
            }
            else if (webSocketAnswer.@params.endpointName.Contains(localEndpointName))
            {
                localConnection.AddIceCandidate(new RTCIceCandidate(rTCIceCandidateInit));
            }
        }
        // Response to Publish Message
        else if (webSocketAnswer.id == publishVideoId && !published)
        {
            RTCSessionDescription remoteDescription = new();
            remoteDescription.type = RTCSdpType.Answer;
            remoteDescription.sdp = webSocketAnswer.result.sdpAnswer;

            localConnection.SetRemoteDescription(ref remoteDescription);

            published = true;
        }
        // Response to Prepare Receive Message
        /*else if (webSocketAnswer.id == prepareReceiveVideoId && !videoReceivingPrepared)
        {
            remoteOffer.type = RTCSdpType.Offer;
            remoteOffer.sdp = webSocketAnswer.result.sdpOffer;

            test = remoteConnection.SetRemoteDescription(ref remoteOffer);

            videoReceivingPrepared = true;
        }*/
        // Response to Receive Message
        else if (webSocketAnswer.id == receiveVideoId && !videoReceived)
        {
            RTCSessionDescription remoteAnswer = new();
            remoteAnswer.type = RTCSdpType.Answer;
            remoteAnswer.sdp = webSocketAnswer.result.sdpAnswer;


            Debug.Log("recieved remote answer....: " + remoteAnswer.sdp);
            operationRemoteAnswer = remoteConnection.SetRemoteDescription(ref remoteAnswer);

            videoReceived = true;
        }
        else if (webSocketAnswer.method == "participantJoined")
        {
            //var remoteConfig = GetSelectedSdpSemantics();

            //// Create remote peer
            //remoteConnection = new(ref remoteConfig);
            //remoteConnection.OnIceCandidate = candidate => {
            //    Debug.Log("new ICE candidate remote peer connection: " + candidate.Candidate);
            //    OnIceCandidate(videoSenderId, candidate, true); };
            //remoteConnection.OnIceConnectionChange = state => {
            //    Debug.Log("ICE state change. new state: " + state);
            //    OnIceConnectionChange(remoteConnection, state); };
            //remoteConnection.OnTrack = (RTCTrackEvent e) =>
            //{
            //    Debug.Log("---->>> got track");
            //    if (e.Track is VideoStreamTrack video)
            //    {
            //        video.OnVideoReceived += tex =>
            //        {
            //            rawImage.texture = tex;
            //        };
            //    }
            //};

            //operationRemote = remoteConnection.CreateOffer();


            //foreach (WebSocketUser user in webSocketUsers)
            //{
            //    if (user.streams != null)
            //    {
            //        Debug.Log("Remote User has joined with connection ID: " + user.id);
            //        videoSenderId = user.streams[0].id;
            //        break;
            //    }
            //}

            //// Create a track from the RenderTexture
            //var streamTrack = new VideoStreamTrack(renderTexture);

            //localSenders.Add(remoteConnection.AddTrack(streamTrack));

            //if (!videoUpdateStarted)
            //{
            //    StartCoroutine(WebRTC.Update());
            //    videoUpdateStarted = true;
            //}
        }
        else if (webSocketAnswer.method == "participantPublished")
        {
            //WebSocketParticipantPublishAnswer publishAnswer = JsonConvert.DeserializeObject<WebSocketParticipantPublishAnswer>(e);

            //remoteVideoSenderId = publishAnswer.@params.streams[0].id;

        }
    }

    private IEnumerator Connect(string token)
    {
        //wait for the socket to be ready
        yield return new WaitForSeconds(1f);
        long i = idMessage++;

        _ = webSocket.Send("{\"jsonrpc\": \"2.0\"," +
         "\"method\": \"joinRoom\"," +
         "\"params\": {" +
         "\"token\": \"" + token + "\"," +
         "\"session\": \"" + sessionName + "\"," +
         "\"platform\": \"Android 31\"," +
         "\"metadata\": \"{\\\"clientData\\\": \\\"" + userName + "\\\"}\"," +
        "\"secret\": \"\" " +
        /*"\"recorder\": false*/" }," +
        "\"id\": " + i + " }");

        joinId = i;

        yield return new WaitForSeconds(1f);
        Debug.Log("---> added join room message");
        webSocketConnected = true;
    }

    // Display the video codec that is actually used.
    IEnumerator CheckStats(RTCPeerConnection pc)
    {
        yield return new WaitForSeconds(0.1f);
        if (pc == null)
            yield break;

        var op = pc.GetStats();
        yield return op;
        if (op.IsError)
        {
            Debug.LogErrorFormat("RTCPeerConnection.GetStats failed: {0}", op.Error);
            yield break;
        }

        RTCStatsReport report = op.Value;
        RTCIceCandidatePairStats activeCandidatePairStats = null;
        RTCIceCandidateStats remoteCandidateStats = null;

        foreach (var transportStatus in report.Stats.Values.OfType<RTCTransportStats>())
        {
            if (report.Stats.TryGetValue(transportStatus.selectedCandidatePairId, out var tmp))
            {
                activeCandidatePairStats = tmp as RTCIceCandidatePairStats;
            }
        }

        if (activeCandidatePairStats == null || string.IsNullOrEmpty(activeCandidatePairStats.remoteCandidateId))
        {
            yield break;
        }

        foreach (var iceCandidateStatus in report.Stats.Values.OfType<RTCIceCandidateStats>())
        {
            if (iceCandidateStatus.Id == activeCandidatePairStats.remoteCandidateId)
            {
                remoteCandidateStats = iceCandidateStatus;
            }
        }

        if (remoteCandidateStats == null || string.IsNullOrEmpty(remoteCandidateStats.Id))
        {
            yield break;
        }

        Debug.Log($"candidate stats Id:{remoteCandidateStats.Id}, Type:{remoteCandidateStats.candidateType}");
    }

    IEnumerator PeerNegotiationNeeded(RTCPeerConnection pc)
    {
        operation = pc.CreateOffer();
        yield return operation;

        if (!operation.IsError)
        {
            if (pc.SignalingState != RTCSignalingState.Stable)
            {
                Debug.LogError($"signaling state is not stable.");
                yield break;
            }

            yield return StartCoroutine(OnCreateOfferSuccess(pc, operation.Desc));
        }
        else
        {
            OnCreateSessionDescriptionError(operation.Error);
        }
    } 

    private IEnumerator OnCreateOfferSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        RTCSetSessionDescriptionAsyncOperation op;
        Debug.Log($"Offer from\n{desc.sdp}");
        Debug.Log($"setLocalDescription start");
        op = pc.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            OnSetLocalSuccess(pc);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }
    }

    private static RTCConfiguration GetSelectedSdpSemantics()
    {
        RTCConfiguration config = default;
        config.iceServers = new[] { 
            new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } }
        };

        return config;
    }

    private void OnIceConnectionChange(RTCPeerConnection pc, RTCIceConnectionState state)
    {
        Debug.Log($"IceConnectionState: {state}");

        if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
        {
            StartCoroutine(CheckStats(pc));
        }
    }

    private void OnIceCandidate(string endpointName, RTCIceCandidate candidate)
    {
        long i = idMessage++;
        _ = webSocket.Send("{\"jsonrpc\": \"2.0\"," +
                "\"method\": \"onIceCandidate\"," +
                "\"params\": {" +
                "\"candidate\": \"" + candidate.Candidate + "\"," +
                "\"endpointName\": \"" + endpointName + "\"," +
                "\"sdpMid\": \"" + candidate.SdpMid + "\"," +
                "\"sdpMLineIndex\": " + candidate.SdpMLineIndex + "}," +
                "\"id\": \"" + i + "\" }");
    }

    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetLocalDescription complete");
        descAdded = true;
    }

    void OnSetSessionDescriptionError(ref RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
        // HangUp();
    }

    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetRemoteDescription complete");
    }

    private static void OnCreateSessionDescriptionError(RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }
}

public class KeycloakResponse
{
    [JsonProperty("access_token")]
    public string accessToken;
    [JsonProperty("expires_in")]
    public int expiresIn;
    [JsonProperty("refresh_expires_in")]
    public int refreshExpiresIn;
    [JsonProperty("refresh_token")]
    public string refreshToken;
    [JsonProperty("token_type")]
    public string tokenType;
    [JsonProperty("id_token")]
    public string idToken;
    [JsonProperty("not-before-policy")]
    public int notBeforePolicy;
    [JsonProperty("session_state")]
    public string sessionState;
    [JsonProperty("scope")]
    public string scope;
}

public class OpenViduResponse
{
    [JsonProperty("status")]
    public int status;
    [JsonProperty("response")]
    public OpenViduTokenResponse response;
}

public class OpenViduTokenResponse
{
    [JsonProperty("token")]
    public string token;
}

public class WebSocketAnswer
{
    public long id;
    public string method;
    public WebSocketResult result;
    public WebSocketParams @params;
}

public class WebSocketParticipantPublishAnswer
{
    public long id;
    public string method;
    public WebSocketResult result;
    public WebSocketPublishParams @params;
}

public class WebSocketResult
{
    public string id;
    public string sdpOffer;
    public string sdpAnswer;
    public WebSocketUser[] value;
}

public class WebSocketParams
{
    public string senderConnectionId;
    public string endpointName;
    public string candidate;
    public string sdpMid;
    public int sdpMLineIndex;
}

public class WebSocketPublishParams
{
    public string id;
    public WebSocketStream[] streams;
}

public class WebSocketUser
{
    public string id;
    public WebSocketStream[] streams;
}

public class WebSocketStream
{
    public string id;
}