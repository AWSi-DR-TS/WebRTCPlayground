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

public class OpenViduHandler : MonoBehaviour
{
    RTCPeerConnection localConnection;
    RTCPeerConnection remoteConnection;

    RTCDataChannel sendChannel;
    RTCDataChannel receiveChannel;

    List<RTCRtpSender> localSenders = new List<RTCRtpSender>();

    MediaStream videoStream, receiveStream;

    HttpClient httpClient = new();

    Texture2D previewTexture;

    public RenderTexture renderTexture;

    private long idMessage = 0;
    private bool videoUpdateStarted = false;

    // Start is called before the first frame update
    async void Start()
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

        var config = GetSelectedSdpSemantics();

        receiveStream = new MediaStream();

        // Create local peer
        localConnection = new(ref config);
        localConnection.OnIceCandidate = candidate => { OnIceCandidate(localConnection, candidate); };
        localConnection.OnIceConnectionChange = state => { OnIceConnectionChange(localConnection, state); };
        localConnection.OnTrack = e =>
        {
            receiveStream.AddTrack(e.Track);
        };

        // Create remote peer
        remoteConnection = new(ref config);
        remoteConnection.OnIceCandidate = candidate => { OnIceCandidate(remoteConnection, candidate); };
        remoteConnection.OnIceConnectionChange = state => { OnIceConnectionChange(remoteConnection, state); };
        remoteConnection.OnTrack = e =>
        {
            receiveStream.AddTrack(e.Track);
        };

        StartCoroutine(Connect(ovResponseObj.response.token));

        await StartCamera();
    }

    // Update is called once per frame
    void Update()
    {
        Graphics.Blit(previewTexture, renderTexture);
    }

    /*void OnDestroy()
    {
        sendChannel.Close();
        receiveChannel.Close();

        localConnection.Close();
        remoteConnection.Close();
    }*/

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
            var device = query.current as CameraDevice;
            // Start the camera preview
            var textureOutput = new TextureOutput();
            device.StartRunning(textureOutput);
            // Display the preview in our UI
            previewTexture = await textureOutput.NextFrame();

            // Get a valid RendertextureFormat
            //var gfxType = SystemInfo.graphicsDeviceType;
            //var format = WebRTC.GetSupportedRenderTextureFormat(gfxType);

            // Create a track from the RenderTexture
            //renderTexture = new RenderTexture(1280, 720, 0, format);
            var track = new VideoStreamTrack(renderTexture);

            videoStream = new MediaStream();
            videoStream.AddTrack(track);

            AddTracks();
        }
    }

    private void AddTracks()
    {
        foreach (var track in videoStream.GetTracks())
        {
            localSenders.Add(localConnection.AddTrack(track, videoStream));
        }

        Debug.Log(localConnection.GetTransceivers().Count());

        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
        }

        RTCOfferAnswerOptions options = default;
        var op = localConnection.CreateAnswer(ref options);
        Debug.Log(op);
    }

    private IEnumerator Connect(string token)
    {
        //connect Websocket
        var webSocket = gameObject.GetComponent<WebSocketBridge>();

        //wait for the socket to be ready
        yield return new WaitForSeconds(1f);
        long i = idMessage++;
        _ = webSocket.Send("{\"jsonrpc\": \"2.0\"," +
         "\"method\": \"joinRoom\"," +
         "\"params\": {" +
         "\"token\": \"" + token + "\"," +
         "\"session\": AWSiTest," +
         "\"platform\": \"Chrome 76.0.3809.132 on Linux 64-bit\"," +
         //"\"platform\": \"Unity\"," +
         "\"metadata\": \"{clientData: TestClient}\"," +
        "\"secret\": e29z34-djjh3Wjxz-5zzu5, " +
        "\"recorder\": false  }," +
        "\"id\": " + i + " }");

        Debug.Log("---> added join room message");
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
        var op = pc.CreateOffer();
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

        var otherPc = GetOtherPc(pc);
        Debug.Log($"setRemoteDescription start");
        var op2 = otherPc.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(otherPc);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }

        Debug.Log($"createAnswer start");
        // Since the 'remote' side has no media stream we need
        // to pass in the right constraints in order for it to
        // accept the incoming offer of audio and video.

        var op3 = otherPc.CreateAnswer();
        yield return op3;
        if (!op3.IsError)
        {
            yield return OnCreateAnswerSuccess(otherPc, op3.Desc);
        }
        else
        {
            OnCreateSessionDescriptionError(op3.Error);
        }
    }

    private static RTCConfiguration GetSelectedSdpSemantics()
    {
        RTCConfiguration config = default;
        config.iceServers = new[] { new RTCIceServer { urls = new[] { "stun:stun.l.google.com:19302" } } };

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

    private void OnIceCandidate(RTCPeerConnection pc, RTCIceCandidate candidate)
    {
        GetOtherPc(pc).AddIceCandidate(candidate);
        Debug.Log($"ICE candidate:\n {candidate.Candidate}");
    }

    private RTCPeerConnection GetOtherPc(RTCPeerConnection pc)
    {
        return (pc == localConnection) ? remoteConnection : localConnection;
    }

    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"SetLocalDescription complete");
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

        var otherPc = GetOtherPc(pc);
        Debug.Log($"setRemoteDescription start");

        var op2 = otherPc.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(otherPc);
        }
        else
        {
            var error = op2.Error;
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