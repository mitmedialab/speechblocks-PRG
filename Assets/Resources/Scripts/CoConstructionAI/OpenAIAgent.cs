using System;
using System.Text;
// using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Networking;

public class NarrativeResult
{
    public string initial_interpretation;
    public string primitive_narrative;
    public string focused_chain;
}

public class SymbolicPlayResult
{
    public string relative_location;
    public string spatial_proximity;
    public string anchor_object;
    public string relative_size;
}

public class OpenAIAgent : MonoBehaviour, IConstructionAgentModule
{
    private string[] narrativeHistory;
    private string[] childASRResult;
    private string SUBSCRIPTION_KEY;
    private string model = "gpt-4o-2024-08-06";
    private string uri = "https://api.openai.com/v1/responses";
    // Memoryless states for managing agent's turn status
    private List<string> currentOnsetItems = new List<string>();
    private string childTurnItem = null;
    private string agentTurnItem = null;
    private NarrativeResult narrativeResult = null;
    private SymbolicPlayResult symbolicPlayResult = null;


    public OpenAIAgent()
    {
        SUBSCRIPTION_KEY = Config.GetConfig("KeyConfig")["OpenAI_API_KEY"];
        Debug.Log("OpenAI Construction Agent Initialized!!! ");
        ClearAgentTurnStatus();
    }

    public void UpdateLastestScene()
    {

    }

    public void UpdateChildUtterance()
    {

    }

    public IEnumerator GetRobotIntegrativeElaboration(byte[] sceneSnapshot, List<string> onsetItems, string new_object, Action<PlacementUtil.RelativePlacement, float, string> callback)
    {
        Debug.Log("Loading scene image for OpenAI request!!! ");
        string systemInstruction = FormSystemIntructionForNarrativeConstruction(new_object, onsetItems, sceneSnapshot);
        Debug.Log("System Instruction: " + systemInstruction);

        using (UnityWebRequest request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST))
        {
            request.SetRequestHeader("Content-type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + SUBSCRIPTION_KEY);
            request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(systemInstruction));
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();
            JObject structuredJson = JObject.Parse(request.downloadHandler.text);
            if (structuredJson["output"][0]["status"].ToString() != "completed")
            {
                Debug.LogError("Error: Request failed with status " + structuredJson["output"][0]["status"].ToString());
            }
            else
            {
                NarrativeResult result = JsonUtility.FromJson<NarrativeResult>(structuredJson["output"][0]["content"][0]["text"].ToString());
                Debug.Log("Initial Interpretation: " + result.initial_interpretation);
                Debug.Log("Primitive Narrative: " + result.primitive_narrative);
                Debug.Log("Focused Chain: " + result.focused_chain);
                narrativeResult = result;
            }
        }

