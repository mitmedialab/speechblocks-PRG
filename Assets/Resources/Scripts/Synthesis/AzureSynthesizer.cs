using System;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class AzureSynthesizer : ISynthesizerModule {
    private Dictionary<string, object> acapelaToIPATree = new Dictionary<string, object>();

    private AudioSource audioSource;
    private string tempPath;
    private string token = null;
    private double lastTokenTime = -10000000;
    private bool isPlaying = false;

    private CoroutineRunner tokenCoroutineRunner = new CoroutineRunner();

    private bool hasBackup = false;

    private const int TOKEN_UPDATE_TIME = 9 * 60;

    private string SUBSCRIPTION_KEY = "";

    private double nextAllowedRequestTime = -1000;

    private const float INTERVAL_BETWEEN_REQUESTS = 0.3f;

    private float INTERNET_ERROR_WAIT_TIME;
    private float OTHER_ERROR_WAIT_TIME;
    private float GIVE_UP_TIME;

    private int lastSpeechStatus = SynthesizerController.SPEECH_STATUS_FAIL;

    private bool internetIssue = false;

    private const float BASE_RATE = 0.9f;
    private const float MAX_REQUEST_WAIT_TIME = 2f;

    public AzureSynthesizer(AudioSource audioSource)
    {
        acapelaToIPATree = PhonemeUtil.LoadPhonemeConversion("Config/ACAPELA_to_IPA");
        this.audioSource = audioSource;
        INTERNET_ERROR_WAIT_TIME = 1f;
        OTHER_ERROR_WAIT_TIME = hasBackup ? 60f : 1f;
        GIVE_UP_TIME = hasBackup ? 0.4f : 1.5f;
        tempPath = $"{Application.persistentDataPath}/azure-speech.wav";
        SUBSCRIPTION_KEY = Config.GetConfig("KeyConfig")["AzureOcpApimSubscriptionKey"];
        StartObtainingToken();
    }

    public void EnableBackup()
    {
        hasBackup = true;
    }

    public void Update()
    {
        if (!tokenCoroutineRunner.IsRunning() && TimeKeeper.time - lastTokenTime > TOKEN_UPDATE_TIME)
        {
            StartObtainingToken();
        }
        tokenCoroutineRunner.Update();
    }

    public IEnumerator Speak(SynQuery query)
    {
        lastSpeechStatus = SynthesizerController.SPEECH_STATUS_FAIL;
        if (hasBackup && internetIssue)
        {
            lastSpeechStatus = SynthesizerController.SPEECH_STATUS_NO_INTERNET;
            yield break;
        }
        bool keepPauses = (bool)query.GetParam("keep_pauses", true);
        string ssmlCode = null;
        double t0 = TimeKeeper.time;
        double maxTime = t0 + GIVE_UP_TIME;
        while (TimeKeeper.time < maxTime && nextAllowedRequestTime < maxTime)
        {
            while (TimeKeeper.time < nextAllowedRequestTime) { yield return null; }
            if (null == ssmlCode) {
                ssmlCode = FormSSMLForAzure(query);
                if (null == ssmlCode)
                {
                    lastSpeechStatus = SynthesizerController.SPEECH_STATUS_OK;
                    yield break;
                }
            }
            using (UnityWebRequest request = FormSpeechRequest(ssmlCode))
            {
                Debug.Log("AZURE SENDING REQUEST");
                AsyncOperation sendOperation = request.SendWebRequest();
                t0 = TimeKeeper.time;
                while (!sendOperation.isDone && Time.time - t0 < MAX_REQUEST_WAIT_TIME) yield return null;
                if (!sendOperation.isDone)
                {
                    Debug.Log("AZURE TIMEOUT");
                    lastSpeechStatus = SynthesizerController.SPEECH_STATUS_NO_INTERNET; // Do not send the internet issue flag, because next time we might succeed
                    yield break;
                }
                else if (request.isNetworkError)
                {
                    Debug.Log("AZURE NETWORK ERROR");
                    internetIssue = true;
                    lastSpeechStatus = SynthesizerController.SPEECH_STATUS_NO_INTERNET;
                    yield break;
                }
                else if (request.responseCode != 200)
                {
                    if (request.responseCode == 401)
                    {
                        Debug.Log("AZURE ERROR 401 - reacquiring token");
                        yield return WaitForToken(maxTime);
                    }
                    else if (request.responseCode == 429)
                    {
                        Debug.Log("AZURE ERROR 429");
                        float waitTime = ObtainError429WaitTime(request);
                        nextAllowedRequestTime = TimeKeeper.time + waitTime;
                        if (maxTime < nextAllowedRequestTime) yield break;
                        while (TimeKeeper.time < nextAllowedRequestTime) { yield return null; }
                        yield return WaitForToken(maxTime);
                    }
                    else
                    {
                        Debug.Log($"AZURE ERROR {request.responseCode}");
                        yield break;
                    }
                }
                else
                {
                    yield return OnRequestSuccess(request, keepPauses);
                    yield break;
                }
            }
        }
    }

    public int LastSpeechStatus()
    {
        return lastSpeechStatus;
    }

    public void Interrupt()
    {
        if (isPlaying) { audioSource.Stop(); audioSource.clip = null; }
        isPlaying = false;
    }

    public bool IsActivelySpeaking()
    {
        return audioSource.isPlaying;
    }

    // the function below assumes that query is in flat form: the root has no modifiers and a list of children; all children are leaves
    private string FormSSMLForAzure(SynQuery synQuery)
    {
        SynQuery azureQuery = BuildSynQueryForAzure(synQuery);
        if (null == azureQuery) return null;
        return "<speak xmlns=\"http://www.w3.org/2001/10/synthesis\" xmlns:mstts=\"http://www.w3.org/2001/mstts\" xmlns:emo=\"http://www.w3.org/2009/10/emotionml\" version=\"1.0\" xml:lang=\"en-US\">"
            + "<voice name=\"en-US-AnaNeural\">"
            + "<prosody pitch=\"-12%\">"
            + SynQuery.BuildSSML(azureQuery)
            + "</prosody></voice></speak>";
            // + "</voice></speak>";
    }

    private SynQuery BuildSynQueryForAzure(SynQuery synQuery)
    {
        List<List<SynQuery>> groups = SynQuery.GroupSequenceByProsody(synQuery.GetChildren());
        List<SynQuery> secondLevel = new List<SynQuery>();
        foreach (List<SynQuery> group in groups)
        {
            if (0 == group.Count) continue;
            List<SynQuery> modGroup = new List<SynQuery>();
            float rate = BASE_RATE * group[0].GetRate();
            foreach (SynQuery item in group)
            {
                SynQuery mod = SynQuery.Del(item, "rate");
                if (mod.ContainsKey("phonemecode"))
                {
                    mod = SynQuery.Mod(mod, "alphabet", "ipa", "phonemecode", PreparePhonemecodeForAzure((string)mod.GetParam("phonemecode")));
                }
                modGroup.Add(mod);
            }
            SynQuery secondLevelQuery = SynQuery.Rate(SynQuery.Seq(modGroup), rate);
            secondLevel.Add(secondLevelQuery);
        }
        if (0 == secondLevel.Count) return null;
        return SynQuery.Seq(secondLevel);
    }

    private string PreparePhonemecodeForAzure(string acapelaCode)
    {
        string phonemecode = MakePhonemeAdjustments(acapelaCode);
        phonemecode = PhonemeUtil.ConvertPhonemes(acapelaToIPATree, phonemecode);
        return ChangeAccentsAndSeparators(phonemecode);
    }

    private string MakePhonemeAdjustments(string phonemecode)
    {
        string[] phonemes = phonemecode.Split(';');
        string lastPhoneme = phonemes[phonemes.Length - 1];
        if (PhonemeUtil.Unaccentuated(lastPhoneme) == "V")
        {
            phonemes[phonemes.Length - 1] = "A" + PhonemeUtil.GetAccent(lastPhoneme);
        }
        return string.Join(";", phonemes);
    }

    private string ChangeAccentsAndSeparators(string phonemecode)
    {
        StringBuilder stringBuilder = new StringBuilder();
        foreach (char c in phonemecode)
        {
            if (';' == c || '0' == c) continue;
            if ('1' == c)
            {
                stringBuilder.Append('ˈ');
            }
            else if ('2' == c)
            {
                stringBuilder.Append('ˌ');
            }
            else
            {
                stringBuilder.Append(c);
            }
        }
        return stringBuilder.ToString();
    }

    private void StartObtainingToken()
    {
        if (!tokenCoroutineRunner.IsRunning()) { tokenCoroutineRunner.SetCoroutine(ObtainTokenCoroutine()); }
    }

    private IEnumerator WaitForToken(double maxTime)
    {
        StartObtainingToken();
        while (tokenCoroutineRunner.IsRunning() && TimeKeeper.time < maxTime) yield return null;
    }

    private IEnumerator ObtainTokenCoroutine()
    {
        while (true)
        {
            using (UnityWebRequest request = FormTokenRequest())
            {
                yield return request.SendWebRequest();
                if (request.isNetworkError)
                {
                    internetIssue = true;
                    yield return CoroutineUtils.WaitCoroutine(INTERNET_ERROR_WAIT_TIME);
                }
                else
                {
                    internetIssue = false;
                    if (request.responseCode != 200)
                    {
                        yield return CoroutineUtils.WaitCoroutine(OTHER_ERROR_WAIT_TIME);
                        yield break;
                    }
                    else
                    {
                        token = request.downloadHandler.text;
                        lastTokenTime = TimeKeeper.time;
                        yield break;
                    }
                }
            }
        }
    }

    private float ObtainError429WaitTime(UnityWebRequest request)
    {
        float waitTime;
        if (float.TryParse(request.GetResponseHeader("Retry-After"), out waitTime))
        {
            return waitTime;
        }
        else
        {
            return 5f;
        }
    }

    private UnityWebRequest FormSpeechRequest(string ssmlCode)
    {
        string uri = "https://germanywestcentral.tts.speech.microsoft.com/cognitiveservices/v1";
        UnityWebRequest request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST);
        request.SetRequestHeader("X-Microsoft-OutputFormat", "riff-16khz-16bit-mono-pcm");
        request.SetRequestHeader("Content-type", "application/ssml+xml");
        request.SetRequestHeader("Authorization", "Bearer " + token);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(ssmlCode));
        request.downloadHandler = new DownloadHandlerBuffer();
        return request;
    }


    private UnityWebRequest FormTokenRequest()
    {
        string uri = "https://germanywestcentral.api.cognitive.microsoft.com/sts/v1.0/issuetoken";
        UnityWebRequest request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST);
        request.SetRequestHeader("Content-type", "application/x-www-form-urlencoded");
        request.SetRequestHeader("Ocp-Apim-Subscription-Key", SUBSCRIPTION_KEY);
        request.downloadHandler = new DownloadHandlerBuffer();
        return request;
    }

    private IEnumerator OnRequestSuccess(UnityWebRequest request, bool keepPauses)
    {
        nextAllowedRequestTime = TimeKeeper.time + INTERVAL_BETWEEN_REQUESTS;
        WWW www;
        try
        {
            File.WriteAllBytes(tempPath, request.downloadHandler.data);
            string url = $"file://{tempPath}";
            www = new WWW(url);
        }
        catch (Exception e)
        {
            Debug.Log("AZURE EXCEPTION: " + e.Message);
            yield break;
        }
        yield return www;
        AudioClip audioClip = null;
        try
        {
            audioClip = www.GetAudioClip(false, false);
        }
        catch (Exception e)
        {
            Debug.Log("AZURE EXCEPTION: " + e.Message);
            yield break;
        }
        if (!keepPauses)
        {
            AudioClip oldAudioClip = audioClip;
            audioClip = SoundUtils.TrimSilence(oldAudioClip, 0.01f);
            MonoBehaviour.Destroy(oldAudioClip);
        }
        isPlaying = true;
        Debug.Log("PLAYING SPEECH CLIP");
        yield return SoundUtils.PlayAudioCoroutine(audioSource, audioClip);
        isPlaying = false;
        lastSpeechStatus = SynthesizerController.SPEECH_STATUS_OK;
        Debug.Log("SPEECH COMPLETED");
        MonoBehaviour.Destroy(audioClip);
        audioSource.clip = null;
    }
}
