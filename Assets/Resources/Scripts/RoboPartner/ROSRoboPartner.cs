using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using MiniJSON;
using System.Linq;
using System.IO;
using SimpleJSON;
using UnityEngine;
using UnityEngine.Networking;

public class ROSRoboPartner : MonoBehaviour, IRoboPartner {

    private const string ROS_PORT = "9090";
    private const string JIBO_STATE_TOPIC = "/jibo_state";
    private const string JIBO_STATE_MESSAGE_TYPE = "/jibo_msgs/JiboState";
    private const string JIBO_ACTION_TOPIC = "/jibo";
    private const string JIBO_ACTION_MESSAGE_TYPE = "/jibo_msgs/JiboAction";

    private static Vector3 LOOKAT_TABLET = new Vector3(-15, 15, -1);
    private static Vector3 LOOKAT_USER = new Vector3(0, 30, 5);
    private Vector3 currentLookat = LOOKAT_USER;

    private Dictionary<string, float> LED_WHITE = new Dictionary<string, float>() { { "x", 1 }, { "y", 1 }, { "z", 1 } };
    private Dictionary<string, float> LED_GREEN = new Dictionary<string, float>() { { "x", 0 }, { "y", 1 }, { "z", 0 } };
    private Dictionary<string, float> LED_LIGHT_GREEN = new Dictionary<string, float>() { { "x", 0 }, { "y", 0.75f }, { "z", 0 } };
    private Dictionary<string, float> LED_BLUE = new Dictionary<string, float>() { { "x", 55f / 255 }, { "y", 198f / 255 }, { "z", 1 } };
    private Dictionary<string, float> LED_BLACK = new Dictionary<string, float>() { { "x", 0 }, { "y", 0 }, { "z", 0 } };

    private bool connected = false;
    private Queue<string> messageQueue = new Queue<string>();
    private bool stopping = false;
    private Thread publisherThread = null;

    private Dictionary<string, float> targetColor;

    private RosbridgeWebSocketClient rosClient = null;

    private double nextAutoMotionTime = -10000;

    private int ledState = 0;
    private double lastLEDBlinkTime = -10000;
    private double nextEyeBlinkTime = -10000;

    private static float BLINK_MEAN_PERIOD = 7f;
    private static float AUTO_MOTION_MEAN_PERIOD = 1.5f;

    private SynthesizerController synthesizer;
    private VoiceActivityDetector voiceActivityDetector;
    Dictionary<RoboExpression, string[]> expressionVocabulary;
    private Environment environment;

    void Start()
    {
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        synthesizer = stageObject.GetComponent<SynthesizerController>();
        voiceActivityDetector = stageObject.GetComponent<VoiceActivityDetector>();
        targetColor = LED_BLACK;
        expressionVocabulary = new Dictionary<RoboExpression, string[]> {   {RoboExpression.GREETING, new string[]{ "greeting.keys", "greeting0.keys", "Misc/greetings_09.keys" } },
                                                                            {RoboExpression.HAPPY, new string[]{ "Misc/Eye_to_Happy_01.keys", "Misc/Eye_to_Happy_02.keys" } },
                                                                            {RoboExpression.EMBARRASSED, new string[]{ "Misc/embarassed_01_02.keys" } },
                                                                            {RoboExpression.CURIOUS, new string[]{ "Misc/interested_01.keys", "silent/interested_01.keys" } },
                                                                            {RoboExpression.PUZZLED, new string[]{ "Misc/puzzled_01_02.keys", "Misc/puzzled_02_02.keys", "silent/confused_01.keys" } },
                                                                            {RoboExpression.EXCITED, new string[]{ "Misc/success_02.keys" } } };
        environment = stageObject.GetComponent<Environment>();
    }

    void Update()
    {
        CheckBlinking();
        CheckSpeakingOrListeningExpressionState();
    }

    public void Setup(string rosip)
    {
        rosClient = new RosbridgeWebSocketClient(rosip, ROS_PORT);
        Connect();
        SendAttentionCommand(0);
        LookAtChild();
        SampleEyeBlinkTime();
    }

