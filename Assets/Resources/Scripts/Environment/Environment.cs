using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using SimpleJSON;
using System.Linq;

public class Environment : MonoBehaviour
{
    private string rosip = null;
    private List<User> users = new List<User>();
    private User user = null;
    private string group = null;
    // the avatars and scenes are stored as strings rather than JSONNodes to make sure that they are not modified outside of this class
    private Dictionary<string, string> shared_avatars = new Dictionary<string, string>();
    private Dictionary<string, string> private_avatars = new Dictionary<string, string>();
    private Dictionary<string, string> scenes = new Dictionary<string, string>();
    private Dictionary<string, string> scene_versions = new Dictionary<string, string>();
    private Dictionary<string, string> shared_avatar_versions = new Dictionary<string, string>();
    private Dictionary<string, string> private_avatar_versions = new Dictionary<string, string>();
    private IRoboPartner roboPartner = null;
    private MachineDriver machineDriver = null;
    private string type;
    private bool isLoggingEnabled = false;
    private bool isVideoEnabled = false;
    private bool recordingDecisionMade = false;
    private bool webcamEnabled = false;
    private DateTime loginTime;
    private string sessionStartMark = null;
    private double nextActivityMarkTime = -100;
    private bool markActivity = false;

    private Replayer replayer = null;
    private CoroutineRunner announcementRunner = new CoroutineRunner();
    private DatabaseKeeper databaseKeeper;

    private const float LOADING_TIMEOUT = 10f;
    private bool appHasFocus = true;

    private List<string> usedThemes = null;

    private GameObject mainIdeaButton = null;

    private const string DEFAULT_STATION_SETUP = "{group: \"default\", type: \"tablet\", log-enabled: false, video-enabled: false}";

    // Start is called before the first frame update
    void Start()
    {
        Application.logMessageReceived += OnLogMessage;
        machineDriver = GetComponent<MachineDriver>();
        databaseKeeper = GetComponent<DatabaseKeeper>();
        mainIdeaButton = GameObject.FindGameObjectsWithTag("IdeaButton").Where(btn => null == btn.transform.parent).First();
        StartCoroutine(ConfigureSpeechBlocks());
        MoveDroppedFiles();
    }

    void Update()
    {
        announcementRunner.Update();
        if (null != sessionStartMark && TimeKeeper.time > nextActivityMarkTime) { UpdateActivityMarker(); }
    }

    private void LateUpdate()
    {
        if (null == replayer)
        {
            TimeKeeper.UpdateGameTime();
        }
    }

    public string GetStationName()
    {
        string systemName = SystemInfo.deviceName;
        // Commenting the following out if your system's name is findable in the databse
        #if UNITY_EDITOR
            systemName = "Test";
        #endif
        return systemName;
    }

    public bool IsLoggingEnabled()
    {
        return isLoggingEnabled;
    }

    public bool IsVideoEnabled()
    {
        return isVideoEnabled;
    }

    public string GetROSIP()
    {
        return rosip;
    }

    public void PickUser(User user)
    {
        SetupForNewUser(user, DateTime.Now);
        MarkActivityStart();
        StartCoroutine(LoadUserConfiguration());
    }

    public void AnnounceInternetIssue(bool fatal)
    {
        if (announcementRunner.IsRunning()) return;
        string messageFile = fatal ? "Sounds/no-internet-fatal" : "Sounds/no-internet-non-fatal";
        announcementRunner.SetCoroutine(AnnounceIssueCoroutine(messageFile));
    }

    public bool IssueAnnouncementInProgress()
    {
        return announcementRunner.IsRunning();
    }

    public string GetGroup()
    {
        return group;
    }

    public User GetUser()
    {
        return user;
    }

    public DateTime GetLoginTime()
    {
        return loginTime;
    }

    public void MarkLessonCompletion(string lesson)
    {
        if (null == replayer)
        {
            string tutorialPath = $"groups/{group}/tutorial/{user.GetID()}";
            databaseKeeper.WriteIn(tutorialPath, lesson, true);
        }
    }

