using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;
using MiniJSON;

public class SpeechRecoServerComm : MonoBehaviour
{
    private const string URL = "https://speech.googleapis.com/v1/speech:recognize";
    private string API_KEY = "";
    private Vocab vocab = null;
    private int recoCount = 0;
    private int recognitionID = 0;

    private bool isReplayMode = false;
    private Dictionary<int, string> replayRecognitionResults = new Dictionary<int, string>();
    private const string INTERRUPT_RECO_RESULT = "INTERRUPT";

    public const string RECO_ERROR = "reco-error";
    public const string NO_WORDS_PICKED = "no-words-picked";
    public const string UNKNOWN_WORDS_PICKED = "unknown-words-picked";

    private List<int> replayRecoIDStack = new List<int>();

    void Start()
    {
        vocab = GetComponent<Vocab>();
        API_KEY = Config.GetConfig("KeyConfig")["GoogleSpeechAPIKey"];
    }

    public bool IsRecognizing()
    {
        return recoCount > 0;
    }

    public IEnumerator Recognize(AudioClip clip, CoroutineResult<List<string>> result)
    {
        if (!isReplayMode)
        {
            return ActuallyRecognize(clip, result);
        }
        else
        {
            return ReplayRecognize(clip, result);
        }
    }

    public IEnumerator GetAudioTranscripts(AudioClip clip, CoroutineResult<List<string>> result)
    {
        using (UnityWebRequest request = GenerateRecognitionRequest(clip, 3))
        {
            yield return request.SendWebRequest();
            if (request.isNetworkError || request.isHttpError)
            {
                Debug.Log("ERROR " + request.error);
                result.SetErrorCode(RECO_ERROR);
                yield break;
            }
            GetPossibleTranscripts(request.downloadHandler.text, result);
        }
    }

    private IEnumerator ActuallyRecognize(AudioClip clip, CoroutineResult<List<string>> result)
    {
        int myRecoID = recognitionID++;
        Logging.LogSpeechRecoStart(myRecoID);
        using (UnityWebRequest request = GenerateRecognitionRequest(clip, 20))
        {
            ++recoCount;
            yield return request.SendWebRequest();
            if (request.isNetworkError || request.isHttpError)
            {
                Debug.Log("ERROR " + request.error);
                result.SetErrorCode(RECO_ERROR);
                --recoCount;
                Logging.LogSpeechRecoInterrupt(myRecoID);
                yield break;
            }
            Logging.LogSpeechRecoResult(myRecoID, request.downloadHandler.text);
            GetPossibleResults(request.downloadHandler.text, result);
            --recoCount;
        }
    }

    private IEnumerator ReplayRecognize(AudioClip clip, CoroutineResult<List<string>> result)
    {
        while (0 == replayRecoIDStack.Count) { yield return null; }
        int myRecoID = replayRecoIDStack[0];
        replayRecoIDStack.RemoveAt(0);
        while (!replayRecognitionResults.ContainsKey(myRecoID)) { yield return null; }
        string recoResult = replayRecognitionResults[myRecoID];
        replayRecognitionResults.Remove(myRecoID);
        if (INTERRUPT_RECO_RESULT == recoResult)
        {
            result.SetErrorCode(RECO_ERROR);
        }
        else
        {
            GetPossibleResults(recoResult, result);
        }
    }

    public void SetReplayMode(bool replayMode)
    {
        this.isReplayMode = replayMode;
    }

    public void ReplayRecoStart(int recoID)
    {
        replayRecoIDStack.Add(recoID);
    }

    public void ReplayRecoResult(int recoID, string recoResult)
    {
        replayRecognitionResults[recoID] = recoResult;
    }

    public void ReplayRecoInterrupt(int recoID)
    {
        replayRecognitionResults[recoID] = INTERRUPT_RECO_RESULT;
    }

