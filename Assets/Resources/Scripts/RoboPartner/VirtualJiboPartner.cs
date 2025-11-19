using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class VirtualJiboPartner : MonoBehaviour, IRoboPartner
{
    private Transform jiboTform;
    private Transform jiboFace;
    private Transform jiboFaceAssembly;
    private Transform jiboRim;
    private Transform jiboBackOfHead;
    private Transform jiboRing;
    private Transform jiboBody;
    private Transform jiboScreen;
    private Transform jiboEyeVantagePoint;
    private Transform jiboEye;
    private Transform jiboEyeCircle;
    private Transform jiboEyelid;
    private Transform[] gears = new Transform[2];
    private Transform[] ears = new Transform[2];

    private const float ALPHA = Mathf.Deg2Rad * 20;
    private float BODY_RADIUS;
    private float CENTER_HEIGHT;
    private float BODY_SCALE;

    private Vector3 currentHeadJointAxis;
    private Vector3 currentMidJointAxis;

    private CoroutineRunner animator = new CoroutineRunner();
    private int animationType = ANIMATION_TYPE_NONE;
    private CoroutineRunner locationAdjustor = new CoroutineRunner();
    private CoroutineRunner eyeController = new CoroutineRunner();
    private bool blinkingHasBeenDisrupted = false;

    private const int ANIMATION_TYPE_NONE = 0;
    private const int ANIMATION_TYPE_LOOKAT = 1;
    private const int ANIMATION_TYPE_EXPRESSION = 2;
    private const int ANIMATION_TYPE_CHANGE_LOCATION = 3;

    private const float MAX_SQUISH = 0.75f;

    private const float LOOKAT_GAZE_DURATION = 0.1f;
    private const float LOOKAT_DURATION = 0.6f;
    private const float BLINK_DURATION = 0.2f;
    private const float LAUNCH_LAND_DURATION = 0.1f;
    private const float IN_THE_AIR_DURATION = 0.4f;
    private const float JUMP_HEIGHT = 3f;
    private const float ADJUSTMENT_DURATION = 0.3f;
    private const float HEAD_TILT_DURATION = 0.6f;

    private const float EYE_NARROW_DURATION = 0.2f;
    private const float EYE_WIDEN_DURATION = 0.6f;
    private const float EYE_SMILE_DURATION = 0.2f;
    private const float EYE_UNSMILE_DURATION = 0.4f;

    private const float TILT_NORMAL_RANGE = Mathf.Deg2Rad * 30;
    private const float TILT_SMALL_RANGE = Mathf.Deg2Rad * 10;

    private const float BLINK_MEAN_PERIOD = 4f;
    private const float AUTO_MOTION_MEAN_PERIOD_WHEN_TALKIN = 1.5f;
    private const float AUTO_MOTION_MEAN_PERIOD_WHEN_NOT_TALKIN = 15f;
    private const float LOSS_OF_INTEREST_MEAN_PERIOD = 20f;

    private const float GEAR_DEGREE_PER_SECOND = 180f;

    private Color LIGHT_COLOR;
    private Color SHADOW_COLOR;

    private StageOrchestrator stageOrchestrator;
    private string currentLocation = "login";

    private GameObject objectOfInterest = null;
    private double changeOfInterestTime = float.MaxValue;

    private Action<List<GameObject>, string>[] objectOfInterestSources = null;
    private List<GameObject> objectsOfInterestBuffer = new List<GameObject>();

    private bool lookingAtTablet = false;

    private Color LED_GREEN = new Color(0, 1, 0);
    private Color LED_LIGHT_GREEN = new Color(0, 0.75f, 0);
    private Color LED_BLUE = new Color(55f / 255f, 198f / 255f, 1);
    private Color LED_BLACK = new Color(0, 0, 0);

    private SynthesizerController synthesizer;
    private VoiceActivityDetector voiceActivityDetector;
    private SpeechRecoServerComm speechRecoServerComm;
    private SpeechRecoButton speechRecoButton;
    private Material jiboLEDMaterial;
    private double lastLEDBlinkTime = 0;
    private bool blinkState = true;
    private double lastSpeechRingDrop = -1f;
    private GameObject speechRingPrefab = null;

    private const float SPEECH_RING_DROP_PERIOD = 0.2f;
    private const float SPEECH_RING_EXPANSION_PERIOD = 0.5f;
    private const float SPEECH_RING_EXPANSION_FACTOR = 1.8f;
    private const float SPEECH_RING_DECAY_PERIOD = 0.15f;

    private const float EAR_PULSE_PERIOD = 1f;
    private const float EAR_PULSE_FACTOR = 0.1f;

    private Quaternion[] poseQuatParams = { Quaternion.identity, Quaternion.identity };
    private Quaternion[] poseQuatDevs   = { Quaternion.identity, Quaternion.identity };
    private float[]      poseScalarParams = { 0, 1 };

    private const int POSE_PARAM_FACEDIR = 0;
    private const int POSE_PARAM_GAZEDIR = 1;
    private const int POSE_PARAM_TILT = 0;
    private const int POSE_PARAM_SQUISH = 1;

    private const float FACE_DEV_ANGLE = Mathf.PI / 15f;
    private const float GAZE_DEV_ANGLE_FACTOR = 0.1f;

    private AnimationMaster animaster;
    private Environment environment;

    private const float SCALE_RELATIVE_TO_HELP_BUTTON = 0.65f;
    private const float Y_OFFSET_RELATIVE_TO_HELP_BUTTON = -0.88f;

    private WordBox bottomWordBox;
    private float celebrationPause = 0.18f;
    private bool isJumpingToWordBoxEnd = false;
    private Scaffolder scaffolder;


    private static Dictionary<string, Vector3> positionByLocation = new Dictionary<string, Vector3>()
    {
        {"title_page", new Vector3(0, -4.02f, -20f)},
        {"login", new Vector3(0, 5.98f, -20f)}
    };

    private static Dictionary<string, float> scaleByLocation = new Dictionary<string, float>()
    {
        {"title_page", 0.7f},
        {"login", 0.7f}
    };

    private static Dictionary<string, float> customButtonScales = new Dictionary<string, float>()
    {
        {"word_bank", 0.6f },
        {"staging_area", 0.9f },
        {"keyboard", 0.6f },
        {"canvas", 0.6f },
        {"avatar_picker", 0.6f },
        {"avatar_selector_panel", 0.6f }
    };

    public static void ConfigureUIForVirtualJibo()
    {
        ConfigureHelpButton("canvas", GameObject.FindGameObjectsWithTag("InterfaceHelpButton").First(gObj => null == gObj.transform.parent).transform);
        WordDrawer wordDrawer = GameObject.FindWithTag("WordDrawer").GetComponent<WordDrawer>();
        ConfigureHelpButton("word_bank", wordDrawer.GetWordBank().transform.Find("help_button"));
        ConfigureHelpButton("staging_area", wordDrawer.GetStagingArea().transform.Find("help_button"));
        ConfigureHelpButton("keyboard", wordDrawer.GetKeyboard().transform.Find("help_button"));
        AvatarPicker avatarPicker = GameObject.FindWithTag("AvatarPicker").GetComponent<AvatarPicker>();
        ConfigureHelpButton("avatar_picker", avatarPicker.transform.Find("help_button"));
        ConfigureHelpButton("avatar_selector_panel", avatarPicker.GetSelectorPanel().transform.Find("help_button"));
        GameObject galleryPagePrefab = Resources.Load<GameObject>("Prefabs/GalleryPage");
        ConfigureHelpButton("gallery", galleryPagePrefab.transform.Find("help_button"));
    }

    public static void ReconfigureUI()
    {
        ConfigureHelpButton("canvas", GameObject.FindGameObjectsWithTag("InterfaceHelpButton").First(gObj => null == gObj.transform.parent).transform);
    }

    // Start is called before the first frame update
    void Start()
    {
        GameObject jiboObject = Instantiate(Resources.Load<GameObject>("Prefabs/jibo"));
        jiboTform = jiboObject.transform;
        jiboTform.position = positionByLocation["login"];
        float scale = scaleByLocation["login"];
        jiboTform.localScale = new Vector3(scale, scale, 1);
        jiboFace = jiboTform.Find("jibo-face");
        jiboFaceAssembly = jiboFace.Find("jibo-face-assembly");
        jiboScreen = jiboFaceAssembly.Find("jibo-screen");
        jiboRim = jiboFaceAssembly.Find("jibo-face-rim");
        jiboBackOfHead = jiboTform.Find("jibo-back-of-head");
        jiboBody = jiboTform.Find("jibo-body");
        jiboRing = jiboBody.Find("led-ring");
        jiboLEDMaterial = jiboRing.Find("led-cylinder").GetComponent<MeshRenderer>().material;
        jiboEyeVantagePoint = jiboRim.Find("eye-vantage-point");
        jiboEye = jiboScreen.Find("jibo-eye");
        jiboEyelid = jiboEye.Find("eyelid");
        jiboEyeCircle = jiboEye.Find("eye-circle");
        gears[0] = jiboScreen.Find("gear-1");
        gears[1] = jiboScreen.Find("gear-2");
        for (int i = 0; i < 2; ++i) { gears[i].gameObject.SetActive(false); }
        ears[0] = jiboFaceAssembly.Find("jibo-left-ear");
        ears[1] = jiboFaceAssembly.Find("jibo-right-ear");
        for (int i = 0; i < 2; ++i) { ears[i].gameObject.SetActive(false); }
        jiboEyelid.transform.localScale = new Vector3(1, 0, 1);
        CENTER_HEIGHT = jiboBody.localPosition.y;
        BODY_RADIUS = jiboFace.transform.localPosition.y - CENTER_HEIGHT;
        BODY_SCALE = jiboBody.localScale.x;
        LIGHT_COLOR = jiboRim.GetComponent<SpriteRenderer>().color;
        SHADOW_COLOR = jiboBackOfHead.Find("half-circle").GetComponent<SpriteRenderer>().color;
        LookTowards(-Vector3.forward, -Vector3.forward, tilt: 0, squish: 1);
        stageOrchestrator = GetComponent<StageOrchestrator>();
        TouchManager touchManager = GetComponent<TouchManager>();
        Scaffolder scaffolder = GetComponent<Scaffolder>();
        Tutorial tutorial = GetComponent<Tutorial>();
        MachineDriver machineDriver = GetComponent<MachineDriver>();
        AssociationsPanel assocPanel = GameObject.FindWithTag("AssociationsPanel").GetComponent<AssociationsPanel>();
        objectOfInterestSources = new Action<List<GameObject>, string>[] { machineDriver.GetObjectsOfAttention, tutorial.GetObjectsOfAttention, scaffolder.GetObjectsOfAttention, touchManager.GetCurrentlyDragged, touchManager.GetCurrentlyAwaitingTap, assocPanel.GetAttentionFocus };
        synthesizer = GetComponent<SynthesizerController>();
        voiceActivityDetector = GetComponent<VoiceActivityDetector>();
        speechRecoServerComm = GetComponent<SpeechRecoServerComm>();
        animaster = GetComponent<AnimationMaster>();
        environment = GetComponent<Environment>();
        speechRingPrefab = Resources.Load<GameObject>("Prefabs/speech-ring");
        eyeController.SetCoroutine(BlinkingCoroutine());
        speechRecoButton = GameObject.FindWithTag("SpeechRecoButton")?.GetComponent<SpeechRecoButton>();


        GameObject wordDrawerGO = GameObject.FindWithTag("WordDrawer");
        if (wordDrawerGO != null)
        {
            bottomWordBox = wordDrawerGO.GetComponentInChildren<WordBox>();
            if (bottomWordBox == null)
            {
                Debug.LogError("WordBox not found as a child of WordDrawer!");
            }
        }
        else
        {
            Debug.LogError("WordDrawer GameObject not found!");
        }
    }


    void OnDestroy()
    {
        if (null != jiboTform && null != jiboTform.gameObject) { Destroy(jiboTform.gameObject); }
        foreach (GameObject speechRing in GameObject.FindGameObjectsWithTag("SpeechRing")) { Destroy(speechRing); }
    }

    public bool HasEntered()
    {
        return "login" != currentLocation && animationType != ANIMATION_TYPE_CHANGE_LOCATION;
    }

    public void LookAtChild()
    {
        if (lookingAtTablet)
        {
            lookingAtTablet = false;
            if (animationType <= ANIMATION_TYPE_LOOKAT) { _LookAtChild(); }
        }
    }

    public void LookAtTablet()
    {
        if (!lookingAtTablet)
        {
            lookingAtTablet = true;
            if (animationType <= ANIMATION_TYPE_LOOKAT) { _LookAtTablet(); }
        }
    }

    public void ShowExpression(RoboExpression expression)
    {
        if (animationType >= ANIMATION_TYPE_EXPRESSION) return;
        switch (expression)
        {
            case RoboExpression.GREETING:
                //Debug.Log("ANIMATOR: GREETING");
                animator.SetCoroutine(AnimateWiggle(wiggleCycles: 3, wiggleDuration: 2.5f));
                break;
            case RoboExpression.HAPPY:
        
            case RoboExpression.EXCITED:
                switch (RandomUtil.Range("jibo-expr1", 6, 7))
                {
                    case 0:
                        //Debug.Log("ANIMATOR: HAPPY WIGGLE");
                        animator.SetCoroutine(AnimateWiggle(wiggleCycles: 2, wiggleDuration: 1.75f));
                        break;
                    case 1:
                        Debug.Log("ANIMATOR: SPIN");
                        animator.SetCoroutine(AnimateSpin(1.25f));
                        break;
                    case 2:
                        //Debug.Log("ANIMATOR: EXCITED JUMP");
                        animator.SetCoroutine(AnimateExcitedJump());
                        break;
                    case 3:
                        animator.SetCoroutine(AnimateHappyWiggleJump());  
                        break;
                    case 4:
                        animator.SetCoroutine(AnimateHappySpinJump());  
                        break;
                    case 5:
                        animator.SetCoroutine(JumpToWordBoxEnd());
                        break;
                    case 6:
                        animator.SetCoroutine(JumpToLastPlacedBlock()); // Current Excited Animation
                        break;
                }       
                break;

            case RoboExpression.PUZZLED:
            case RoboExpression.CURIOUS:
                //Debug.Log("ANIMATOR: HEAD TILT");
                animator.SetCoroutine(AnimateHeadTilt());
                break;
            case RoboExpression.EMBARRASSED:
                //Debug.Log("ANIMATOR: SADNESS");
                animator.SetCoroutine(AnimateSadness());
                break;
        }
    }

    public bool IsShowingExpression()
    {
        return ANIMATION_TYPE_EXPRESSION == animationType;
    }

    public void AtEase()
    { }

    public void SuggestObjectsOfInterest(List<GameObject> objectsOfInterest)
    {
        if (0 == objectsOfInterest.Count) return;
        if (null == objectOfInterest || changeOfInterestTime != float.MaxValue)
        {
            AssignObjectOfInterest(RandomUtil.PickOne("jibo-interest1", objectsOfInterest));
        }
    }

    private static void ConfigureHelpButton(string locationCode, Transform helpButtonTform)
    {
        helpButtonTform.GetComponent<HelpButton>().ActivateJiboMode();
        if (customButtonScales.ContainsKey(locationCode))
        {
            float customScale = customButtonScales[locationCode];
            helpButtonTform.localScale = new Vector3(customScale, customScale, 1);
        }
        float helpButtonScale = helpButtonTform.localScale.y;
        scaleByLocation[locationCode] = helpButtonScale * SCALE_RELATIVE_TO_HELP_BUTTON;
        Vector3 helpButtonLocation = helpButtonTform.localPosition;
        Vector3 targetLocation = new Vector3(helpButtonLocation.x,
                                             helpButtonLocation.y + Y_OFFSET_RELATIVE_TO_HELP_BUTTON * helpButtonScale,
                                             helpButtonLocation.z - 0.1f);
        positionByLocation[locationCode] = targetLocation;
    }

    private void _LookAtChild()
    {
        //Debug.Log("ANIMATOR: WATCH VEC FORWARD");
        animator.SetCoroutine(Watch(-Vector3.forward));
    }

    private void _LookAtTablet()
    {
        if (null != objectOfInterest)
        {
            //Debug.Log("ANIMATOR: WATCH OBJ OF INTEREST");
            animator.SetCoroutine(Watch(objectOfInterest));
        }
        else
        {
            FocusOnAPointOfInterest();
        }
    }




    private void Update()
    {
        UpdateFocusOfInterest();
        CheckLocation();
        UpdateAnimation();
        UpdatePose();
        UpdateAcousticIndicators();
        UpdateThinkingIndicator();
    }

    private void UpdatePose()
    {
        LookTowards(faceDirection: (poseQuatParams[POSE_PARAM_FACEDIR] * poseQuatDevs[POSE_PARAM_FACEDIR]) * Vector3.forward,
                    gazeDirection: (poseQuatParams[POSE_PARAM_GAZEDIR] * poseQuatDevs[POSE_PARAM_GAZEDIR]) * Vector3.forward,
                    tilt: poseScalarParams[POSE_PARAM_TILT],
                    squish: poseScalarParams[POSE_PARAM_SQUISH]);
    }

    private void UpdateAcousticIndicators()
    {
        if (voiceActivityDetector.IsPickingVoice())
        {
            jiboLEDMaterial.SetColor("_EmissionColor", LED_BLUE);
        }
        else if (ThereIsSpeech())
        {
            if (TimeKeeper.time - lastLEDBlinkTime > 0.15)
            {
                if (blinkState)
                {
                    jiboLEDMaterial.SetColor("_EmissionColor", LED_GREEN);
                }
                else
                {
                    jiboLEDMaterial.SetColor("_EmissionColor", LED_LIGHT_GREEN);
                }
                blinkState = !blinkState;
                lastLEDBlinkTime = TimeKeeper.time;
            }
        }
        else
        {
            jiboLEDMaterial.SetColor("_EmissionColor", LED_BLACK);
        }
        if (ThereIsSpeech() && TimeKeeper.time - lastSpeechRingDrop > SPEECH_RING_DROP_PERIOD)
        {
            DropSpeechRing();
        }
        UpdateEars();
    }

    private void UpdateEars()
    {
        if (voiceActivityDetector.IsActivelyRecording())
        {
            if (!ears[0].gameObject.activeSelf)
            {
                for (int i = 0; i < 2; ++i)
                {
                    ears[i].gameObject.SetActive(true);
                }
            }
            double t = 2 * Math.PI * (TimeKeeper.time % EAR_PULSE_PERIOD) / EAR_PULSE_PERIOD;
            float scale = (float)Math.Exp(EAR_PULSE_FACTOR * Math.Sin(t));
            for (int i = 0; i < 2; ++i)
            {
                ears[i].transform.localScale = new Vector3(scale, scale, 1);
            }
        }
        else
        {
            if (ears[0].gameObject.activeSelf)
            {
                for (int i = 0; i < 2; ++i)
                {
                    ears[i].gameObject.SetActive(false);
                }
            }
        }
    }

    private bool ThereIsSpeech()
    {
        return synthesizer.IsActivelySpeaking() || environment.IssueAnnouncementInProgress();
    }

    private void UpdateThinkingIndicator()
    {
        if (speechRecoServerComm.IsRecognizing() && null != speechRecoButton && speechRecoButton.ButtonIsActive())
        {
            jiboEyeCircle.gameObject.SetActive(false);
            jiboEyelid.gameObject.SetActive(false);
            for (int i = 0; i < 2; ++i) { gears[i].gameObject.SetActive(true); }
            float t = (float)(TimeKeeper.time * GEAR_DEGREE_PER_SECOND % 360.0);
            gears[0].transform.rotation = Quaternion.Euler(0, 0, -t);
            gears[1].transform.rotation = Quaternion.Euler(0, 0, t);
        }
        else
        {
            jiboEyeCircle.gameObject.SetActive(true);
            jiboEyelid.gameObject.SetActive(true);
            for (int i = 0; i < 2; ++i) { gears[i].gameObject.SetActive(false); }
        }
    }

    private void DropSpeechRing()
    {
        GameObject speechRing = Instantiate(speechRingPrefab);
        speechRing.transform.localScale = jiboTform.localScale;
        StartCoroutine(SpeechRingExpansionCoroutine(speechRing));
        lastSpeechRingDrop = TimeKeeper.time;
    }

    private void UpdateAnimation()
    {
        animator.Update();
        eyeController.Update();
        if (!animator.IsRunning()) { animationType = ANIMATION_TYPE_NONE; }
        locationAdjustor.Update();
    }

    private void CheckLocation()
    {
        string locationCode = GetLocationCode();
        if (null != locationCode && locationCode != currentLocation)
        {
            if (positionByLocation.ContainsKey(locationCode))
            {
                Vector3 targetPosition = positionByLocation[locationCode];
                float targetScale = scaleByLocation[locationCode];
                float distance = Vector2.Distance(targetPosition, jiboTform.position);
                //if (animationType == ANIMATION_TYPE_CHANGE_LOCATION) { animator.SetCoroutine(null); }
                if (locationAdjustor.IsRunning()) { locationAdjustor.SetCoroutine(null); }
                if (distance > 3.5f)
                {
                    //Debug.Log("ANIMATOR: JUMP");
                    animator.SetCoroutine(Jump(targetPosition, targetScale));
                }
                else if (distance > 0.01f || Mathf.Abs(targetScale - jiboTform.localScale.x) > 0.01f)
                {
                    locationAdjustor.SetCoroutine(AdjustLocation(targetPosition, targetScale));
                }
            }
            currentLocation = locationCode;
        }
    }

    private string GetLocationCode()
    {
        string activePanel = stageOrchestrator.GetStage();
        if (activePanel == "title_page" && null == environment.GetUser()) { return "login"; }
        return activePanel;
    }

    private void UpdateFocusOfInterest()
    {
        GameObject newObjOfInterest = PickObjectOfInterest();
        if (null == newObjOfInterest)
        {
            if (changeOfInterestTime == float.MaxValue)
            {
                if (lookingAtTablet) { SampleChangeOfInterestTime(); }
            }
            else if (TimeKeeper.time > changeOfInterestTime)
            {
                objectOfInterest = null;
                if (lookingAtTablet)
                {
                    FocusOnAPointOfInterest();
                }
                else
                {
                    changeOfInterestTime = float.MaxValue;
                }
            }
        }
        else if (objectOfInterest != newObjOfInterest)
        {
            AssignObjectOfInterest(newObjOfInterest);
        }
    }

    private void AssignObjectOfInterest(GameObject objOfInterest)
    {
        objectOfInterest = objOfInterest;
        if (lookingAtTablet && animationType <= ANIMATION_TYPE_LOOKAT)
        {
            //Debug.Log("ANIMATOR: WATCH OBJ OF INTEREST");
            animator.SetCoroutine(Watch(objectOfInterest));
        }
        changeOfInterestTime = float.MaxValue;
    }

    private void FocusOnAPointOfInterest()
    {
        if (animationType <= ANIMATION_TYPE_LOOKAT) {
            //Debug.Log("ANIMATOR: WATCH POINT OF INTEREST");
            animator.SetCoroutine(Watch(RandomPointOnScreen()));
            SampleChangeOfInterestTime();
            animationType = ANIMATION_TYPE_LOOKAT;
        }
    }

    private Vector2 RandomPointOnScreen()
    {
        float ySpan = Camera.main.orthographicSize;
        float xSpan = Camera.main.aspect * ySpan;
        return new Vector2(RandomUtil.Range("jibo-point1", -xSpan, xSpan), RandomUtil.Range("jibo-point2", -ySpan, ySpan));
    }

    private void SampleChangeOfInterestTime()
    {
        changeOfInterestTime = TimeKeeper.time + RandomUtil.SampleExponential("jibo-t-interest1", LOSS_OF_INTEREST_MEAN_PERIOD);
    }

    private GameObject PickObjectOfInterest()
    {
        string stage = stageOrchestrator.GetStage();
        foreach (Action<List<GameObject>, string> objectOfInterestSource in objectOfInterestSources)
        {
            GameObject objOfInterest = null;
            try
            {
                objectOfInterestSource(objectsOfInterestBuffer, stage);
                objOfInterest = PickObjectOfInterest(objectsOfInterestBuffer);
            }
            catch (Exception e)
            {
                ExceptionUtil.OnException(e);
            }
            objectsOfInterestBuffer.Clear();
            if (null != objOfInterest) return objOfInterest;
        }
        return null;
    }

    private GameObject PickObjectOfInterest(List<GameObject> candidates)
    {
        if (null != objectOfInterest && candidates.Contains(objectOfInterest)) return objectOfInterest;
        if (0 == candidates.Count) return null;
        return RandomUtil.PickOne("jibo-obj-interest1", candidates);
    }

    private void LookTowards(Vector3 faceDirection, Vector3 gazeDirection, float tilt, float squish)
    {
        faceDirection = faceDirection.normalized;
        gazeDirection = gazeDirection.normalized;
        SquishBody(squish);
        currentHeadJointAxis = CalculateHeadJointAxis(faceDirection, tilt);
        currentMidJointAxis = CalculateMidJointAxis(currentHeadJointAxis);
        PositionRing(currentMidJointAxis);
        PositionHead(faceDirection, currentHeadJointAxis, currentMidJointAxis, squish);
        PositionEye(gazeDirection, faceDirection);
    }

    private Vector3 CalculateHeadJointAxis(Vector3 faceDirection, float tilt)
    {
        Vector3 dirUntiltedLeft = Vector3.Cross(faceDirection, Vector3.up).normalized;
        Vector3 dirUntiltedUp = Vector3.Cross(dirUntiltedLeft, faceDirection).normalized;
        Vector3 dirTiltedUp = Mathf.Cos(tilt) * dirUntiltedUp + Mathf.Sin(tilt) * dirUntiltedLeft;
        Vector3 jointAxis = Mathf.Cos(ALPHA) * dirTiltedUp + Mathf.Sin(ALPHA) * faceDirection;
        if (Mathf.Cos(ALPHA) >= Vector3.Dot(jointAxis, Vector3.up)) return jointAxis;
        return IntersectCones(Vector3.up, faceDirection, ALPHA, Mathf.PI / 2 - ALPHA);
    }

    private Vector3 CalculateMidJointAxis(Vector3 headJointAxis)
    {
        return IntersectCones(Vector3.up, headJointAxis, ALPHA, 2 * ALPHA);
    }

    private Vector3 IntersectCones(Vector3 cone_1_axis, Vector3 cone_2_axis, float alpha_1, float alpha_2)
    {
        float cos_alpha_1 = Mathf.Cos(alpha_1);
        float sin_alpha_1 = Mathf.Sqrt(1 - Mathf.Pow(cos_alpha_1, 2));
        float tan_alpha_1 = sin_alpha_1 / cos_alpha_1;
        float cos_alpha_2 = Mathf.Cos(alpha_2);
        float cos_beta = Vector3.Dot(cone_1_axis, cone_2_axis);
        float sin_beta = Mathf.Sqrt(1 - Mathf.Pow(cos_beta, 2));
        float cos_fi_1 = (cos_alpha_2 / cos_alpha_1 - cos_beta) / (sin_beta * tan_alpha_1);
        if (cos_fi_1 > 1) { cos_fi_1 = 1; }
        if (cos_fi_1 < -1) { cos_fi_1 = -1; }
        float sin_fi_1 = Mathf.Sqrt(1 - Mathf.Pow(cos_fi_1, 2));
        Vector3 sharedPlaneNormal = Vector3.Cross(cone_1_axis, cone_2_axis).normalized;
        Vector3 towardsMid = Vector3.Cross(cone_1_axis, sharedPlaneNormal).normalized;
        Vector3 coneLowerPoint = tan_alpha_1 * (cos_fi_1 * towardsMid + sin_fi_1 * sharedPlaneNormal);
        Vector3 jointAxis = cone_1_axis - coneLowerPoint;
        return jointAxis;
    }

    private void SquishBody(float yScale)
    {
        Vector3 bodyPosition = jiboBody.localPosition;
        jiboBody.localPosition = new Vector3(bodyPosition.x, yScale * CENTER_HEIGHT, bodyPosition.z);
        jiboBody.localScale = new Vector3(BODY_SCALE / yScale, BODY_SCALE * yScale, 1);
    }

    private void PositionRing(Vector3 midJointAxis)
    {
        jiboRing.rotation = Quaternion.LookRotation(midJointAxis);
    }

    private void PositionHead(Vector3 faceDirection, Vector3 headJointAxis, Vector3 midJointAxis, float yScale)
    {
        Vector3 axisForward = faceDirection.normalized;
        jiboFace.rotation = Quaternion.LookRotation(-axisForward, headJointAxis);
        Vector3 midBodyAxis = (headJointAxis + midJointAxis).normalized;
        Vector3 headOffset = new Vector3(BODY_RADIUS * midBodyAxis.x, BODY_RADIUS * yScale * midBodyAxis.y, BODY_RADIUS * midBodyAxis.z);
        Vector3 facePosition = jiboBody.localPosition + headOffset + 0.5f * BODY_RADIUS * faceDirection;
        jiboFace.localPosition = new Vector3(facePosition.x, facePosition.y, jiboFace.localPosition.z);
        Vector3 rimPos = jiboRim.transform.position;
        jiboBackOfHead.position = new Vector3(rimPos.x, rimPos.y, rimPos.z + 3);
        Vector3 creaseAxis = Vector3.Cross(axisForward, Vector3.forward);
        jiboBackOfHead.transform.rotation = Quaternion.Euler(0, 0, Mathf.Rad2Deg * Mathf.Atan2(creaseAxis.y, creaseAxis.x));
        PickHeadRenderingMode(faceDirection);
    }

    private void PickHeadRenderingMode(Vector3 faceDirection)
    {
        if (faceDirection.z < 0)
        {
            jiboRim.GetComponent<SpriteRenderer>().color = LIGHT_COLOR;
            jiboScreen.gameObject.SetActive(true);
        }
        else
        {
            jiboRim.GetComponent<SpriteRenderer>().color = SHADOW_COLOR;
            jiboScreen.gameObject.SetActive(false);
        }
    }

    private void PositionEye(Vector3 gazeDirection, Vector3 faceDirection)
    {
        Vector3 pointOnFace = jiboEyeVantagePoint.position + (-jiboEyeVantagePoint.position.z) * gazeDirection / Vector3.Dot(gazeDirection, faceDirection);
        Vector3 localPointOnFace = jiboRim.worldToLocalMatrix.MultiplyPoint(pointOnFace);
        float offsetFromCenter = localPointOnFace.magnitude;
        if (offsetFromCenter > 0.8f) { localPointOnFace = (0.8f / offsetFromCenter) * localPointOnFace; }
        jiboEye.localPosition = new Vector3(localPointOnFace.x, localPointOnFace.y, -0.2f);
    }

    private Vector3 GetFaceDirection(Vector2 target)
    {
        return new Vector3(target.x - jiboRim.position.x, target.y - jiboRim.position.y, -10).normalized;
    }

    private Vector3 GetGazeDirection(Vector2 target)
    {
        return new Vector3(target.x - jiboRim.position.x, target.y - jiboRim.position.y, -7).normalized;
    }

    private Vector3 GetFaceDirection(GameObject target)
    {
        if (null == target) return Vector3.zero;
        return GetFaceDirection(target.transform.position);
    }

    private Vector3 GetGazeDirection(GameObject target)
    {
        if (null == target) return Vector3.zero;
        return GetGazeDirection(target.transform.position);
    }

    private IEnumerator ControlAutoMotions(float startDelay)
    {
        if (startDelay > 0)
        {
            yield return CoroutineUtils.WaitCoroutine(startDelay);
            startDelay = 0;
        }
        bool sampledForSpeaking = ThereIsSpeech();
        double nextMotion = startDelay < 0 ? TimeKeeper.time : SampleNextAutoMotion();
        while (true)
        {
            if (TimeKeeper.time >= nextMotion)
            {
                //Debug.Log("PERFORMING AUTO MOTION");
                yield return PerformAutoMotion();
                //Debug.Log("SAMPLING NEXT AUTO MOTION");
                nextMotion = SampleNextAutoMotion();
                sampledForSpeaking = ThereIsSpeech();
            }
            else if (sampledForSpeaking != ThereIsSpeech())
            {
                sampledForSpeaking = ThereIsSpeech();
                if (ThereIsSpeech())
                {
                    //Debug.Log("PERFORMING AUTO MOTION");
                    yield return PerformAutoMotion();
                    //Debug.Log("SAMPLING NEXT AUTO MOTION");
                    nextMotion = SampleNextAutoMotion();
                }
                else
                {
                    //Debug.Log("SAMPLING NEXT AUTO MOTION");
                    nextMotion = SampleNextAutoMotion();
                }
            }
            yield return null;
        }
    }

    private double SampleNextAutoMotion()
    {
        float autoMotionMeanPeriod = ThereIsSpeech() ? AUTO_MOTION_MEAN_PERIOD_WHEN_TALKIN : AUTO_MOTION_MEAN_PERIOD_WHEN_NOT_TALKIN;
        return TimeKeeper.time + RandomUtil.SampleExponential("jibo-t-motion1", autoMotionMeanPeriod);
    }

    private IEnumerator PerformAutoMotion()
    {
        float[] deviationAngles = { 0, 0 };
        deviationAngles[POSE_PARAM_FACEDIR] = RandomUtil.ApproxNormal("jibo-motion1", FACE_DEV_ANGLE, 3 * FACE_DEV_ANGLE);
        deviationAngles[POSE_PARAM_GAZEDIR] = GAZE_DEV_ANGLE_FACTOR * deviationAngles[POSE_PARAM_FACEDIR];
        float deviationDirAngle = RandomUtil.Range("jibo-motion-2", 0, Mathf.PI);
        IEnumerator[] microMovementEnumerators = new IEnumerator[3];
        for (int i = 0; i < 2; ++i) {
            Vector3 deviationDir = RandomUtil.DeviateDirection(Vector3.forward, deviationAngles[i], deviationDirAngle);
            microMovementEnumerators[i] = TransitionQuatParam(poseQuatDevs, i, deviationDir, LOOKAT_DURATION, Easing.EaseInOut);
        }
        microMovementEnumerators[2] = TransitionScalarParam(POSE_PARAM_TILT, SampleTilt(), LOOKAT_DURATION, Easing.EaseInOut);
        return CoroutineUtils.RunUntilAllStop(microMovementEnumerators);
    }

    private float SampleTilt()
    {
        float tilt = RandomUtil.ApproxNormal("jibo-tilt1", TILT_SMALL_RANGE, TILT_NORMAL_RANGE);
        return tilt;
    }

    private void AnnulDeviations()
    {
        for (int i = 0; i < 2; ++i)
        {
            poseQuatParams[i] = poseQuatParams[i] * poseQuatDevs[i];
            poseQuatDevs[i] = Quaternion.identity;
        }
    }



    private IEnumerator Watch(Vector3 direction)
    {
        animationType = ANIMATION_TYPE_LOOKAT;
        AnnulDeviations();
        return CoroutineUtils.RunUntilAllStop(new IEnumerator[] {
            TransitionLook(direction, direction, LOOKAT_DURATION, LOOKAT_GAZE_DURATION),
            ControlAutoMotions(startDelay: -1f)
        });
    }

    private IEnumerator Watch(Vector2 target)
    {
        animationType = ANIMATION_TYPE_LOOKAT;
        AnnulDeviations();
        Vector3 targetFaceDirection = GetFaceDirection(target);
        Vector3 targetGazeDirection = GetGazeDirection(target);
        return CoroutineUtils.RunUntilAllStop(new IEnumerator[] {
            TransitionLook(targetFaceDirection, targetGazeDirection, LOOKAT_DURATION, LOOKAT_GAZE_DURATION),
            ControlAutoMotions(startDelay: 0)
        });
    }

    private IEnumerator Watch(GameObject target)
    {
        animationType = ANIMATION_TYPE_LOOKAT;
        AnnulDeviations();
        yield return CoroutineUtils.RunUntilAnyStop(new IEnumerator[] {
            ChaseQuatParam(POSE_PARAM_FACEDIR, () => GetFaceDirection(target), LOOKAT_DURATION, Easing.EaseInOut),
            ChaseQuatParam(POSE_PARAM_GAZEDIR, () => GetGazeDirection(target), LOOKAT_GAZE_DURATION, Easing.EaseInOut),
            ControlAutoMotions(startDelay: LOOKAT_DURATION)
        });
    }

    private IEnumerator TransitionLook(Vector3 targetFaceDirection, Vector3 targetGazeDirection, float duration, float gazeDuration)
    {
        return CoroutineUtils.RunUntilAllStop(new IEnumerator[] {
            TransitionQuatParam(poseQuatParams, POSE_PARAM_FACEDIR, targetFaceDirection, duration, Easing.EaseInOut),
            TransitionQuatParam(poseQuatParams, POSE_PARAM_GAZEDIR, targetGazeDirection, gazeDuration, Easing.EaseInOut)
        });
    }

    private IEnumerator TransitionQuatParam(Quaternion[] paramDict, int param, Vector3 finDir, float duration, Func<float, float> easing)
    {
        Quaternion iniQuat = paramDict[param];
        Quaternion finQuat = Quaternion.LookRotation(finDir);
        double t0 = TimeKeeper.time;
        double tEnd = TimeKeeper.time + duration;
        while (true)
        {
            yield return null;
            float t = (float)(TimeKeeper.time - t0) / duration;
            paramDict[param] = Quaternion.Slerp(iniQuat, finQuat, easing(t));
            if (TimeKeeper.time > tEnd) yield break;
        }
    }

    private IEnumerator ChaseQuatParam(int param, Func<Vector3> targetDirFn, float duration, Func<float, float> easing)
    {
        double t0 = TimeKeeper.time;
        float param_t_current = 0;
        double tEnd = TimeKeeper.time + duration;
        while (true)
        {
            yield return null;
            Vector3 targetDir = targetDirFn();
            if (targetDir == Vector3.zero) yield break;
            Quaternion quatTarget = Quaternion.LookRotation(targetDir);
            if (TimeKeeper.time < tEnd)
            {
                float t = (float)(TimeKeeper.time - t0) / duration;
                float param_t_prev = param_t_current;
                param_t_current = easing(t);
                Quaternion quatStart = poseQuatParams[param];
                poseQuatParams[param] = Quaternion.Slerp(quatStart, quatTarget, (param_t_current - param_t_prev) / (1 - param_t_prev));
            }
            else
            {
                poseQuatParams[param] = quatTarget;
            }
        }
    }

    private IEnumerator TransitionScalarParam(int param, float finVal, float duration, Func<float, float> easing)
    {
        float iniVal = poseScalarParams[param];
        double t0 = TimeKeeper.time;
        double tEnd = TimeKeeper.time + duration;
        while (true)
        {
            yield return null;
            float t = (float)(TimeKeeper.time - t0) / duration;
            poseScalarParams[param] = Mathf.Lerp(iniVal, finVal, easing(t));
            //if (param == POSE_PARAM_TILT) { Debug.Log($"TILT PARAM: {poseScalarParams[param]} T: {t} INI-VAL: {iniVal} FIN-VAL: {finVal}"); };
            if (TimeKeeper.time > tEnd) yield break;
        }
    }

    private IEnumerator BlinkingCoroutine()
    {
        blinkingHasBeenDisrupted = false;
        if (jiboEye.transform.localScale.y < 0.99f)
        {
            yield return WidenEye();
        }
        double nextEyeBlinkTime = SampleEyeBlinkTime();
        while (true)
        {
            yield return null;
            if (TimeKeeper.time < nextEyeBlinkTime) continue;
            yield return AnimateBlink();
            nextEyeBlinkTime = SampleEyeBlinkTime();
        }
    }

    private double SampleEyeBlinkTime()
    {
        return TimeKeeper.time + RandomUtil.SampleExponential("jibo-t-blink1", BLINK_MEAN_PERIOD);
    }

    private IEnumerator AnimateBlink()
    {
        return AnimateSquish(jiboEye.gameObject, BLINK_DURATION, 0.2f, Easing.EaseThereAndBackAgain);
    }

    private IEnumerator NarrowEye()
    {
        blinkingHasBeenDisrupted = true;
        return AnimateSquish(jiboEye.gameObject, EYE_NARROW_DURATION, 0.2f, Easing.EaseInOut);
    }

    private IEnumerator WidenEye()
    {
        return AnimateSquish(jiboEye.gameObject, EYE_WIDEN_DURATION, 1, Easing.EaseInOut);
    }

    private void SmileEye()
    {
        animaster.StartScale(jiboEyelid.gameObject, Vector3.one, EYE_SMILE_DURATION, Easing.EaseInOut);
    }

    private void UnsmileEye()
    {
        animaster.StartScale(jiboEyelid.gameObject, new Vector3(1, 0, 1), EYE_UNSMILE_DURATION, Easing.EaseInOut);
    }

    private IEnumerator AnimateSquish(GameObject gObject, float duration, float target_scale, Func<float, float> easing)
    {
        Scale scale = animaster.StartScale(gObject, new Vector2(gObject.transform.localScale.x, target_scale), duration, easing);
        while (scale.IsGoing()) { yield return null; }
    }

    private IEnumerator AdjustLocation(Vector2 target, float targetScale)
    {
        double t0 = TimeKeeper.time;
        Vector2 initialLocation = jiboTform.position;
        float z = jiboTform.position.z;
        float initialScale = jiboTform.localScale.x;
        while (TimeKeeper.time - t0 < ADJUSTMENT_DURATION)
        {
            yield return null;
            float t = (float)(TimeKeeper.time - t0) / ADJUSTMENT_DURATION;
            Vector2 lerped = Vector2.Lerp(initialLocation, target, t);
            jiboTform.position = new Vector3(lerped.x, lerped.y, z);
            float scale = Mathf.Lerp(initialScale, targetScale, t);
            jiboTform.localScale = new Vector3(scale, scale, 1);
        }
    }

    private IEnumerator Jump(Vector2 target, float targetScale)
    {
        animationType = ANIMATION_TYPE_CHANGE_LOCATION;
        AnnulDeviations();
        yield return PreJumpSquish(target);
        yield return Launch();
        yield return Fly(target, targetScale, IN_THE_AIR_DURATION, JUMP_HEIGHT);
        yield return Land();
        yield return PostJumpUnsquish();
        RestoreAfterExpression();
    }

    private IEnumerator PreJumpSquish(Vector2 target)
    {
        return CoroutineUtils.RunUntilAllStop(new IEnumerator[] {
            TransitionQuatParam(poseQuatParams, POSE_PARAM_FACEDIR, GetFaceDirection(target), LOOKAT_GAZE_DURATION, Easing.EaseInOut),
            TransitionQuatParam(poseQuatParams, POSE_PARAM_GAZEDIR, GetGazeDirection(target), LOOKAT_GAZE_DURATION, Easing.EaseInOut),
            TransitionScalarParam(POSE_PARAM_TILT, 0, LOOKAT_GAZE_DURATION, Easing.EaseInOut),
            TransitionScalarParam(POSE_PARAM_SQUISH, MAX_SQUISH, LOOKAT_GAZE_DURATION, Easing.EaseInOut)
        });
    }

    private IEnumerator PostJumpUnsquish()
    {
        return CoroutineUtils.RunUntilAllStop(new IEnumerator[]
        {
            TransitionQuatParam(poseQuatParams, POSE_PARAM_FACEDIR, -Vector3.forward, LOOKAT_GAZE_DURATION, Easing.EaseInOut),
            TransitionQuatParam(poseQuatParams, POSE_PARAM_GAZEDIR, -Vector3.forward, LOOKAT_GAZE_DURATION, Easing.EaseInOut),
            TransitionScalarParam(POSE_PARAM_SQUISH, 1, LOOKAT_GAZE_DURATION, Easing.EaseInOut)
        });
    }

    private IEnumerator Launch()
    {
        return TransitionScalarParam(POSE_PARAM_SQUISH, 1 / MAX_SQUISH, LAUNCH_LAND_DURATION, Easing.EaseIn);
    }

    private IEnumerator Land()
    {
        return TransitionScalarParam(POSE_PARAM_SQUISH, 1, LAUNCH_LAND_DURATION, Easing.EaseOut);
    }

    private IEnumerator Fly(Vector2 target, float targetScale, float duration, float height)
    {
        Vector3 originalPosition = jiboTform.position;
        double t0 = TimeKeeper.time;
        float originalScale = jiboTform.localScale.x;
        while (TimeKeeper.time - t0 < duration && !locationAdjustor.IsRunning())
        {
            yield return null;
            float t = (float)(TimeKeeper.time - t0) / duration;
            Vector2 midPoint = Vector2.Lerp(originalPosition, target, t);
            jiboTform.position = new Vector3(midPoint.x, midPoint.y + height * Easing.EaseHop(t), originalPosition.z);
            float scale = Mathf.Lerp(originalScale, targetScale, t);
            jiboTform.localScale = new Vector3(scale, scale, 1);
        }
    }

    private IEnumerator AnimateExcitedJump()
    {
        animationType = ANIMATION_TYPE_EXPRESSION;

        // 1. Store original position
        Vector3 originalPos = jiboTform.position;

        // 2. Reset any pose deviations
        AnnulDeviations();

        // 3. Smile eyes
        SmileEye();

        // 4. Squish down
        yield return TransitionScalarParam(POSE_PARAM_SQUISH, MAX_SQUISH, LOOKAT_GAZE_DURATION, Easing.EaseInOut);

        // 5. Launch up (relative to original position)
        yield return Launch();

        // 6. Fly in an arc, anchored to original position
        yield return Fly(originalPos, jiboTform.localScale.x, IN_THE_AIR_DURATION / Mathf.Sqrt(3), JUMP_HEIGHT / 3);

        // 7. Land at original position
        yield return Land();

        // 8. Unsquish
        yield return TransitionScalarParam(POSE_PARAM_SQUISH, 1, LOOKAT_GAZE_DURATION, Easing.EaseInOut);

        // 9. Restore default expression/pose
        RestoreAfterExpression();
    }
    private void AssignFaceDir(Vector3 direction)
    {
        Quaternion dirQuat = Quaternion.LookRotation(direction);
        poseQuatParams[POSE_PARAM_FACEDIR] = dirQuat;
    }

    private void AssignFaceDirAndGazeDir(Vector3 direction)
    {
        Quaternion dirQuat = Quaternion.LookRotation(direction);
        poseQuatParams[POSE_PARAM_FACEDIR] = dirQuat;
        poseQuatParams[POSE_PARAM_GAZEDIR] = dirQuat;
    }

    private IEnumerator AnimateWiggle(int wiggleCycles, float wiggleDuration)
    {
        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();
        float DEFLECTION_R = Mathf.Deg2Rad * 15;
        Vector3 direction_0 = poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward;
        float azimuth_0 = Azimuth(direction_0);
        Vector3 direction = DirFromAzimuthAndElevation(azimuth_0, 0);
        SmileEye();
        yield return TransitionLook(targetFaceDirection: direction, targetGazeDirection: direction, duration: 0.3f, gazeDuration: LOOKAT_GAZE_DURATION);
        double t0 = TimeKeeper.time;
        while (TimeKeeper.time - t0 < wiggleDuration)
        {
            yield return null;
            float t = 2 * Mathf.PI * wiggleCycles * Easing.EaseInOut((float)(TimeKeeper.time - t0) / wiggleDuration);
            float azimuth = azimuth_0 - DEFLECTION_R * Mathf.Sin(t);
            AssignFaceDirAndGazeDir(DirFromAzimuthAndElevation(azimuth, 0));
            poseScalarParams[POSE_PARAM_TILT] = TILT_NORMAL_RANGE * Mathf.Sin(t);
        }
        UnsmileEye();
        RestoreAfterExpression();
    }

    private IEnumerator AnimateSpin(float spinDuration)
    {
        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();
        Vector3 direction_0 = poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward;
        float elevation_0 = Elevation(direction_0);
        float azimuth_0 = Azimuth(direction_0);
        double t0 = TimeKeeper.time;
        SmileEye();
        while (TimeKeeper.time - t0 < spinDuration)
        {
            yield return null;
            float t = 2 * Mathf.PI * Easing.EaseInSteadyOut((float)(TimeKeeper.time - t0) / spinDuration, steadyW: 0.75f);
            float azimuth = azimuth_0 + t;
            float elevation = elevation_0 * Mathf.Cos(t);
            AssignFaceDirAndGazeDir(DirFromAzimuthAndElevation(azimuth, elevation));
        }
        UnsmileEye();
        RestoreAfterExpression();

    }


    private IEnumerator AnimateHappySpinJump(float spinDuration = 1.5f, float jumpHeight = 0.5f, int spinCycles = 2)
    {
        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();

        SmileEye();

        Vector3 startPos = jiboTform.position;
        Quaternion startRot = jiboTform.rotation;

        float azimuth_0 = Azimuth(poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward);
        float elevation_0 = Elevation(poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward);

        // Spin over time
        double t0 = TimeKeeper.time;
        while (TimeKeeper.time - t0 < spinDuration)
        {
            yield return null;
            float tSpin = 2 * Mathf.PI * spinCycles * Easing.EaseInSteadyOut(
                (float)(TimeKeeper.time - t0) / spinDuration, 0.75f);
            float azimuth = azimuth_0 + tSpin;
            float elevation = elevation_0 * Mathf.Cos(tSpin);
            AssignFaceDirAndGazeDir(DirFromAzimuthAndElevation(azimuth, elevation));
        }

        // Jump
        Vector2 targetPos = new Vector2(startPos.x, startPos.y);
        yield return PreJumpSquish(targetPos);
        yield return Launch();
        yield return Fly(targetPos, jiboTform.localScale.x, IN_THE_AIR_DURATION * 1.5f, jumpHeight);
        yield return Land();
        yield return PostJumpUnsquish();

        UnsmileEye();
        RestoreAfterExpression();
        jiboTform.position = startPos;
        jiboTform.rotation = startRot;
    }

    private IEnumerator AnimateHappyWiggleJump(float wiggleDuration = 1.5f, int wiggleCycles = 2, float jumpHeight = 0.2f)
    {
        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();
        SmileEye();

        Vector3 startPos = jiboTform.position;
        Quaternion startRot = jiboTform.rotation;
        float azimuth_0 = Azimuth(poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward);
        float DEFLECTION_R = Mathf.Deg2Rad * 15;

        // Wiggle Motion
        double t0 = TimeKeeper.time;
        while (TimeKeeper.time - t0 < wiggleDuration)
        {
            yield return null;
            float tWiggle = 2 * Mathf.PI * wiggleCycles * Easing.EaseInOut(
                (float)(TimeKeeper.time - t0) / wiggleDuration);
            float wiggleAzimuth = azimuth_0 + DEFLECTION_R * Mathf.Sin(tWiggle);
            AssignFaceDirAndGazeDir(DirFromAzimuthAndElevation(wiggleAzimuth, 0));
            poseScalarParams[POSE_PARAM_TILT] = TILT_NORMAL_RANGE * Mathf.Sin(tWiggle);
        }

        // Jump
        Vector2 targetPos = new Vector2(startPos.x, startPos.y);
        yield return PreJumpSquish(targetPos);
        yield return Launch();
        yield return Fly(targetPos, jiboTform.localScale.x, IN_THE_AIR_DURATION * 1.5f, jumpHeight);
        yield return Land();
        yield return PostJumpUnsquish();

        UnsmileEye();
        RestoreAfterExpression();
        jiboTform.position = startPos;
        jiboTform.rotation = startRot;
    }


    private IEnumerator AnimateSadness()
    {
        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();
        float DEFLECTION_R = Mathf.Deg2Rad * 10;
        Vector3 direction_0 = poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward;
        float azimuth_0 = Azimuth(direction_0);
        float face_elevation_0 = Mathf.Deg2Rad * -20;
        Vector3 faceDirection_0 = DirFromAzimuthAndElevation(azimuth_0, face_elevation_0);
        Vector3 gazeDirection_0 = DirFromAzimuthAndElevation(azimuth_0, Mathf.Deg2Rad * -60);
        yield return TransitionLook(targetFaceDirection: faceDirection_0, targetGazeDirection: gazeDirection_0, duration: 1f, gazeDuration: 1f);
        double t0 = TimeKeeper.time;
        float sadnessDuration = 1f;
        while (TimeKeeper.time - t0 < sadnessDuration)
        {
            yield return null;
            float t = 2 * Mathf.PI * Easing.EaseInOut((float)(TimeKeeper.time - t0) / sadnessDuration);
            float azimuth = azimuth_0 - DEFLECTION_R * Mathf.Sin(t);
            AssignFaceDir(DirFromAzimuthAndElevation(azimuth, face_elevation_0));
        }
        RestoreAfterExpression();
    }

    private IEnumerator AnimateHeadTilt()
    {
        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();
        Vector3 faceDirection_0 = poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward;
        Vector3 gazeDirection_0 = poseQuatParams[POSE_PARAM_GAZEDIR] * Vector3.forward;
        float azimuth_0 = Azimuth(faceDirection_0);
        float elevation_0 = Elevation(gazeDirection_0);
        float tilt_0 = poseScalarParams[POSE_PARAM_TILT];
        float targetTilt = (faceDirection_0.x < 0) ? TILT_NORMAL_RANGE : -TILT_NORMAL_RANGE;
        List<IEnumerator> inAnimations = new List<IEnumerator>() { TransitionScalarParam(POSE_PARAM_TILT, targetTilt, HEAD_TILT_DURATION, Easing.EaseInOut) };
        if (elevation_0 > 0)
        {
            Vector3 direction1 = DirFromAzimuthAndElevation(azimuth_0, 0);
            inAnimations.Add(TransitionLook(direction1, direction1, HEAD_TILT_DURATION, HEAD_TILT_DURATION));
        }
        eyeController.SetCoroutine(NarrowEye());
        yield return CoroutineUtils.RunUntilAllStop(inAnimations);
        yield return CoroutineUtils.WaitCoroutine(2f);
        List<IEnumerator> outAnimations = new List<IEnumerator>() { TransitionScalarParam(POSE_PARAM_TILT, tilt_0, HEAD_TILT_DURATION, Easing.EaseInOut) };
        if (elevation_0 > 0) { outAnimations.Add(TransitionLook(faceDirection_0, gazeDirection_0, HEAD_TILT_DURATION, HEAD_TILT_DURATION)); }
        eyeController.SetCoroutine(WidenEye());
        yield return CoroutineUtils.RunUntilAllStop(outAnimations);
        RestoreAfterExpression();
    }

    private void RestoreAfterExpression()
    {
        if (blinkingHasBeenDisrupted) { eyeController.SetCoroutine(BlinkingCoroutine()); }
        if (jiboEyelid.transform.localScale.y > 0) { UnsmileEye(); }
        if (lookingAtTablet)
        {
            _LookAtTablet();
        }
        else
        {
            _LookAtChild();
        }
    }

    private float Azimuth(Vector3 direction)
    {
        return Mathf.Atan2(direction.z, direction.x);
    }

    private float Elevation(Vector3 direction)
    {
        float x = direction.x;
        float z = direction.z;
        return Mathf.Atan2(direction.y, Mathf.Sqrt(x * x + z * z));
    }

    private Vector3 DirFromAzimuthAndElevation(float azimuth, float elevation)
    {
        Vector3 azimuthalDirection = new Vector3(Mathf.Cos(azimuth), 0, Mathf.Sin(azimuth));
        return Mathf.Sin(elevation) * Vector3.up + Mathf.Cos(elevation) * azimuthalDirection;
    }

    private IEnumerator SpeechRingExpansionCoroutine(GameObject speechRing)
    {
        float initialScale = speechRing.transform.localScale.x;
        float targetScale = initialScale * SPEECH_RING_EXPANSION_FACTOR;
        double t0 = TimeKeeper.time;
        AlignSpeechRingWithHead(speechRing);
        float opacity = 1;
        float synth_departure_opacity = -1;
        double synth_departure_time = -1;
        while (TimeKeeper.time - t0 < SPEECH_RING_EXPANSION_PERIOD && opacity > 0)
        {
            yield return null;
            AlignSpeechRingWithHead(speechRing);
            float t = (float)(TimeKeeper.time - t0) / SPEECH_RING_EXPANSION_PERIOD;
            float scale = Mathf.Lerp(initialScale, targetScale, t);
            speechRing.transform.localScale = new Vector3(scale, scale, 1);
            if (synth_departure_opacity < 0)
            {
                opacity = Mathf.Max(0, 1 - t);
                if (!ThereIsSpeech())
                {
                    synth_departure_opacity = opacity;
                    synth_departure_time = TimeKeeper.time;
                }
            }
            else
            {
                float decay_t = (float)(TimeKeeper.time - synth_departure_time) / SPEECH_RING_DECAY_PERIOD;
                opacity = Mathf.Lerp(synth_departure_opacity, 0, decay_t);
            }
            Opacity.SetOpacity(speechRing, opacity);
        }
        Destroy(speechRing);
    }

    private void AlignSpeechRingWithHead(GameObject speechRing)
    {
        Vector2 headPos = jiboFaceAssembly.position;
        speechRing.transform.position = new Vector3(headPos.x, headPos.y, -5);
    }



    private IEnumerator AnimateWiggleSlideToWordBoxEdge(int wiggleCycles, float wiggleDuration)
    {
        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();
        SmileEye();

        // Ensure we have a reference to the bottom WordBox
        if (bottomWordBox == null)
        {
            bottomWordBox = GameObject.FindObjectOfType<WordBox>();
            if (bottomWordBox == null)
            {
                Debug.LogWarning("No WordBox found for Jibo wiggle-slide animation!");
                yield break;
            }
        }

        // Compute target slide distance using WordBox bounds
        Bounds boxBounds = bottomWordBox.GetMyBounds();
        Vector3 startPos = jiboTform.position;
        float slideDistance = boxBounds.max.x - startPos.x; // move toward the right edge

        slideDistance -= 0.2f;

        float DEFLECTION_R = Mathf.Deg2Rad * 15;
        Vector3 direction_0 = poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward;
        float azimuth_0 = Azimuth(direction_0);
        Vector3 direction = DirFromAzimuthAndElevation(azimuth_0, 0);
        yield return TransitionLook(targetFaceDirection: direction, targetGazeDirection: direction, duration: 0.3f, gazeDuration: LOOKAT_GAZE_DURATION);

        Vector3 endPos = startPos + new Vector3(slideDistance, 0, 0);

        double t0 = TimeKeeper.time;
        while (TimeKeeper.time - t0 < wiggleDuration)
        {
            yield return null;
            float normalizedT = (float)((TimeKeeper.time - t0) / wiggleDuration);
            float easedT = Easing.EaseInOut(normalizedT);

            // Wiggle rotation
            float t = 2 * Mathf.PI * wiggleCycles * easedT;
            float azimuth = azimuth_0 - DEFLECTION_R * Mathf.Sin(t);
            AssignFaceDirAndGazeDir(DirFromAzimuthAndElevation(azimuth, 0));
            poseScalarParams[POSE_PARAM_TILT] = TILT_NORMAL_RANGE * Mathf.Sin(t);

            // Slide motion toward the right edge
            jiboTform.position = Vector3.Lerp(startPos, endPos, easedT);
        }

        UnsmileEye();
        RestoreAfterExpression();
    }

    private IEnumerator JumpToLastPlacedBlock()
    {
        if (bottomWordBox == null)
        {
            bottomWordBox = GameObject.FindObjectOfType<WordBox>();
            if (bottomWordBox == null)
            {
                Debug.LogWarning("JumpToLastPlacedBlock: No WordBox found!");
                yield break;
            }
        }

        List<Block> placedBlocks = bottomWordBox.GetBlocks();
        if (placedBlocks.Count == 0)
        {
            Debug.LogWarning("JumpToLastPlacedBlock: No placed blocks to jump to!");
            yield break;
        }

        // Capture original global position BEFORE any jumps
        Vector3 originalPos = jiboTform.position;

        Block lastBlock = placedBlocks.Last();

        // Compute jump target slightly right and above last block
        float cellHeight = Block.GetStandardHeight();
        Vector3 lastBlockWorldPos = bottomWordBox.transform.position + lastBlock.transform.localPosition;

        Vector3 targetPos = lastBlockWorldPos + new Vector3(0, 0.75f * cellHeight, 0);
      

        // Pre-jump squish
        yield return TransitionScalarParam(POSE_PARAM_SQUISH, MAX_SQUISH, LOOKAT_GAZE_DURATION, Easing.EaseInOut);

        // Launch and jump
        yield return Launch();
        yield return Fly(targetPos, jiboTform.localScale.x, IN_THE_AIR_DURATION, JUMP_HEIGHT);

        // Land and recover
        yield return Land();
        yield return TransitionScalarParam(POSE_PARAM_SQUISH, 1, LOOKAT_GAZE_DURATION, Easing.EaseInOut);

        // Randomly pick a happy animation
        if (UnityEngine.Random.value < 0.5f)
        {
            yield return AnimateHappySpinJump();
        }
        else
        {
            yield return AnimateHappyWiggleJump();
        }

        RestoreAfterExpression();
        
    }

    private IEnumerator JumpToWordBoxEnd()
    {
        // Capture original global position before any jumps
        Vector3 originalPos = jiboTform.position;

        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();
        SmileEye();

        // --- Pre-jump squish ---
        yield return TransitionScalarParam(POSE_PARAM_SQUISH, MAX_SQUISH, LOOKAT_GAZE_DURATION, Easing.EaseInOut);

        // --- Launch first jump ---
        yield return Launch();

        // --- Jump to end-of-word-box ---
        Vector3 firstTarget;
        GameObject lastActiveCell = bottomWordBox.GetLastActiveCell();
        if (lastActiveCell != null)
        {
            float cellWidth = Block.GetStandardHeight();
            float cellHeight = Block.GetStandardHeight();
            firstTarget = lastActiveCell.transform.position + new Vector3(cellWidth, -0.25f * cellHeight, 0);
        }
        else
        {
            firstTarget = bottomWordBox.transform.position;
        }

        yield return Fly(firstTarget, jiboTform.localScale.x, IN_THE_AIR_DURATION, JUMP_HEIGHT);
        yield return Land();
        yield return TransitionScalarParam(POSE_PARAM_SQUISH, 1, LOOKAT_GAZE_DURATION, Easing.EaseInOut);

        // Short pause
        yield return CoroutineUtils.WaitCoroutine(0.3f);

        // Wiggle Slide
        {
            int wiggleCycles = 2;
            float wiggleDuration = 1.5f;

            Vector3 startPos = jiboTform.position;
            Vector3 startLoginPos = positionByLocation["login"];
            float maxX = startLoginPos.x + 3 * Block.GetStandardHeight(); // clamp to 3 blocks from login

            float slideDistance = 4 * Block.GetStandardHeight(); // desired slide distance
            Vector3 desiredEndPos = startPos + new Vector3(slideDistance, 0, 0);

            // Clamp end position so we never go past maxX
            Vector3 endPos = new Vector3(Mathf.Min(desiredEndPos.x, maxX), startPos.y, startPos.z);

            float DEFLECTION_R = Mathf.Deg2Rad * 15;
            Vector3 direction_0 = poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward;
            float azimuth_0 = Azimuth(direction_0);
            Vector3 direction = DirFromAzimuthAndElevation(azimuth_0, 0);

            yield return TransitionLook(targetFaceDirection: direction, targetGazeDirection: direction, duration: 0.3f, gazeDuration: LOOKAT_GAZE_DURATION);

            double t0 = TimeKeeper.time;
            while (TimeKeeper.time - t0 < wiggleDuration)
            {
                yield return null;
                float normalizedT = (float)((TimeKeeper.time - t0) / wiggleDuration);
                float easedT = Easing.EaseInOut(normalizedT);

                // Wiggle rotation
                float t = 2 * Mathf.PI * wiggleCycles * easedT;
                float azimuth = azimuth_0 - DEFLECTION_R * Mathf.Sin(t);
                AssignFaceDirAndGazeDir(DirFromAzimuthAndElevation(azimuth, 0));
                poseScalarParams[POSE_PARAM_TILT] = TILT_NORMAL_RANGE * Mathf.Sin(t);

                // Slide motion
                Vector3 lerpedPos = Vector3.Lerp(startPos, endPos, easedT);
                lerpedPos.x = Mathf.Min(lerpedPos.x, maxX); // ensure we never go past maxX
                jiboTform.position = lerpedPos;
            }

            UnsmileEye();
        }

        // Short pause after wiggle-slide
        yield return CoroutineUtils.WaitCoroutine(0.3f);

        // --- Launch second jump (down to bottom) ---
        yield return Launch();

        Bounds wordBoxBounds = bottomWordBox.GetMyBounds();
        Vector3 startPosLogin = positionByLocation["login"];
        Vector3 targetPos = new Vector3(
            startPosLogin.x + 3.95f * Block.GetStandardHeight(), // start X plus 4 blocks
            wordBoxBounds.max.y + 0.2f * Block.GetStandardHeight(),                  // bottom of WordBox
            startPosLogin.z                                  // keep depth
        );

        yield return Fly(targetPos, jiboTform.localScale.x, IN_THE_AIR_DURATION, JUMP_HEIGHT);
        yield return Land();

        // Short pause after landing
        yield return CoroutineUtils.WaitCoroutine(0.6f);


        // --- Final wiggle after second jump ---
        yield return AnimateWiggle(wiggleCycles: 2, wiggleDuration: 1.2f);

        RestoreAfterExpression();
       


    }


    public void TriggerJumpToWordBoxEnd()
    {
        if (animator == null)
        {
            Debug.LogWarning("TriggerJumpToWordBoxEnd: animator is null.");
            return;
        }
        animator.SetCoroutine(JumpToWordBoxEnd());
    }


    public void JumpToTarget(Vector3 targetPosition)
    {
        // Optionally clamp Z if your scene is 2D
        Vector2 flatTarget = new Vector2(targetPosition.x, targetPosition.y);

        // You can tweak the scale to match your animation’s sense of depth
        float targetScale = 1.0f;

        // Start the jump coroutine
        StartCoroutine(Jump(flatTarget, targetScale));
    }


    public IEnumerator AnimateHappyWiggleJumpToTarget(Vector3 targetPos, int wiggleCycles = 2, float jumpHeight = 0.2f, float wiggleDuration = 1.5f)
    {
        animationType = ANIMATION_TYPE_EXPRESSION;
        AnnulDeviations();
        SmileEye();

        Vector3 startPos = jiboTform.position;
        Quaternion startRot = jiboTform.rotation;

        float azimuth_0 = Azimuth(poseQuatParams[POSE_PARAM_FACEDIR] * Vector3.forward);
        float DEFLECTION_R = Mathf.Deg2Rad * 15;

        // Jump sequence
        yield return PreJumpSquish(targetPos);
        yield return Launch();
        yield return Fly(targetPos, jiboTform.localScale.x, IN_THE_AIR_DURATION * 1.5f, jumpHeight);
        yield return Land();
        yield return PostJumpUnsquish();

        // Wiggle Motion before jump
        double t0 = TimeKeeper.time;
        while (TimeKeeper.time - t0 < wiggleDuration)
        {
            yield return null;
            float tWiggle = 2 * Mathf.PI * wiggleCycles * Easing.EaseInOut(
                (float)(TimeKeeper.time - t0) / wiggleDuration);
            float wiggleAzimuth = azimuth_0 + DEFLECTION_R * Mathf.Sin(tWiggle);
            AssignFaceDirAndGazeDir(DirFromAzimuthAndElevation(wiggleAzimuth, 0));
            poseScalarParams[POSE_PARAM_TILT] = TILT_NORMAL_RANGE * Mathf.Sin(tWiggle);
        }

        UnsmileEye();
        RestoreAfterExpression();

        // Place Jibo exactly on the target
        jiboTform.position = targetPos;
        jiboTform.rotation = startRot;
    }
}