    public JSONNode GetAvatar(string nameSense)
    {
        string avatar = null;
        string fullname = Vocab.GetFullNameFromNameSense(nameSense);
        if (!private_avatars.TryGetValue(fullname, out avatar))
        {
            if (!shared_avatars.TryGetValue(fullname, out avatar)) return null;
        }
        return JSONNode.Parse(avatar);
    }

    public void UpdateAvatar(string nameSense, string avatar)
    {
        bool updateWordButtons = false;
        bool updateLoginButtons = false;
        RecordAvatar(nameSense, avatar, out updateWordButtons, out updateLoginButtons);
        List<GameObject> pictureObjsToUpdate = new List<GameObject>();
        GameObject pictureBlockObj = GameObject.FindWithTag("ResultBox").GetComponent<ResultBox>().GetSpawnedPictureBlock();
        if (null != pictureBlockObj) { pictureObjsToUpdate.Add(pictureBlockObj); }
        if (updateWordButtons) { pictureObjsToUpdate.AddRange(GameObject.FindGameObjectsWithTag("WordBankButton")); }
        if (updateLoginButtons) {
            LoginPage loginPage = GameObject.FindWithTag("LoginPage").GetComponent<LoginPage>();
            pictureObjsToUpdate.AddRange(loginPage.GetLoginButtons().Select(button => button.gameObject));
        }
        if (0 != pictureObjsToUpdate.Count)
        {
            JSONNode avatarJSON = JSONNode.Parse(avatar);
            foreach (GameObject pictureObj in pictureObjsToUpdate)
            {
                Picture buttonPicture = pictureObj.GetComponent<Picture>();
                if (nameSense == buttonPicture.GetImageWordSense())
                {
                    buttonPicture.UpdateAvatar(avatarJSON);
                }
            }
        }
    }

    public void RecordScene(string sceneID, string scene)
    {
        Debug.Log("RECORD SCENE");
        scenes[sceneID] = scene;
        string sceneVersion = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
        if (null == replayer)
        {
            databaseKeeper.Write($"groups/{group}/scenes/{user.GetID()}/{sceneID}/versions/{sceneVersion}", scene);
            databaseKeeper.WriteIn($"groups/{group}/scenes-catalogs/{user.GetID()}", $"{sceneID}/{sceneVersion}", true);
        }
        scene_versions[sceneID] = sceneVersion;
    }

    public JSONNode GetScene(string sceneID)
    {
        string scene;
        if (!scenes.TryGetValue(sceneID, out scene)) return null;
        return JSONNode.Parse(scene);
    }

    public string GetSceneVersion(string sceneID)
    {
        return DictUtil.GetOrDefault(scene_versions, sceneID);
    }

    public void DeleteScene(string sceneID)
    {
        scenes.Remove(sceneID);
        if (null == replayer)
        {
            databaseKeeper.WriteIn($"groups/{group}/scenes-catalogs/{user.GetID()}", $"{sceneID}/{scene_versions[sceneID]}", false);
        }
    }

    public string GetStationType()
    {
        return type;
    }

    public IRoboPartner GetRoboPartner()
    {
        return roboPartner;
    }

    public bool IsVideoApprovalNeeded()
    {
        Debug.Log($"VIDEO ENABLED: {isVideoEnabled}; VIDEO CONSENTED: {user.IsConsentedToVideo()}");
        return isVideoEnabled && user.IsConsentedToVideo();
    }

    public void ProceedWithRecording(bool webcamEnabled)
    {
        if (recordingDecisionMade || !isVideoEnabled) return;
        GameObject.FindWithTag("TitlePage").GetComponent<TitlePage>().DeactivateVideoButtons(instant: false);
        this.webcamEnabled = webcamEnabled;
        recordingDecisionMade = true;
        if (null == replayer) StartRecording();
    }

    public bool IsRecordingDecisionMade()
    {
        return recordingDecisionMade;
    }

