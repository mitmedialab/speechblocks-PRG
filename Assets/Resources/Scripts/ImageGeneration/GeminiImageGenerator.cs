using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public class GeminiImageGenerator : MonoBehaviour
{
    private static GeminiImageGenerator _instance;

    // Change this if you need a different Gemini model for image generation
    private string model = "gemini-2.5-flash-image";
    private string baseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private string API_KEY;
    private byte[] styleReferenceBytes;
    private Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

    public static GeminiImageGenerator Instance
    {
        get
        {
            if (_instance == null)
            {
                GameObject stageObject = GameObject.FindWithTag("StageObject");
                if (stageObject != null)
                {
                    _instance = stageObject.GetComponent<GeminiImageGenerator>();
                    if (_instance == null)
                    {
                        _instance = stageObject.AddComponent<GeminiImageGenerator>();
                    }
                }
            }
            return _instance;
        }
    }

    void Awake()
    {
        _instance = this;
        try
        {
            API_KEY = Config.GetConfig("KeyConfig")["Gemini_API_KEY"];
        }
        catch
        {
            Debug.LogWarning("[GeminiImageGenerator] No Gemini_API_KEY found in KeyConfig.json");
        }
        LoadStyleReference();
    }

    private void LoadStyleReference()
    {
        Texture2D styleRef = Resources.Load<Texture2D>("Images/ambulance");
        if (styleRef != null)
        {
            styleReferenceBytes = styleRef.EncodeToPNG();
            Debug.Log("[GeminiImageGenerator] Loaded style reference image (ambulance)");
        }
        else
        {
            Debug.LogWarning("[GeminiImageGenerator] Could not load style reference image");
        }
    }

    public bool IsAvailable()
    {
        return !string.IsNullOrEmpty(API_KEY);
    }

    public bool HasCached(string subject)
    {
        if (cache.ContainsKey(subject)) return true;
        // Check disk for a previously saved generated image
        if (File.Exists(GetSavePath(subject)))
        {
            LoadFromDisk(subject);
            return cache.ContainsKey(subject);
        }
        return false;
    }

    public Texture2D GetCached(string subject)
    {
        if (cache.ContainsKey(subject)) return cache[subject];
        // Try loading from disk
        if (File.Exists(GetSavePath(subject)))
        {
            LoadFromDisk(subject);
        }
        return cache.ContainsKey(subject) ? cache[subject] : null;
    }

    private string GetSaveDirectory()
    {
        return Path.Combine(Application.persistentDataPath, "GeneratedImages");
    }

    private string GetSavePath(string subject)
    {
        // Sanitize subject to be a safe filename
        string safeName = subject.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
        return Path.Combine(GetSaveDirectory(), safeName + ".png");
    }

    private void SaveToDisk(string subject, Texture2D texture)
    {
        try
        {
            string dir = GetSaveDirectory();
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            byte[] pngBytes = texture.EncodeToPNG();
            File.WriteAllBytes(GetSavePath(subject), pngBytes);
            Debug.Log("[GeminiImageGenerator] Saved generated image to disk for: " + subject);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GeminiImageGenerator] Failed to save image to disk: " + e.Message);
        }
    }

    private void LoadFromDisk(string subject)
    {
        try
        {
            string path = GetSavePath(subject);
            byte[] fileBytes = File.ReadAllBytes(path);
            Texture2D texture = new Texture2D(2, 2);
            if (texture.LoadImage(fileBytes))
            {
                cache[subject] = texture;
                Debug.Log("[GeminiImageGenerator] Loaded generated image from disk for: " + subject);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GeminiImageGenerator] Failed to load image from disk: " + e.Message);
        }
    }

    public IEnumerator GenerateImage(string subject, Action<Texture2D> callback)
    {
        if (cache.ContainsKey(subject))
        {
            Debug.Log("[GeminiImageGenerator] Returning cached image for: " + subject);
            callback?.Invoke(cache[subject]);
            yield break;
        }

        if (string.IsNullOrEmpty(API_KEY))
        {
            Debug.LogWarning("[GeminiImageGenerator] No API key configured");
            callback?.Invoke(null);
            yield break;
        }

        string url = $"{baseUrl}{model}:generateContent?key={API_KEY}";
        string prompt = $"Generate a clip art type image of a {subject} in the style of the attached image, with a white background.";

        Debug.Log("[GeminiImageGenerator] Generating image for: " + subject);

        // Build request parts
        var parts = new List<object>();

        if (styleReferenceBytes != null)
        {
            parts.Add(new Dictionary<string, object>
            {
                {"inlineData", new Dictionary<string, object>
                    {
                        {"mimeType", "image/png"},
                        {"data", Convert.ToBase64String(styleReferenceBytes)}
                    }
                }
            });
        }

        parts.Add(new Dictionary<string, object>
        {
            {"text", prompt}
        });

        var requestBody = new Dictionary<string, object>
        {
            {"contents", new List<object>
                {
                    new Dictionary<string, object>
                    {
                        {"parts", parts}
                    }
                }
            },
            {"generationConfig", new Dictionary<string, object>
                {
                    {"responseModalities", new List<string>{"TEXT", "IMAGE"}},
                    {"imageConfig", new Dictionary<string, object>
                        {
                            {"aspectRatio", "1:1"},
                            {"imageSize", "512px"}
                        }
                    }
                }
            }
        };

        string jsonBody = JsonConvert.SerializeObject(requestBody);

        using (UnityWebRequest request = new UnityWebRequest(url, UnityWebRequest.kHttpVerbPOST))
        {
            request.SetRequestHeader("Content-Type", "application/json");
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(jsonBody));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 30;
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("[GeminiImageGenerator] Request failed: " + request.error);
                Debug.LogError("[GeminiImageGenerator] Response: " + request.downloadHandler?.text);
                callback?.Invoke(null);
                yield break;
            }

            try
            {
                JObject response = JObject.Parse(request.downloadHandler.text);
                JArray responseParts = response["candidates"]?[0]?["content"]?["parts"] as JArray;

                if (responseParts != null)
                {
                    foreach (JToken part in responseParts)
                    {
                        JToken inlineData = part["inlineData"];
                        if (inlineData != null)
                        {
                            string base64Data = inlineData["data"].ToString();
                            byte[] imageBytes = Convert.FromBase64String(base64Data);

                            Texture2D texture = new Texture2D(2, 2);
                            if (texture.LoadImage(imageBytes))
                            {
                                texture = RemoveWhiteBackground(texture);
                                cache[subject] = texture;
                                SaveToDisk(subject, texture);
                                Debug.Log("[GeminiImageGenerator] Successfully generated image for: " + subject);
                                callback?.Invoke(texture);
                                yield break;
                            }
                        }
                    }
                }

                Debug.LogWarning("[GeminiImageGenerator] No image data in response for: " + subject);
                callback?.Invoke(null);
            }
            catch (Exception e)
            {
                Debug.LogError("[GeminiImageGenerator] Error parsing response: " + e.Message);
                callback?.Invoke(null);
            }
        }
    }

    private static Texture2D RemoveWhiteBackground(Texture2D source)
    {
        Texture2D result = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false);
        Color[] pixels = source.GetPixels();
        float threshold = 0.92f;
        for (int i = 0; i < pixels.Length; i++)
        {
            Color pixel = pixels[i];
            // Only modify opaque pixels that are white/near-white
            if (pixel.a > 0.5f && pixel.r > threshold && pixel.g > threshold && pixel.b > threshold)
            {
                pixels[i] = new Color(0, 0, 0, 0);
            }
        }
        result.SetPixels(pixels);
        result.Apply();
        return result;
    }
}
