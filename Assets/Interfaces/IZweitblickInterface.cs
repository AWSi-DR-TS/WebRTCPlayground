using Demo.Scripts;
using Retrofit;
using Retrofit.Methods;
using Retrofit.Parameters;
using UniRx;

public interface IZweitblickInterface
{
    [Post("/auth/realms/zweitblick/protocol/openid-connect/token")]
    IObservable<KeycloakResponse> GetKeycloakAccessToken(
        [Field("client_id")] string client_id,
        [Field("grant_type")] string grant_type,
        [Field("scope")] string scope,
        [Field("username")] string username,
        [Field("password")] string password
        );

    [Post("/api/openvidu/token")]
    IObservable<OpenViduResponse> GetOpenViduToken(
        [Header("Authorization")] string authHeader,
        [Body] OpenViduTokenBody sessionId
        );
}