    public void WrapDataCollection()
    {
        if (isLoggingEnabled) { Logging.FinalizeLog(); }
        Recorder recorder = GetComponent<Recorder>();
        if (null != recorder) { recorder.FinishRecording(); }
    }

    public void Logout()
    {
        MarkActivityEnd();
        scenes = new Dictionary<string, string>();
        GameObject.FindWithTag("LoginPage").GetComponent<LoginPage>().Deploy(deployInstantly: false);
        Gallery gallery = GameObject.FindWithTag("Gallery").GetComponent<Gallery>();
        AvatarPicker avatarPicker = GameObject.FindWithTag("AvatarPicker").GetComponent<AvatarPicker>();
        gallery.Reset();
        avatarPicker.Retract(retractInstantly: true);
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        stageObject.GetComponent<Tutorial>().Reset();
        stageObject.GetComponent<Scaffolder>().UnsetTarget();
        GameObject.FindWithTag("WordBox").GetComponent<WordBox>().InstantClear();
        GameObject.FindWithTag("ResultBox").GetComponent<ResultBox>().Clear();
        GameObject.FindWithTag("CompositionRoot").GetComponent<Composition>().Reset();
        WordDrawer wordDrawer = GameObject.FindWithTag("WordDrawer").GetComponent<WordDrawer>();
        wordDrawer.Retract(retractInstantly: true);
        wordDrawer.InvokeWordBank(instant: true);
        CategoryButton.DeactivateButtons();
        GameObject wordsArea = GameObject.FindWithTag("WordBankWordsArea");
        if (null != wordsArea) { wordsArea.GetComponent<WordsArea>().Clear(); }
        if (null != roboPartner) { Destroy((MonoBehaviour)roboPartner); }
        user = null;
    }

    public IEnumerator InitiateReplay(  string group,
                                        string user_id,
                                        bool videoEnabled,
                                        bool videoConsent,
                                        bool childDriven,
                                        bool expressive,
                                        List<string> completed_lessons,
                                        Dictionary<string, string> shared_avatar_versions,
                                        Dictionary<string, string> private_avatar_versions,
                                        Dictionary<string, string> scene_versions)
    {
        isLoggingEnabled = false;
        this.isVideoEnabled = videoEnabled;
        this.group = group;
        yield return LoadGroupConfiguration();
        User user = InitiateReplayUser(user_id, videoConsent, childDriven, expressive);
        SetupForNewUser(user, DateTime.Now);
        this.shared_avatar_versions = shared_avatar_versions;
        this.private_avatar_versions = private_avatar_versions;
        yield return InitiateReplayAvatars(GetSharedAvatarsPath(), shared_avatar_versions, shared_avatars);
        yield return InitiateReplayAvatars(GetPrivateAvatarsPath(), private_avatar_versions, private_avatars);
        yield return InitiateReplayScenes(user_id, scene_versions);
        GetComponent<Tutorial>().Setup(completed_lessons);
        OnFinishedLoadingUser();
    }

    private User InitiateReplayUser(string user_id,
                                    bool videoConsent,
                                    bool childDriven,
                                    bool expressive)
    {
        int userIndex = users.FindIndex(user => user.GetID() == user_id);
        User oldUser = users[userIndex];
        users[userIndex] = new User(oldUser.GetID(), oldUser.GetFullName(), videoConsent, childDriven, expressive);
        return users[userIndex];
    }

    private IEnumerator InitiateReplayAvatars(string avatarsPath, Dictionary<string, string> avatar_versions, Dictionary<string, string> avatars)
    {
        this.shared_avatar_versions = avatar_versions;
        foreach (string fullname in avatar_versions.Keys)
        {
            string avatarPath = $"{avatarsPath}/{fullname}/versions/{avatar_versions[fullname]}";
            CoroutineResult<string> databaseResult = new CoroutineResult<string>();
            yield return databaseKeeper.Load(avatarPath, databaseResult, isImmutable: true);
            if (null != databaseResult.GetResult()) { avatars[fullname] = databaseResult.GetResult(); }
        }
    }

