using System;
using System.Globalization;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using SimpleJSON;

// Loading sprites using recipe from https://forum.unity.com/threads/generating-sprites-dynamically-from-png-or-jpeg-files-in-c.343735/

public class PictureBlock : MonoBehaviour, ITappable, IDetailedLogging, IStackable, ITouchListener
{
    private string id = null;
    private string theme = null;
    private DateTime timestamp = DateTime.Now;

    public JSONNode Serialize()
    {
        JSONObject description = new JSONObject();
        SerializationUtil.SerializeTransform(transform, description);
        description["id"] = id;
        description["pic"] = GetComponent<Picture>().Serialize();
        description["ord"] = ZSorting.GetSortingOrder(gameObject);
        description["timestamp"] = timestamp.ToString("yyyy-MM-dd-HH-mm-ss-FFF");
        if (null != theme) { description["theme"] = theme; }
        return description;
    }

    public bool Matches(JSONNode description)
    {
        try
        {
            if (id != description["id"]) return false;
            if (ZSorting.GetSortingOrder(gameObject) != description["ord"]) return false;
            if (!SerializationUtil.TransformMatches(transform, description)) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Setup(Transform parent, JSONNode description)
    {
        id = description["id"];
        if (null == id) { id = System.Guid.NewGuid().ToString(); }
        transform.SetParent(parent, false);
        SerializationUtil.DeserializeTransform(transform, description);
        GetComponent<Picture>().Setup(description["pic"]);
        ZSorting.SetSortingOrder(gameObject, description["ord"]);
        theme = description["theme"];
        try
        {
            timestamp = DateTime.ParseExact(description["timestamp"], "yyyy-MM-dd-HH-mm-ss-FFF", CultureInfo.InvariantCulture);
        }
        catch
        {
            timestamp = DateTime.Now;
        }
    }

    public void Setup(Vector3 position, string wordSense, string sortingLayer)
    {
        if (!GetComponent<Picture>().IsSetUp()) { id = System.Guid.NewGuid().ToString(); }
        transform.position = position;
        Vocab vocab = GameObject.FindWithTag("StageObject").GetComponent<Vocab>();
        GetComponent<Picture>().Setup(wordSense, 2, 2, sortingLayer);
        timestamp = DateTime.Now;
    }

    public void SetTheme(string theme)
    {
        this.theme = theme;
    }

    public string GetTheme()
    {
        return theme;
    }

    public DateTime GetTimestamp()
    {
        return timestamp;
    }

    public float GetWidth()
    {
        Vector2 colliderSize = GetComponent<BoxCollider2D>().size;
        return colliderSize.x * transform.localScale.x;
    }

    public float GetHeight()
    {
        Vector2 colliderSize = GetComponent<BoxCollider2D>().size;
        return colliderSize.y * transform.localScale.y;
    }

    public string GetTermWordSense()
    {
        return GetComponent<Picture>().GetTermWordSense();
    }

    public bool IsAvatar()
    {
        return GetComponent<Picture>().IsAvatar();
    }

    public string GetImageWordSense()
    {
        return GetComponent<Picture>().GetImageWordSense();
    }

    public object[] GetLogDetails()
    {
        return new object[] { "img", GetImageWordSense(), "ppu", 100 };
    }

    public IEnumerator<Vector2> GetLocalProbePoints()
    {
        float probeRadius = 1.2f; // based on the span of the bounding box being 2
        for (int i = 0; i < ProbeHelper.RAYS_TO_CAST; ++i)
        {
            yield return ProbeHelper.GetProbePoint(Vector2.zero, probeRadius, i);
        }
    }

    public bool IsSuitableRoot(GameObject newRoot)
    {
        Vector3 myScale = transform.lossyScale;
        Vector3 theirScale = newRoot.transform.lossyScale;
        return theirScale.y > 0.9f * myScale.y;
    }

    public void OnTap(TouchInfo touchInfo)
    {
        Debug.Log("OnTap: " + gameObject.name);
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        if (!stageObject.GetComponent<Environment>().GetUser().InChildDrivenCondition()) return;
        if (!stageObject.GetComponent<Tutorial>().IsLessonCompleted("gallery")) return;
        string stage = ZSorting.GetSortingLayer(gameObject);
        if ("Default" != stage) return;
        AssociationsPanel assocPanel = GameObject.FindWithTag("AssociationsPanel").GetComponent<AssociationsPanel>();
        string term = GetComponent<Picture>().GetTermWordSense();
        if (assocPanel.HasAssociations(term))
        {
            assocPanel.Invoke(term, cause: Logging.GetObjectLogID(gameObject));
        }
    }

    public void TouchMoved(TouchInfo touchInfo)
    {
        // Debug.Log("OnTouchMoved: " + GetTermWordSense());
        return;
    }

    public void OnTouchUp(TouchInfo touchInfo)
    {
        // Debug.Log("OnTouchUp: " + GetTermWordSense());
        return;
    }
    public void OnTouch(TouchInfo touchInfo)
    {   
        string seedWord = GetTermWordSense();
        Debug.Log("User Touched PictureBlock: " + seedWord);
        GameObject.FindWithTag("CoCreationBox")?.GetComponent<CoCreateButton>().SetSeedWord(seedWord);
        return;
    }

}
