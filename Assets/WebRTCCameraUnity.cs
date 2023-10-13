using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.UI;
using AOT;
using System;

public class WebRTCCameraUnity : MonoBehaviour
{
    public Text text;

    public Text debugText;

    // Import methods from native plugin
    [DllImport("CameraCapture")]
    private static extern void initializeCamera();

    private void Start()
    {
        Debug.Log("--------------- Start WebRTCCamerUnity ----------------");
        Application.logMessageReceived += HandleLog;

        AndroidJavaClass unityPlayerClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer");

        // Get the Context from the current Android activity
        AndroidJavaObject activityContext = unityPlayerClass.GetStatic<AndroidJavaObject>("currentActivity");

        // Create an instance of your custom AndroidJavaObject
        AndroidJavaObject myAndroidInstance = new AndroidJavaObject("com.awsi.camera2tounity");

        // Call the non-static Android method with the context
        myAndroidInstance.Call("test", activityContext);
        Debug.Log("--------------- Start WebRTCCamerUnity 22222222 ----------------");

    }

    // Callback for receiving frames from native plugin
    public void OnReceiveFrame(string base64FrameData)
    {
        Debug.Log("recieved frame .... " + base64FrameData);
        text.text = "Frame data: " + base64FrameData;
        //byte[] frameData = Convert.FromBase64String(base64FrameData);
        // Process frameData and send it through WebRTC
    }

    [MonoPInvokeCallback(typeof(Action<IntPtr>))]
    public static void ReceiveDataFromAndroid(IntPtr dataPtr, int length)
    {
        byte[] data = new byte[length];
        Marshal.Copy(dataPtr, data, 0, length);

        Debug.Log("------------> Got data... Length: " + data.Length);
        //Logger.Log("Got data from android... Length: " + data.Length);
        // Now you can use this data array within Unity
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        //debugText.text = "Log: " + stackTrace;
    }
}