    private IEnumerator InitiateReplayScenes(string user_id, Dictionary<string, string> scene_versions)
    {
        usedThemes = new List<string>();
        this.scene_versions = scene_versions;
        foreach (string scene_id in scene_versions.Keys)
        {
            string scene_version = scene_versions[scene_id];
            string scenePath = $"groups/{group}/scenes/{user_id}/{scene_id}/versions/{scene_version}";
            CoroutineResult<string> databaseResult = new CoroutineResult<string>();
            yield return databaseKeeper.Load(scenePath, databaseResult, isImmutable: true);
            if (null != databaseResult.GetResult()) { scenes[scene_id] = databaseResult.GetResult(); }
        }
        OnScenesLoaded();
    }
     
    private void RecordAvatar(string nameSense, string avatar, out bool updateWordButtons, out bool updateLoginButtons)
    {
        if ("guest.name" == nameSense)
        {
            updateWordButtons = false;
            updateLoginButtons = false;
        }
        else
        {
            bool isUserName = GetComponent<Vocab>().IsUserNameSense(nameSense);
            string fullname = Vocab.GetFullNameFromNameSense(nameSense);
            bool shouldBeShared = isUserName && nameSense == user.GetNameSense();
            updateWordButtons = true;
            updateLoginButtons = shouldBeShared;
            var avatars = shouldBeShared ? shared_avatars : private_avatars;
            var avatar_versions = shouldBeShared ? shared_avatar_versions : private_avatar_versions;
            avatars[fullname] = avatar;
            string avatarVersion = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            avatar_versions[fullname] = avatarVersion;
            if (null == replayer)
            {
                var avatars_path = shouldBeShared ? GetSharedAvatarsPath() : GetPrivateAvatarsPath();
                var catalog_path = shouldBeShared ? GetSharedAvatarsCatalogPath() : GetPrivateAvatarsCatalogPath();
                databaseKeeper.Write($"{avatars_path}/{fullname}/versions/{avatarVersion}", avatar);
                databaseKeeper.WriteIn(catalog_path, $"{fullname}/{avatarVersion}", true);
            }
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (null != user && null == replayer)
        {
            if (hasFocus)
            {
                if (appHasFocus) return;
                MarkActivityStart();
                appHasFocus = true;
                Logging.OnRegainedFocus();
                if (recordingDecisionMade && null == replayer) { StartRecording(); }
            }
            else
            {
                if (!appHasFocus) return;
                MarkActivityEnd();
                appHasFocus = false;
                Logging.OnLostFocus();
                GameObject.FindWithTag("CompositionRoot").GetComponent<Composition>().Push(makeSnapshot: false);
                Recorder recorder = GetComponent<Recorder>();
                if (null != recorder)
                {
                    recorder.FinishRecording();
                }
            }
        }
    }

    private void MoveDroppedFiles()
    {
        try
        {
            foreach (string filepath in Directory.GetFiles(Application.persistentDataPath))
            {
                if (filepath.StartsWith("recording-"))
                {
                    File.Delete(filepath);
                }
            }
            UploadManager uploadManager = GetComponent<UploadManager>();
            uploadManager.ScheduleDirectoryUpload(Path.Combine(Application.persistentDataPath, "Logs"), "Logs", ".txt", deleteWhenDone: true);
            uploadManager.ScheduleDirectoryUpload(Path.Combine(Application.persistentDataPath, "Audio"), "Audio", ".wav", deleteWhenDone: true);
        }
        catch { }
    }

    private void StartRecording()
    {
        if (recordingDecisionMade && webcamEnabled)
        {
            Recorder recorder = gameObject.AddComponent<Recorder>();
            recorder.Setup();
        }
    }

    private IEnumerator ConfigureSpeechBlocks()
    {
        Debug.Log("LOADING SPEECHBLOCKS CONFIGURATION");
        databaseKeeper.Init();
        yield return databaseKeeper.ConnectToFirebase();
        yield return LoadStationConfiguration();
        if (null == replayer)
        {
            yield return LoadGroupConfiguration();
        }
        else
        {
            yield return replayer.InitReplay();
        }
        Debug.Log("FINISHED LOADING SPEECHBLOCKS CONFIGURATION");
    }

    private IEnumerator LoadStationConfiguration()
    {
        string stationPath = $"stations/{GetStationName()}";
        CoroutineResult<string> databaseResult = new CoroutineResult<string>();
        yield return databaseKeeper.Load(stationPath, databaseResult, isImmutable: false);
        string stationRecord = databaseResult.GetResult();
        if (null == stationRecord) { stationRecord = DEFAULT_STATION_SETUP; }
        JSONNode stationData = JSONNode.Parse(stationRecord);
        Debug.Log("STATION: " + GetStationName());
        Debug.Log("STATION DATA: " + stationRecord);
        group = stationData["group"];
        type = stationData["type"];
        if ("jibo" == type) { rosip = stationData["ROSIP"]; }
        else { VirtualJiboPartner.ConfigureUIForVirtualJibo(); }
        isVideoEnabled = (bool)GetOptionalSetting(stationData, "video-enabled", true);
        isLoggingEnabled = (bool)GetOptionalSetting(stationData, "log-enabled", true);
        markActivity = (bool)GetOptionalSetting(stationData, "mark-activity", true);
        string replayPath = stationData["replay-path"];
        if (null != replayPath)
        {
            replayer = gameObject.AddComponent<Replayer>();
            replayer.Setup(replayPath);
        }
    }

    private IEnumerator LoadGroupConfiguration()
    {
        yield return LoadCustomVocab();
        yield return LoadGroupUsers();
        if (null == replayer) { yield return LoadSharedAvatars(); }
        OnFinishedLoadingGroup();
    }

    private IEnumerator LoadCustomVocab()
    {
        Vocab vocab = GetComponent<Vocab>();
        CoroutineResult<string> coroutineResult = new CoroutineResult<string>();
        yield return databaseKeeper.Load($"groups/{group}/shared/names", coroutineResult, isImmutable: false);
        if (null != coroutineResult.GetResult()) {
            string nameListString = coroutineResult.GetResult();
            nameListString = nameListString.Substring(1, nameListString.Length - 2); // remove quotes
            vocab.AddCustomNameSenses(nameListString.Split(',').Select(name => Vocab.GetNameSenseFromFullName(name)));
        }
        yield return databaseKeeper.Load($"groups/{group}/shared/minivocab", coroutineResult, isImmutable: false);
        Dictionary<string, string> minivocab = new Dictionary<string, string>();
        if (null != coroutineResult.GetResult())
        {
            JSONNode minivocabJSON = JSONNode.Parse(coroutineResult.GetResult());
            foreach (string word in minivocabJSON.Keys)
            {
                minivocab[word] = minivocabJSON[word];
            }
        }
        vocab.SetupMinivocab(minivocab);
    }

    private string GetSharedAvatarsCatalogPath() { return $"groups/{group}/shared/avatars-catalog"; }

    private string GetPrivateAvatarsCatalogPath() { return $"groups/{group}/private-avatars-catalogs/{user.GetID()}"; }

    private string GetSharedAvatarsPath() { return $"groups/{group}/shared/avatars"; }

    private string GetPrivateAvatarsPath() { return $"groups/{group}/private-avatars/{user.GetID()}"; }

    private IEnumerator LoadSharedAvatars()
    {
        return LoadAvatars(catalogPath: GetSharedAvatarsCatalogPath(),
                           avatarsPath: GetSharedAvatarsPath(),
                           avatars: shared_avatars,
                           avatar_versions: shared_avatar_versions);
    }

    private IEnumerator LoadPrivateAvatars()
    {
        return LoadAvatars(catalogPath: GetPrivateAvatarsCatalogPath(),
                           avatarsPath: GetPrivateAvatarsPath(),
                           avatars: private_avatars,
                           avatar_versions: private_avatar_versions);
    }

    private IEnumerator LoadAvatars(string catalogPath,
                                    string avatarsPath,
                                    Dictionary<string, string> avatars,
                                    Dictionary<string, string> avatar_versions)
    {
        CoroutineResult<string> databaseResult = new CoroutineResult<string>();
        yield return databaseKeeper.Load(catalogPath, databaseResult, isImmutable: false);
        if (null != databaseResult.GetResult())
        {
            JSONNode avatarCatalogRecord = JSONNode.Parse(databaseResult.GetResult());
            foreach (string name in avatarCatalogRecord.Keys)
            {
                JSONNode versionsJSON = avatarCatalogRecord[name];
                List<string> versions = new List<string>();
                foreach (string version in versionsJSON.Keys)
                {
                    versions.Add(version);
                }
                if (0 == versions.Count) continue;
                string maxVersion = versions.Max();
                yield return databaseKeeper.Load($"{avatarsPath}/{name}/versions/{maxVersion}", databaseResult, isImmutable: true);
                if (null != databaseResult.GetResult())
                {
                    avatar_versions[name] = maxVersion;
                    avatars[name] = databaseResult.GetResult();
                }
            }
        }
    }

    private IEnumerator LoadGroupUsers()
    {
        CoroutineResult<string> databaseResult = new CoroutineResult<string>();
        yield return databaseKeeper.Load($"groups/{group}/users", databaseResult, isImmutable: false);
        if (null == databaseResult.GetResult()) yield break;
        Vocab vocab = GetComponent<Vocab>();
        JSONNode usersJSON = JSONNode.Parse(databaseResult.GetResult());
        foreach (string id in usersJSON.Keys)
        {
            JSONNode userData = usersJSON[id];
            string full_name = userData["name"];
            User user = new User(user_id: id,
                fullname: full_name,
                consented_to_video: (bool)GetOptionalSetting(userData, "video-consent", false),
                childDriven: (bool)GetOptionalSetting(userData, "child-driven", true),
                expressive: (bool)GetOptionalSetting(userData, "expressive", true));
            if (vocab.IsInVocab(user.GetShortName()))
            {
                users.Add(user);
            }
            else
            {
                Debug.Log($"WARNING!!! Name {user.GetShortName()} is not in vocab.");
            }
        }
        users = users.OrderBy(user => user.GetFullName()).ToList();
        vocab.AddUserNameSenses(users.Select(user => user.GetNameSense()).ToList());
    }

    private void SetupForNewUser(User user, DateTime loginTime)
    {
        this.loginTime = loginTime;
        this.user = user;
        recordingDecisionMade = false;
        TouchManager touchManager = GetComponent<TouchManager>();
        touchManager.Constrain();
        GameObject.FindWithTag("TitlePage").GetComponent<TitlePage>().Deploy(deployInstantly: true);
        GameObject.FindWithTag("LoginPage").GetComponent<LoginPage>().Retract(retractInstantly: true);
        machineDriver.enabled = !user.InChildDrivenCondition();
        private_avatars.Clear();
        private_avatar_versions.Clear();
        ConfigureUI();
    }

    private IEnumerator LoadUserConfiguration()
    {
        Debug.Log("LOADING USER CONFIGURATION");
        yield return LoadTutorialData();
        yield return LoadPrivateAvatars();
        yield return LoadScenes();
        Debug.Log("LOADING USER COMPLETED");
        OnFinishedLoadingUser();
    }

    private IEnumerator LoadTutorialData()
    {
        CoroutineResult<string> databaseResult = new CoroutineResult<string>();
        yield return databaseKeeper.Load($"groups/{group}/tutorial/{user.GetID()}", databaseResult, isImmutable: false);
        List<string> completedLessons = new List<string>();
        if (null != databaseResult.GetResult())
        {
            JSONNode tutorialJSON = JSONNode.Parse(databaseResult.GetResult());
            foreach (string lessonName in tutorialJSON.Keys)
            {
                if ((bool)tutorialJSON[lessonName])
                {
                    completedLessons.Add(lessonName);
                }
            }
        }
        GetComponent<Tutorial>().Setup(completedLessons);
        Debug.Log("TUTORIAL SET UP");
    }

    private IEnumerator LoadScenes()
    {
        CoroutineResult<string> databaseResult = new CoroutineResult<string>();
        yield return databaseKeeper.Load($"groups/{group}/scenes-catalogs/{user.GetID()}", databaseResult, isImmutable: false);
        if (null != databaseResult.GetResult())
        {
            JSONNode scenesRecordJSON = JSONNode.Parse(databaseResult.GetResult());
            foreach (string sceneID in scenesRecordJSON.Keys)
            {
                JSONNode sceneVersions = scenesRecordJSON[sceneID];
                string maxVersion = null;
                foreach (string versionID in sceneVersions.Keys)
                {
                    if (null == maxVersion || string.Compare(maxVersion, versionID) < 0) { maxVersion = versionID; }
                }
                if (null == maxVersion) continue;
                if (!sceneVersions[maxVersion]) continue;
                yield return databaseKeeper.Load($"groups/{group}/scenes/{user.GetID()}/{sceneID}/versions/{maxVersion}", databaseResult, isImmutable: true);
                if (null != databaseResult.GetResult())
                {
                    scene_versions[sceneID] = maxVersion;
                    scenes[sceneID] = databaseResult.GetResult();
                }
            }
        }
        OnScenesLoaded();
    }

    private void OnScenesLoaded()
    {
        List<string> activeScenes = new List<string>(scenes.Keys);
        activeScenes.Sort();
        GameObject.FindWithTag("Gallery").GetComponent<Gallery>().Setup(activeScenes);
        usedThemes = new List<string>();
        foreach (string activeScene in activeScenes)
        {
            string[] themes = GetSceneTheme(scenes[activeScene]);
            usedThemes.AddRange(themes);
        }
    }

    private string[] GetSceneTheme(string sceneDescription)
    {
        try
        {
            string keyString = "\"theme\":";
            int keyStringIndex = sceneDescription.IndexOf(keyString);
            if (keyStringIndex < 0) return new string[0];
            int openingQuoteIndex = sceneDescription.IndexOf("\"", startIndex: keyStringIndex + keyString.Length);
            int closingQuoteIndex = sceneDescription.IndexOf("\"", startIndex: openingQuoteIndex + 1);
            string themesString = sceneDescription.Substring(openingQuoteIndex, closingQuoteIndex - openingQuoteIndex);
            return themesString.Split('+');
        }
        catch
        {
            return null;
        }
    }

    private void InitRoboPartner()
    {
        if ("tablet" == type)
        {
            roboPartner = gameObject.AddComponent<VirtualJiboPartner>();
        }
        else
        {
            roboPartner = gameObject.AddComponent<ROSRoboPartner>();
            ((ROSRoboPartner)roboPartner).Setup(rosip);
        }
    }

    private JSONNode GetOptionalSetting(JSONNode data, string setting, JSONNode defaultValue)
    {
        JSONNode value = data[setting];
        if (null != value) return value;
        return defaultValue;
    }

    private void OnFinishedLoadingGroup()
    {
        GameObject.FindWithTag("TitlePage").GetComponent<TitlePage>().Retract(retractInstantly: false);
        LoginPage loginPage = GameObject.FindWithTag("LoginPage").GetComponent<LoginPage>();
        loginPage.Setup(users);
        loginPage.Deploy(deployInstantly: true);
        if (!databaseKeeper.IsConnected()) { AskToConnect(); }
    }

    private void OnFinishedLoadingUser()
    {
        InitRoboPartner();
        Gallery gallery = GameObject.FindWithTag("Gallery").GetComponent<Gallery>();
        Tutorial tutorial = GetComponent<Tutorial>();
        if (isLoggingEnabled && user.GetID() != "guest") {
            Logging.StartNewLog(tutorialLessons: tutorial.GetCompletedLessons(),
                sharedAvatarVersions: shared_avatar_versions,
                privateAvatarVersions: private_avatar_versions,
                sceneVersions: scene_versions);
        }
        if (scenes.Count > 0 && tutorial.IsLessonCompleted("gallery"))
        {
            gallery.Deploy(deployInstantly: true);
        }
        GetComponent<IdeaMaster>().Reload(usedThemes);
        GetComponent<ConversationMaster>().StartIntro();
    }

    private void ConfigureUI()
    {
        GameObject wordDrawerObj = GameObject.FindWithTag("WordDrawer");
        Debug.Log(wordDrawerObj.name);
        WordDrawer wordDrawer = wordDrawerObj.GetComponent<WordDrawer>();
        wordDrawer.ConfigureUI();
        Gallery gallery = GameObject.FindWithTag("Gallery").GetComponent<Gallery>();
        gallery.ConfigureUI();
        if (null != machineDriver) { machineDriver.ConfigureUI(); }
        foreach (MachineDriverThemeChoiceButton button in MachineDriver.GetThemeChoiceButtons()) { button.gameObject.SetActive(false); }
        mainIdeaButton.SetActive(user.InChildDrivenCondition());
        GameObject canvasHelpButton = GameObject.FindGameObjectsWithTag("InterfaceHelpButton").First(gObj => null == gObj.transform.parent);
        Vector3 buttonPos = canvasHelpButton.transform.position;
        canvasHelpButton.transform.position = new Vector3(buttonPos.x, user.InChildDrivenCondition() ? 2.03f : 3.9f, buttonPos.z);
        if ("tablet" == type) { VirtualJiboPartner.ReconfigureUI(); }
    }

    private IEnumerator AnnounceIssueCoroutine(string messageFile)
    {
        return SoundUtils.PlayAudioCoroutine(GetComponent<AudioSource>(), Resources.Load<AudioClip>(messageFile));
    }

    private void OnLogMessage(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception) return;
        try { Logging.LogError(condition, stackTrace); } catch { }
    }

