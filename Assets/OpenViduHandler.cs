using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.WebRTC;
using System.Net.Http;
using Newtonsoft.Json;
using System.Net.Http.Headers;
using NatML.Devices;
using NatML.Devices.Outputs;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UI;

public class OpenViduHandler : MonoBehaviour
{
    RTCPeerConnection localConnection;

    List<RTCRtpSender> localSenders = new List<RTCRtpSender>();

    HttpClient httpClient = new();

    Texture2D previewTexture;

    RenderTexture renderTexture;

    public RawImage rawImage;
    public string userName = "Test";

    private CameraDevice cameraDevice;
    private long idMessage = 0;
    private long joinId = -1;
    private long publishVideoId = -1;
    private bool videoUpdateStarted = false;
    private bool webSocketConnected = false;
    private bool videoPublishSend = false;
    private bool descAdded = false;
    private bool joined = false;
    private bool published = false;
    private bool isTorchOn = false;
    private bool cameraInitialized = false;
    private bool zoomSet = false;

    private float zoomRatio;

    private string endpointName;
    private RTCSessionDescriptionAsyncOperation op;
    private RTCSetSessionDescriptionAsyncOperation operationRemote;
    private WebSocketBridge webSocket;

    // Start is called before the first frame update
    async void Start()
    {
        webSocket = gameObject.GetComponent<WebSocketBridge>();
        webSocket.OnReceived += e =>
        {
            WebSocketAnswer test = JsonConvert.DeserializeObject<WebSocketAnswer>(e);
            if (test.id == joinId && !joined)
            {
                joined = true;

                endpointName = test.result.id;

                var config = GetSelectedSdpSemantics();

                // Create local peer
                localConnection = new(ref config);
                localConnection.OnIceCandidate = candidate => { OnIceCandidate(candidate); };
                localConnection.OnIceConnectionChange = state => { OnIceConnectionChange(localConnection, state); };
                localConnection.OnNegotiationNeeded = () => { StartCoroutine(PeerNegotiationNeeded(localConnection)); };

                AddTracks();
            }
            if (test.method == "iceCandidate")
            {
                RTCIceCandidateInit rTCIceCandidateInit = new RTCIceCandidateInit();
                rTCIceCandidateInit.candidate = test.@params.candidate;
                rTCIceCandidateInit.sdpMid = test.@params.sdpMid;
                rTCIceCandidateInit.sdpMLineIndex = test.@params.sdpMLineIndex;

                localConnection.AddIceCandidate(new RTCIceCandidate(rTCIceCandidateInit));
            }
            if (test.id == publishVideoId && !published)
            {
                published = true;

                RTCSessionDescription remoteDescription = new();
                remoteDescription.type = RTCSdpType.Answer;
                remoteDescription.sdp = test.result.sdpAnswer;

                operationRemote = localConnection.SetRemoteDescription(ref remoteDescription);
            }
        };

        renderTexture = new RenderTexture(256, 256, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.B8G8R8A8_UNorm);

        rawImage.texture = renderTexture;

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
        KeycloakResponse kcResponseObj = JsonConvert.DeserializeObject<KeycloakResponse>(kcResponseString);

        Debug.Log(kcResponseObj.accessToken);

        var ovContent = new StringContent(
            JsonConvert.SerializeObject(new Dictionary<string, string>
            {
                { "sessionId", "AWSiTest" }
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

        Debug.Log(ovResponseObj.response.token);

        StartCoroutine(Connect(ovResponseObj.response.token));

        await StartCamera();
    }

    // Update is called once per frame
    void Update()
    {
        Graphics.Blit(previewTexture, renderTexture);

        if (cameraInitialized && !zoomSet)
        {
            Debug.Log("Focusmode: " + cameraDevice.focusMode);
            zoomRatio = cameraDevice.zoomRatio;

            Debug.Log("Zoom Range from " + cameraDevice.zoomRange.min + " to " + cameraDevice.zoomRange.max);
            Debug.Log("Zoom Ratio now " + zoomRatio);

            Debug.Log(cameraDevice.previewResolution);

            cameraDevice.previewResolution = (1280, 720);

            Debug.Log(cameraDevice.previewResolution);

            zoomSet = true;
        }

        if (operationRemote != null && operationRemote.IsDone)
        {
            OnSetRemoteSuccess(localConnection);
            operationRemote = null;
        }

        if (!videoPublishSend && webSocketConnected && descAdded)
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

            Debug.Log("---> added publish video message");
            videoPublishSend = true;
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
            cameraDevice = query.current as CameraDevice;
            // Start the camera preview
            var textureOutput = new TextureOutput();
            cameraDevice.StartRunning(textureOutput);
            // Display the preview in our UI
            previewTexture = await textureOutput.NextFrame();

            cameraInitialized = true;
        }
    }

    private void AddTracks()
    {
        // Create a track from the RenderTexture
        var streamTrack = new VideoStreamTrack(renderTexture);

        localSenders.Add(localConnection.AddTrack(streamTrack));

        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
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
         "\"session\": \"AWSiTest\"," +
         "\"platform\": \"Android 31\"," +
         "\"metadata\": \"{\\\"clientData\\\": \\\"" + userName + "\\\"}\"," +
        "\"secret\": \"\", " +
        "\"recorder\": false }," +
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
        op = pc.CreateOffer();
        yield return op;

        if (!op.IsError)
        {
            if (pc.SignalingState != RTCSignalingState.Stable)
            {
                Debug.LogError($"signaling state is not stable.");
                yield break;
            }

            yield return StartCoroutine(OnCreateOfferSuccess(pc, op.Desc));
        }
        else
        {
            OnCreateSessionDescriptionError(op.Error);
        }
    }

    private IEnumerator OnCreateOfferSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        Debug.Log($"Offer from\n{desc.sdp}");
        Debug.Log($"setLocalDescription start");
        var op = pc.SetLocalDescription(ref desc);
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

    private void OnIceCandidate(RTCIceCandidate candidate)
    {
        //localConnection.AddIceCandidate(candidate);
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

    IEnumerator OnCreateAnswerSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        Debug.Log($"Answer:\n{desc.sdp}");
        Debug.Log($"setLocalDescription start");
        var op = pc.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            OnSetLocalSuccess(pc);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }
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

public class WebSocketResult
{
    public string id;
    public string sdpAnswer;
}

public class WebSocketParams
{
    public string candidate;
    public string sdpMid;
    public int sdpMLineIndex;
}