    private bool Connect()
    {
        rosClient.OnReconnectSuccess(SetupPubSub);
        if (!rosClient.SetupSocket()) return false;
        SetupPubSub();
        publisherThread = new Thread(new ThreadStart(MessagePublisherProcess));
        publisherThread.Start();
        Debug.Log("CONNECTED");
        return connected;
    }

    public bool HasEntered()
    {
        return true;
    }

    public void LookAtChild()
    {
        Debug.Log("Look at child");
        currentLookat = LOOKAT_USER;
        Debug.Log($"CURRENT LOOKAT IS {currentLookat.x}, {currentLookat.y}, {currentLookat.z}");
        SendLookatCommand(currentLookat);
    }

    public void LookAtTablet()
    {
        Debug.Log("Look at tablet");
        currentLookat = LOOKAT_TABLET;
        Debug.Log($"CURRENT LOOKAT IS {currentLookat.x}, {currentLookat.y}, {currentLookat.z}");
        SendLookatCommand(currentLookat);
    }

    public void ShowExpression(RoboExpression expression)
    {
        nextAutoMotionTime = TimeKeeper.time + 2; // do not do auto motions for some time after showing an expression
        SendMotionCommand(RandomUtil.PickOne("ros-jibo-expr1", expressionVocabulary[expression]));
    }

    public void AtEase()
    {
        SendAttentionCommand(1);
    }

    public void SuggestObjectsOfInterest(List<GameObject> objectsOfInterest)
    {
    }

    void OnDestroy()
    {
        lock (messageQueue)
        {
            stopping = true;
            Monitor.Pulse(messageQueue);
        }
        if (null != rosClient)
        {
            rosClient.CloseSocket();
            rosClient.StopTimer();
        }
    }

    private void SetupPubSub()
    {
        string actionPubMessage = RosbridgeUtilities.GetROSJsonAdvertiseMsg(JIBO_ACTION_TOPIC, JIBO_ACTION_MESSAGE_TYPE);
        //string stateSubMessage = RosbridgeUtilities.GetROSJsonSubscribeMsg(JIBO_STATE_TOPIC, JIBO_STATE_MESSAGE_TYPE);
        // Send all advertisements to publish and subscribe to appropriate channels.
        connected = this.rosClient.SendMessage(actionPubMessage);// && this.rosClient.SendMessage(stateSubMessage);
    }

    private void SendLookatCommand(Vector3 lookat)
    {
        Dictionary<string, object> action_message = GetBlankActionMessage();
        action_message["do_lookat"] = true;
        action_message["lookat"] = SerializeVector(lookat);
        //Debug.Log($"Looking at {lookat.x: 0.000}, {lookat.y: 0.000}, {lookat.z: 0.000}");
        //UpdateAutoMotionPermittedTime();
        SendCommand(action_message);
    }

    private void SendMotionCommand(string motion)
    {
        Dictionary<string, object> action_message = GetBlankActionMessage();
        action_message["do_motion"] = true;
        action_message["motion"] = motion;
        SendCommand(action_message);
        SampleEyeBlinkTime();
    }

    private void SendCommand(Dictionary<string, object> command)
    {
        Dictionary<string, object> publish = new Dictionary<string, object>();
        publish.Add("topic", JIBO_ACTION_TOPIC);
        publish.Add("op", "publish");
        publish.Add("msg", command);
        string command_serialized = Json.Serialize(publish);
        //Debug.Log("SENDING " + command_serialized);
        lock (messageQueue)
        {
            messageQueue.Enqueue(command_serialized);
            Monitor.Pulse(messageQueue);
        }
    }

    private void MessagePublisherProcess()
    {
        while (true)
        {
            string message = AcquireMessageToSend();
            if (null == message) break;
            bool sent = false;
            while (!sent && !stopping) { sent = rosClient.SendMessage(message); }
            //Debug.Log("SENT " + message);
        }
    }

    private string AcquireMessageToSend()
    {
        lock (messageQueue)
        {
            while (0 == messageQueue.Count)
            {
                Monitor.Wait(messageQueue);
                if (stopping) return null;
            }
            return messageQueue.Dequeue();
        }
    }

    private Dictionary<string, float> SerializeVector(Vector3 vector)
    {
        return new Dictionary<string, float>() { { "x", vector.x }, { "y", vector.y }, { "z", vector.z } };
    }