    private UnityWebRequest GenerateRecognitionRequest(AudioClip audioClip, int maxAlternatives)
    {
        string uri = $"{URL}?key={API_KEY}";
        UnityWebRequest request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST);
        string data_json = GenerateRecognitionJSON(audioClip, maxAlternatives);
        Debug.Log(data_json);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(data_json));
        request.downloadHandler = new DownloadHandlerBuffer();
        request.SetRequestHeader("Content-Type", "application/json");
        return request;
    }

    private string GenerateRecognitionJSON(AudioClip audioClip, int maxAlternatives)
    {
        Dictionary<string, object> request = new Dictionary<string, object>();
        Dictionary<string, object> config = new Dictionary<string, object>();
        config["encoding"] = "LINEAR16";
        config["languageCode"] = "en-US";
        config["sampleRateHertz"] = 16000;
        config["maxAlternatives"] = maxAlternatives;
        config["profanityFilter"] = true;
        Dictionary<string, object> speechContext = new Dictionary<string, object>();
        List<object> phrases = new List<object>(vocab.GetCustomNames());
        phrases.AddRange(vocab.GetCustomWords());
        speechContext["phrases"] = phrases;
        speechContext["boost"] = 5;
        config["speechContexts"] = new List<object>() { speechContext };
        request["config"] = config;
        Dictionary<string, object> audio = new Dictionary<string, object>();
        audio["content"] = SoundUtils.ConvertToString(audioClip); 
        request["audio"] = audio;
        return Json.Serialize(request);
    }

    public void GetPossibleResults(string response_data, CoroutineResult<List<string>> result)
    {
        Debug.Log(response_data);
        List<Transcript> transcripts = GetTranscripts(response_data);
        List<Candidate> candidates = new List<Candidate>();
        foreach (Transcript transcript in transcripts)
        {
            CollectOptions(transcript, candidates);
        }
        List<string> recoResults = candidates.OrderByDescending(candidate => candidate.score).Select(candidate => candidate.GetWordSense()).ToList();
        if (0 == transcripts.Count)
        {
            result.SetErrorCode(NO_WORDS_PICKED);
        }
        else if (0 == recoResults.Count)
        {
            string unknownWord = GetBestUnknownWord(transcripts);
            if (unknownWord != null)
            {
                Debug.Log("[SpeechRecoServerComm] Accepting unknown word for image generation: " + unknownWord);
                result.Set(new List<string> { unknownWord });
            }
            else
            {
                result.SetErrorCode(UNKNOWN_WORDS_PICKED);
            }
        }
        else
        {
            result.Set(recoResults);
        }
    }

    private List<Transcript> GetTranscripts(string response_data)
    {
        List<Transcript> alternatives = new List<Transcript>();
        Dictionary<string, object> response_json = null;
        try
        {
            response_json = (Dictionary<string, object>)Json.Deserialize(response_data);
        }
        catch (Exception e)
        {
            ExceptionUtil.OnException(e);
            return alternatives;
        }
        if (!response_json.ContainsKey("results")) { Debug.Log("SKIPPING DUE TO NO RESULTS"); return alternatives; }
        List<object> results_json = (List<object>)response_json["results"];
        foreach (object result_obj in results_json)
        {
            Dictionary<string, object> result_json = (Dictionary<string, object>)result_obj;
            if (!result_json.ContainsKey("alternatives")) { Debug.Log("SKIPPING DUE TO NO ALTERNATIVES"); continue; }
            List<object> alternatives_json = (List<object>)result_json["alternatives"];
            foreach (object alternative_obj in alternatives_json)
            {
                Dictionary<string, object> alternativeDict = (Dictionary<string, object>)alternative_obj;
                if (!alternativeDict.ContainsKey("transcript") || !alternativeDict.ContainsKey("confidence")) { Debug.Log("SKIPPING DUE TO NO TRANSCRIPT FIELD OR CONFIDENCE"); continue; }
                string transcript = (string)alternativeDict["transcript"];
                double confidence;
                try   { confidence = Convert.ToDouble(alternativeDict["confidence"]); }
                catch { confidence = 1.0f; }
                alternatives.Add(new Transcript(transcript, confidence));
            }
        }
        return alternatives.OrderByDescending(alternative => alternative.confidence).ToList();
    }

    private void CollectOptions(Transcript alternative, List<Candidate> outputOptions)
    {
        try
        {
            Debug.Log($"TRANSCRIPT: {alternative.transcript} $CONF: {alternative.confidence:0.###}");
            List<string> words = alternative.transcript.Split(' ').Where(word => word.All(char.IsLetter)).Select(word => word.ToLower()).ToList();
            foreach (string word in words)
            {
                CheckOptions(word, ScoreFactor(1, words.Count) * alternative.confidence, outputOptions);
            }
            for (int i = 0; i < words.Count() - 1; ++i)
            {
                string target = $"{words[i]}{words[i + 1]}";
                CheckOptions(target, ScoreFactor(2, words.Count) * alternative.confidence, outputOptions);
            }
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private float ScoreFactor(int targetCount, int wordCount)
    {
        if (targetCount < wordCount) return 0.5f;
        return 1;
    }

    private bool HasWord(string word, List<Candidate> consideredOptions)
    {
        return consideredOptions.Any(option => option.word == word);
    }

    private void CheckOptions(string word, double score, List<Candidate> outputOptions)
    {
        if (HasWord(word, outputOptions)) return;
        bool interpreted = false;
        List<string> imageableSenses = vocab.GetImageableSenses(word);
        foreach (string imageableSense in imageableSenses)
        {
            if (!vocab.IsInVocab(imageableSense)) continue;
            outputOptions.Add(new Candidate(imageableSense, score));
            interpreted = true;
        }
        if (!interpreted && vocab.IsInVocab(word) && !vocab.IsSwearWord(word))
        {
            outputOptions.Add(new Candidate(word, score));
        }
    }

    private string GetBestUnknownWord(List<Transcript> transcripts)
    {
        // Only accept single-word transcripts (no fragments like "wom" from "wom bat")
        // Transcripts are already sorted by confidence, so the first match is the best
        foreach (Transcript transcript in transcripts)
        {
            string word = transcript.transcript.Trim().ToLower();
            if (word.Contains(" ")) continue;
            if (!word.All(char.IsLetter)) continue;
            if (word.Length < 3) continue;
            try { if (vocab.IsSwearWord(word)) continue; } catch { }
            return word;
        }
        return null;
    }

    private struct Transcript
    {
        public string transcript;
        public double confidence;

        public Transcript(string transcript, double confidence)
        {
            this.transcript = transcript;
            this.confidence = confidence;
        }
    }

    private struct Candidate
    {
        public string word;
        public double score;

        public Candidate(string word, double score)
        {
            this.word = word;
            this.score = score;
        }

        public string GetWordSense()
        {
            return word;
        }
    }

    private void GetPossibleTranscripts(string response_data, CoroutineResult<List<string>> result)
    {
        List<string> alternatives = new List<string>();
        Dictionary<string, object> response_json = null;
        try
        {
            response_json = (Dictionary<string, object>)Json.Deserialize(response_data);
        }
        catch (Exception e)
        {
            ExceptionUtil.OnException(e);
            result.SetErrorCode(RECO_ERROR);
            return;
        }
        if (!response_json.ContainsKey("results")) { Debug.Log("SKIPPING DUE TO NO RESULTS"); result.Set(alternatives); return; }
        List<object> results_json = (List<object>)response_json["results"];
        foreach (object result_obj in results_json)
        {
            Dictionary<string, object> result_json = (Dictionary<string, object>)result_obj;
            if (!result_json.ContainsKey("alternatives")) { Debug.Log("SKIPPING DUE TO NO ALTERNATIVES"); continue; }
            List<object> alternatives_json = (List<object>)result_json["alternatives"];
            foreach (object alternative_obj in alternatives_json)
            {
                Dictionary<string, object> alternativeDict = (Dictionary<string, object>)alternative_obj;
                if (!alternativeDict.ContainsKey("transcript")) { Debug.Log("SKIPPING DUE TO NO TRANSCRIPT FIELD OR CONFIDENCE"); continue; }
                string transcript = (string)alternativeDict["transcript"];
                alternatives.Add(transcript);
            }
        }
        result.Set(alternatives);
        return;
    }
}
