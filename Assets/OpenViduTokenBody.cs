using Newtonsoft.Json;

public class OpenViduTokenBody
{
    [JsonProperty("sessionId")]
    public string sessionId;

    public OpenViduTokenBody(string sessionId)
    {
        this.sessionId = sessionId;
    }
}