        yield return StartCoroutine(GetRobotSymbolicPlayBehavior(sceneSnapshot, onsetItems, new_object, callback));

    }

    public IEnumerator GetRobotSymbolicPlayBehavior(byte[] sceneSnapshot, List<string> onsetItems, string new_object, Action<PlacementUtil.RelativePlacement, float, string> callback)
    {
        string selected_narrative = narrativeResult.focused_chain;
        string systemInstruction = FormSystemIntructionForSymbolicPlay(sceneSnapshot, onsetItems, new_object, selected_narrative);
        Debug.Log("Getting Location for Symbolic Play: " + selected_narrative);

        string uri = "https://api.openai.com/v1/responses";
        UnityWebRequest request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST);
        request.SetRequestHeader("Content-type", "application/json");
        request.SetRequestHeader("Authorization", "Bearer " + SUBSCRIPTION_KEY);
        request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(systemInstruction));
        request.downloadHandler = new DownloadHandlerBuffer();
        yield return request.SendWebRequest();
        if (request.result != UnityWebRequest.Result.Success)
        {
            Debug.LogError("Error sending request: " + request.error);
        }
        else
        {
            Debug.Log("Request sent successfully!!! ");
            JObject structuredJson = JObject.Parse(request.downloadHandler.text);
            if (structuredJson["output"][0]["status"].ToString() != "completed")
            {
                Debug.LogError("Error: Request failed with status " + structuredJson["output"][0]["status"].ToString());
            }
            else
            {
                SymbolicPlayResult result = JsonUtility.FromJson<SymbolicPlayResult>(structuredJson["output"][0]["content"][0]["text"].ToString());
                Debug.Log("Related Object: " + result.anchor_object);
                Debug.Log("Spatial Proximity: " + result.spatial_proximity);
                Debug.Log("Relative Location: " + result.relative_location);
                Debug.Log("Relative Size: " + result.relative_size);
                symbolicPlayResult = result;
                PlacementUtil.RelativePlacement relativePlacement = PlacementUtil.MatchRelativePlacementByString(result.relative_location);
                float relativeSize = PlacementUtil.MatchRelativeSizeByString(result.relative_size);
                string relatedObject = result.anchor_object;
                callback?.Invoke(relativePlacement, relativeSize, relatedObject);
            }
        }
    }

    public void ClearAgentTurnStatus()
    {
        childTurnItem = null;
        agentTurnItem = null;
        narrativeResult = null;
        symbolicPlayResult = null;
        currentOnsetItems.Clear();
    }

    public NarrativeResult GetNarrativeResult()
    {
        return narrativeResult;
    }

    public SymbolicPlayResult GetSymbolicPlayResult()
    {
        return symbolicPlayResult;
    }

    // private IEnumerator MakeOpenAIRequest(string systemInstruction, string requestType)
    // {
    //     string uri = "https://api.openai.com/v1/responses";
    //     UnityWebRequest request = new UnityWebRequest(uri, UnityWebRequest.kHttpVerbPOST);
    //     request.SetRequestHeader("Content-type", "application/json");
    //     request.SetRequestHeader("Authorization", "Bearer " + SUBSCRIPTION_KEY);
    //     request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(systemInstruction));
    //     request.downloadHandler = new DownloadHandlerBuffer();
    //     yield return request.SendWebRequest();
    //     if (request.result != UnityWebRequest.Result.Success)
    //     {
    //         Debug.LogError("Error sending request: " + request.error);
    //         yield break;
    //     }
    //     else
    //     {
    //         Debug.Log("Request sent successfully!!! ");
    //         JObject structuredJson = JObject.Parse(request.downloadHandler.text);
    //         if (structuredJson["output"][0]["status"].ToString() != "completed")
    //         {
    //             Debug.LogError("Error: Request failed with status " + structuredJson["output"][0]["status"].ToString());
    //         }
    //         else
    //         {
    //             Debug.Log("Request sent successfully!!! ");
    //         }
    //     }
    // }

    private string FormSystemIntructionForNarrativeConstruction(string new_object, List<string> onsetItems, byte[] image_bytes)
    {
        Dictionary<string, object> SystemInstruction = new Dictionary<string, object>()
        {
            {"model", model},
            {"input", new Dictionary<string, object>()}
        };

        SystemInstruction["input"] = new List<Dictionary<string, object>>();
        (SystemInstruction["input"] as List<Dictionary<string, object>>).Add(new Dictionary<string, object>(){
            {"role", "system"},
            {"content", "You are a narrative construction assistant designed to power an agent's behavior in a co-creative interaction with children." +
            "You will be given an image scene with different objects to convey a narrative and a new object to be added to the narrative." +
            "With that, you generate a one line statement to explain how the new object is related to the current scene." +
            "You can use one of the following narrative strategies to come up with the semantic link in your narrative:\n" +
            "a. Primitive Narrative: a statement about how the new object is perceptually related to the narrative with a central theme." +
            "They may include emerging story structure elements (i.e. initiating event, actions, consequences) with basic joining words to link ideas (e.g. and, then).\n" +
            "b. Focused Chain: a narrative that expresses the temporal relationship or has a cause and effect relationship to introduce the new object into the narrative.\n\n" +
            "In your response, please first generate the initial interpretation of the scene." +
            "Based on the interpretation, generate a primitive narrative and a focused chain narrative, separately, to incorporate the new object into the scene." +
            "Make sure you use a simplified language that a 4 to 5 years old child would understand in narrative generation."}
        });
        (SystemInstruction["input"] as List<Dictionary<string, object>>).Add(new Dictionary<string, object>()
        {
            {"role", "user"},
            {"content", new List<Dictionary<string, object>>(){
                new Dictionary<string, object>(){
                    {"type", "input_text"},
                    {"text", "The onset items in the scene are: " + string.Join(", ", onsetItems) + ". The new object to be added is a " + new_object + "."}
                },
                new Dictionary<string, object>(){
                    {"type", "input_image"},
                    {"image_url", "data:image/png;base64," + Convert.ToBase64String(image_bytes)}
                }
            }}
        });

        SystemInstruction["text"] = BuildStructuredOutputForNarrativeConstruction();
        return JsonConvert.SerializeObject(SystemInstruction);
    }

    private string FormSystemIntructionForSymbolicPlay(byte[] image_bytes, List<string> onsetItems, string new_object, string new_narrative)
    {
        Dictionary<string, object> SystemInstruction = new Dictionary<string, object>()
        {
            {"model", model},
            {"input", new Dictionary<string, object>()}
        };

        SystemInstruction["input"] = new List<Dictionary<string, object>>();
        (SystemInstruction["input"] as List<Dictionary<string, object>>).Add(new Dictionary<string, object>(){
            {"role", "system"},
            {"content", "You are a narrative construction assistant designed to power an agent's behavior in a co-creative interaction with children." +
            "You will be given an image scene with different objects to convey a narrative and a new object to be added to the narrative." +
            "Giving a narrative about the new object's relationship to the current scene, you infer the location of the new object to reflect the semantic meaning of the narrative." +
            "To illustrate the location of the new object, you can use an anchor object to describe the relative position of the new object." +
            "For the location, you need to give four variables: anchor_object, spatial_proximity, relative_location, and relative_size." +
            "anchor_object is the object used to anchor the relative position of the new object." +
            "spatial_proximity is the spatial proximity of the new object to the anchor object, which can be one of the following: close or far." +
            "relative_location is the relative location of the new object to the anchor object, which can be one of the following: left, right, up, down." +
            "relative_size is the relative size of the new object to the anchor object, which can be one of the following: smaller, same, larger." +
            "Make sure the location make sense for the new object based on the narrative."}
        });
        (SystemInstruction["input"] as List<Dictionary<string, object>>).Add(new Dictionary<string, object>()
        {
            {"role", "user"},
            {"content", new List<Dictionary<string, object>>(){
                new Dictionary<string, object>(){
                    {"type", "input_text"},
                    {"text", "The new object to be added is a " + new_object + ". The narrative about the new object's relationship to the current scene is: " + new_narrative + "."}
                },
                new Dictionary<string, object>(){
                    {"type", "input_image"},
                    {"image_url", "data:image/png;base64," + Convert.ToBase64String(image_bytes)}
                }
            }}
        });

        SystemInstruction["text"] = BuildStructuredOutputForSymbolicPlay(onsetItems);
        return JsonConvert.SerializeObject(SystemInstruction);
    }

    private Dictionary<string, object> BuildStructuredOutputForNarrativeConstruction()
    {
        Dictionary<string, object> StructuredOutput = new Dictionary<string, object>();
        StructuredOutput["format"] = new Dictionary<string, object>(){
            {"type", "json_schema"},
            {"name", "generate_narrative_sequence"},
            {"strict", true}
        };
        (StructuredOutput["format"] as Dictionary<string, object>)["schema"] = new Dictionary<string, object>(){
            {"type", "object"},
            {"properties", new Dictionary<string, object>(){
                {"initial_interpretation", new Dictionary<string, object>(){
                    {"type", "string"},
                    {"description", "The initial interpretation of the scene sent in the input."}
                }},
                {"primitive_narrative", new Dictionary<string, object>(){
                    {"type", "string"},
                    {"description", "The primitive narrative to incorporate the new object into the scene. It should be a one line statement that is easy to understand by a 4 to 5 years old child."}
                }},
                {"focused_chain", new Dictionary<string, object>(){
                    {"type", "string"},
                    {"description", "The focused chain to incorporate the new object into the scene. It should be a one line statement that is easy to understand by a 4 to 5 years old child."}
                }}
            }},
            {"required", new List<string>(){"initial_interpretation", "primitive_narrative", "focused_chain"}},
            {"additionalProperties", false}
        };
        return StructuredOutput;
    }

    private Dictionary<string, object> BuildStructuredOutputForSymbolicPlay(List<string> onsetItems)
    {
        Dictionary<string, object> StructuredOutput = new Dictionary<string, object>();
        StructuredOutput["format"] = new Dictionary<string, object>(){
            {"type", "json_schema"},
            {"name", "generate_symbolic_play"},
            {"strict", true}
        };
        (StructuredOutput["format"] as Dictionary<string, object>)["schema"] = new Dictionary<string, object>(){
            {"type", "object"},
            {"properties", new Dictionary<string, object>(){
                {"anchor_object", new Dictionary<string, object>(){
                    {"type", "string"},
                    {"description", "The object used to anchor the relative position of the new object, which can only be one of the objects currently in the image."},
                    {"enum", onsetItems}
                }},
                {"spatial_proximity", new Dictionary<string, object>(){
                    {"type", "string"},
                    {"description", "The spatial proximity of the new object to the anchor object, which can be one of the following: close or far."},
                    {"enum", new List<string>(){"close", "far"}}
                }},
                {"relative_location", new Dictionary<string, object>(){
                    {"type", "string"},
                    {"description", "The relative location of the new object to the anchor object, which can be one of the following: left, right, up, down."},
                    {"enum", new List<string>(){"left", "right", "up", "down"}}
                }},
                {"relative_size", new Dictionary<string, object>(){
                    {"type", "string"},
                    {"description", "The relative size of the new object to the anchor object, which can be one of the following: smaller, same, larger."},
                    {"enum", new List<string>(){"smaller", "same", "larger"}}
                }}
            }},
            {"required", new List<string>(){"anchor_object", "spatial_proximity", "relative_location", "relative_size"}},
            {"additionalProperties", false}
        };
        return StructuredOutput;
    }
    
}