    private void Quit()
    {
        //shuttingDown = true;
        Debug.Log("Quitting");
        Application.Quit();
#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject activity = new AndroidJavaClass("com.unity3d.player.UnityPlayer").GetStatic<AndroidJavaObject>("currentActivity");
        activity.Call("finish");
#endif
    }

    private void MarkActivityStart()
    {
        if (!markActivity || null != replayer) return;
        if (null != sessionStartMark || null == user) return;
        sessionStartMark = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-FFF");
        databaseKeeper.Write($"groups/{group}/activity/{user.GetID()}/{sessionStartMark}", "\"none\"");
        nextActivityMarkTime = TimeKeeper.time + 60;
    }

    private void UpdateActivityMarker()
    {
        if (null != replayer) return;
        if (null == sessionStartMark || null == user) return;
        databaseKeeper.Write($"groups/{group}/activity/{user.GetID()}/{sessionStartMark}", $"\"{DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-FFF")}\"");
        nextActivityMarkTime = TimeKeeper.time + 60;
    }

    private void MarkActivityEnd()
    {
        if (null != replayer) return;
        UpdateActivityMarker();
        sessionStartMark = null;
    }

    private void AskToConnect()
    {
        string[] connectionNotifications = {    "Could you ask your parents to connect me to the Internet? I work better this way!",
                                                "Please ask your parents to connect me to the Internet!",
                                                "Looks like there is no Internet. Could you please ask your parents to connect me?" };
        GetComponent<SynthesizerController>().Speak(RandomUtil.PickOne("no-internet", connectionNotifications), cause: "no-internet", canInterrupt: false);
    }
}
