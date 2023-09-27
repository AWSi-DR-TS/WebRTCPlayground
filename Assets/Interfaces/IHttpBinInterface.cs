using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public interface IHttpBinInterface
{
    [Post("/auth/realms/zweitblick/protocol/openid-connect/token")]
    IObservable<HttpBinResponse> Post(
        [Query("client_id")] string client_id,
        [Query("grant_type")] string grant_type,
        [Query("scope")] string scope,
        [Query("username")] string username,
        [Query("password")] string password
        );
}