    private Dictionary<string, object> GetBlankActionMessage()
    {
        Dictionary<string, object> action_message = new Dictionary<string, object>();
        action_message["header"] = RosbridgeUtilities.GetROSHeader();
        action_message["do_motion"] = false;
        action_message["motion"] = "";
        action_message["do_lookat"] = false;
        action_message["lookat"] = SerializeVector(currentLookat);
        action_message["do_tts"] = false;
        action_message["tts_text"] = "";
        action_message["tts_duration_stretch"] = 1.0f;
        action_message["tts_pitch"] = 0.5f;
        action_message["do_mim"] = false;
        action_message["mim_body"] = "";
        action_message["mim_rule"] = "";
        action_message["do_sound_playback"] = false;
        action_message["audio_filename"] = "";
        action_message["do_led"] = false;
        action_message["led_color"] = LED_WHITE;
        action_message["do_volume"] = false;
        action_message["volume"] = 1f;
        action_message["do_anim_transition"] = false;
        action_message["anim_transition"] = (byte)0;
        action_message["do_attention_mode"] = false;
        action_message["attention_mode"] = (byte)0;
        return action_message;
    }

    private void CheckBlinking()
    {
        if (TimeKeeper.time > nextEyeBlinkTime && targetColor != LED_BLUE) // hold blinking while listening in order not to reset the listening eye
        {
            BlinkEye();
        }
    }

    private void BlinkEye()
    {
        SendMotionCommand("Misc/eye_medium_blink_01.keys");
        SampleEyeBlinkTime();
    }

    private void SampleEyeBlinkTime()
    {
        nextEyeBlinkTime = TimeKeeper.time + RandomUtil.SampleExponential("ros-jibo-eye1", BLINK_MEAN_PERIOD);
    }

    private void CheckOnAutoMotion()
    {
        if (TimeKeeper.time > nextAutoMotionTime)
        {
            Vector3 lookatTarget = RandomUtil.DeviateDirection("ros-jibo-dev1", currentLookat, Mathf.PI / 15);
            SendLookatCommand(lookatTarget);
            nextAutoMotionTime = TimeKeeper.time + RandomUtil.SampleExponential("ros-jibo-mot1", AUTO_MOTION_MEAN_PERIOD);
        }
    }

    private void CheckSpeakingOrListeningExpressionState()
    {
        if (voiceActivityDetector.IsPickingVoice())
        {
            if (targetColor != LED_BLUE)
            {
                SendMotionCommand("eyeListen.keys");
                SendLEDCommand(LED_BLUE);
                nextAutoMotionTime = TimeKeeper.time;
            }
        }
        else if (synthesizer.IsSpeaking() || environment.IssueAnnouncementInProgress())
        {
            if (targetColor == LED_BLUE) { BlinkEye(); } // reset the listening eye
            CheckOnAutoMotion();
            if (TimeKeeper.time - lastLEDBlinkTime > 0.15)
            {
                UpdateLEDBlinking();
            }
        }
        else if (targetColor != LED_BLACK)
        {
            if (targetColor == LED_BLUE) { BlinkEye(); } // reset the listening eye
            SendLEDCommand(LED_BLACK);
            nextAutoMotionTime = TimeKeeper.time;
        }
    }
    private void UpdateLEDBlinking()
    {
        Dictionary<string, float> ledColor = (0 == ledState % 2) ? LED_GREEN : LED_LIGHT_GREEN;
        ledState += 1;
        lastLEDBlinkTime = TimeKeeper.time;
        SendLEDCommand(ledColor);
    }

    private void SendLEDCommand(Dictionary<string, float> ledColor)
    {
        Dictionary<string, object> action_message = GetBlankActionMessage();
        action_message["do_led"] = true;
        action_message["led_color"] = ledColor;
        targetColor = ledColor;
        SendCommand(action_message);
    }

    private void SendAttentionCommand(byte attentionMode)
    {
        Dictionary<string, object> action_message = GetBlankActionMessage();
        action_message["do_attention_mode"] = true;
        action_message["attention_mode"] = attentionMode;
        SendCommand(action_message);
    }

    public void TriggerJumpToWordBoxEnd()
    {

    }
}
