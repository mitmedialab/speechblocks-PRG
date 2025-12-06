using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using SimpleJSON;
using System.Linq;

public class Composition : MonoBehaviour
{
    private GameObject pictureBlockPrefab = null;
    private Environment environment;
    private string sceneID = null;
    private string[] allowedForScreenshot = new string[] { "CompositionRoot", "MainCamera", "StageObject" };
    private JSONArray emptyJSONArray = new JSONArray();
    private GameObject _lastPlacedBlock;

    private void Start()
    {
        pictureBlockPrefab = Resources.Load<GameObject>("Prefabs/PictureBlock");
        environment = GameObject.FindWithTag("StageObject").GetComponent<Environment>();
    }

    public string Push(bool makeSnapshot)
    {
        if (!HasSomethingToPush()) return null;
        Debug.Log("COMPOSITION PUSH");
        JSONArray content = SerializePictureBlocksHierarchy(transform);
        if (content.Count > 0)
        {
            if (null == sceneID)
            {
                sceneID = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            }
            JSONObject sceneRecord = new JSONObject();
            sceneRecord["active"] = true;
            sceneRecord["content"] = content;
            environment.RecordScene(sceneID, sceneRecord.ToString());
            if (makeSnapshot) { CreateSceneSnapshot(environment.GetUser().GetID(), sceneID); }
            return sceneID;
        }
        return null;
    }

    public void Reset()
    {
        this.sceneID = null;
        foreach (Transform child in transform)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
    }

    public static string SceneFilePath(string userID, string sceneID, string sceneVersion)
    {
        if ("" == sceneVersion) return $"{Application.persistentDataPath}/SceneThumbnails/{userID}/{sceneID}.png"; ;
        return $"{Application.persistentDataPath}/SceneThumbnails/{userID}/{sceneID}-{sceneVersion}.png";
    }

    public void CreateSceneSnapshot(string userID, string sceneID)
    {
        Camera camera = GameObject.FindWithTag("MainCamera").GetComponent<Camera>();
        List<GameObject> hidden = HideUIElements();
        int height = 400;
        int width = (int)(height * camera.aspect);
        RenderTexture renderTexture = new RenderTexture(width, height, 24);
        camera.targetTexture = renderTexture;
        Texture2D screenShot = new Texture2D(width, height, TextureFormat.RGB24, false);
        try { camera.Render(); } catch { }
        RenderTexture.active = renderTexture;
        screenShot.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        camera.targetTexture = null;
        RenderTexture.active = null; // JC: added to avoid errors
        Destroy(renderTexture);
        byte[] bytes = screenShot.EncodeToPNG();
        string scenePath = SceneFilePath(userID, sceneID, environment.GetSceneVersion(sceneID));
        Directory.CreateDirectory(Path.GetDirectoryName(scenePath));
        Debug.Log($"SCENE PATH: {scenePath}");
        System.IO.File.WriteAllBytes(scenePath, bytes);
        RestoreUIElements(hidden);
    }

    public void Setup(string sceneID, JSONNode description)
    {
        Reset();
        this.sceneID = sceneID;
        DeserializePictureBlockHierarchy(transform, (JSONArray)description["content"]);
        AdjustInscriptionOrientation();
    }

    public static bool IsOnCanvas(Transform tform)
    {
        if ("CompositionRoot" == tform.tag) { return true; }
        if (null == tform.parent) { return false; }
        return IsOnCanvas(tform.parent);
    }

    public List<PictureBlock> GetTopPictureBlocks()
    {
        List<PictureBlock> pictureBlocks = new List<PictureBlock>();
        foreach (Transform canvasChild in transform)
        {
            PictureBlock pictureBlock = canvasChild.GetComponent<PictureBlock>();
            if (null != pictureBlock) { pictureBlocks.Add(pictureBlock); }
        }
        return pictureBlocks;
    }

    public List<PictureBlock> GetAllPictureBlocks()
    {
        List<PictureBlock> allPictureBlocks = new List<PictureBlock>();
        foreach (PictureBlock topPictureBlock in GetTopPictureBlocks())
        {
            FillPictureBlocksList(topPictureBlock, allPictureBlocks);
        }
        return allPictureBlocks;
    }

    private bool IsThereAnythingInTheScene()
    {
        foreach (Transform child in transform)
        {
            if (child.gameObject.activeSelf) return true;
        }
        return false;
    }

    private bool HasSomethingToPush()
    {
        if (null == sceneID) return IsThereAnythingInTheScene();
        return !SceneMatchesDescription(transform, (JSONArray)(environment.GetScene(sceneID)["content"]));
    }

    private bool SceneMatchesDescription(Transform rootTransform, JSONArray descriptions)
    {
        int children_count = 0;
        foreach (Transform child in rootTransform)
        {
            if (!child.gameObject.activeSelf) continue;
            PictureBlock pictureBlock = child.GetComponent<PictureBlock>();
            if (null == pictureBlock) continue;
            JSONNode matchingDescription = FindMatchingDescription(pictureBlock, descriptions);
            if (null == matchingDescription) return false;
            JSONNode childrenNode = matchingDescription["children"];
            JSONArray childrenArray = typeof(JSONArray).IsInstanceOfType(childrenNode) ? (JSONArray)childrenNode : emptyJSONArray;
            if (!SceneMatchesDescription(child, childrenArray)) return false;
            ++children_count;
        }
        return children_count == descriptions.Count;
    }

    private JSONNode FindMatchingDescription(PictureBlock pictureBlock, JSONArray descriptions)
    {
        for (int i = 0; i < descriptions.Count; ++i)
        {
            JSONNode pblockDescription = descriptions[i];
            if (pictureBlock.Matches(pblockDescription)) return pblockDescription;
        }
        return null;
    }

    private JSONArray SerializePictureBlocksHierarchy(Transform rootTransform)
    {
        JSONArray pictureBlocksArray = new JSONArray();
        foreach (Transform child in rootTransform)
        {
            PictureBlock pictureBlock = child.GetComponent<PictureBlock>();
            if (null != pictureBlock)
            {
                JSONNode pictureBlockDescription = pictureBlock.Serialize();
                JSONArray childrenDescription = SerializePictureBlocksHierarchy(child);
                if (childrenDescription.Count > 0)
                {
                    pictureBlockDescription["children"] = childrenDescription;
                }
                pictureBlocksArray.Add(pictureBlockDescription);
            }
        }
        return pictureBlocksArray;
    }

    private void DeserializePictureBlockHierarchy(Transform rootTransform, JSONArray description)
    {
        for (int i = 0; i < description.Count; ++i)
        {
            JSONNode pictureblockDescription = description[i];
            PictureBlock pictureBlock = Instantiate(pictureBlockPrefab).GetComponent<PictureBlock>();
            pictureBlock.Setup(rootTransform, pictureblockDescription);
            RegisterPlacedPicture(pictureBlock.gameObject);
            JSONNode childrenNode = pictureblockDescription["children"];
            if (null == childrenNode) continue;
            JSONArray childrenDescription = (JSONArray)childrenNode;
            if (null != childrenDescription)
                DeserializePictureBlockHierarchy(pictureBlock.transform, childrenDescription);
        }
    }

    private void AdjustInscriptionOrientation()
    {
        List<PictureBlock> topPictureBlocks = GetTopPictureBlocks();
        foreach (PictureBlock pictureBlock in topPictureBlocks)
        {
            List<GameObject> inscriptions = new List<GameObject>();
            Inscription.GatherInscriptions(pictureBlock.gameObject, inscriptions);
            foreach (GameObject inscription in inscriptions)
            {
                Vector3 lossyScale = inscription.transform.lossyScale;
                if (lossyScale.x < 0)
                {
                    Vector3 localScale = inscription.transform.localScale;
                    inscription.transform.localScale = new Vector3(-localScale.x, localScale.y, localScale.z);
                }
            }
        }
    }

    private List<GameObject> HideUIElements()
    {
        List<GameObject> rootObjects = new List<GameObject>();
        Scene scene = SceneManager.GetActiveScene();
        scene.GetRootGameObjects(rootObjects);
        rootObjects = rootObjects.Where(obj => obj.activeSelf && !allowedForScreenshot.Contains(obj.tag)).ToList();
        foreach (GameObject objToHide in rootObjects)
        {
            objToHide.SetActive(false);
        }
        return rootObjects;
    }

    private void RestoreUIElements(List<GameObject> hidden)
    {
        foreach (GameObject hiddenObj in hidden)
        {
            hiddenObj.SetActive(true);
        }
    }

    private void FillPictureBlocksList(PictureBlock root, List<PictureBlock> pictureBlockList)
    {
        pictureBlockList.Add(root);
        foreach (Transform childTform in root.transform)
        {
            PictureBlock child = childTform.GetComponent<PictureBlock>();
            if (null != child) { FillPictureBlocksList(child, pictureBlockList); }
        }
    }

    public List<GameObject> GetAllPictureBlockGameObjects()
    {
        List<GameObject> gos = new List<GameObject>();
        foreach (PictureBlock pb in GetAllPictureBlocks())
        {
            if (pb != null && pb.gameObject != null)
            {
                gos.Add(pb.gameObject);
            }
        }
        return gos;
    }

    public Bounds GetWorldBounds(GameObject go)
    {
        if (go == null) return new Bounds(Vector3.zero, Vector3.zero);
        Renderer[] renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers != null && renderers.Length > 0)
        {
            Bounds b = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; ++i) b.Encapsulate(renderers[i].bounds);
            return b;
        }
        return new Bounds(go.transform.position, Vector3.one * 0.01f);
    }

    public IEnumerator MoveObjectSmoothly(GameObject obj, Vector3 targetPos, float duration = 0.5f, Action onComplete = null)
    {
        if (obj == null) yield break;
        Vector3 start = obj.transform.position;
        float elapsed = 0f;
        if (duration <= 0f)
        {
            obj.transform.position = targetPos;
            onComplete?.Invoke();
            yield break;
        }
        while (elapsed < duration)
        {
            float t = Mathf.Clamp01(elapsed / duration);
            obj.transform.position = Vector3.Lerp(start, targetPos, t);
            elapsed += Time.deltaTime;
            yield return null;
        }
        obj.transform.position = targetPos;
        onComplete?.Invoke();
    }

    public void MoveObjectNextTo(GameObject objToMove, GameObject referenceObj, Vector3 direction, float spacing = 0.02f, bool smooth = true, float duration = 0.4f)
    {
        if (objToMove == null || referenceObj == null) return;
        Bounds refBounds = GetWorldBounds(referenceObj);
        Bounds moveBounds = GetWorldBounds(objToMove);
        Vector3 dir = direction.normalized;
        if (dir == Vector3.zero) dir = Vector3.right;
        Vector3 axis;
        if (Mathf.Abs(Vector3.Dot(dir, Vector3.right)) > 0.707f) axis = Vector3.right;
        else if (Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.707f) axis = Vector3.up;
        else axis = Vector3.forward;

        Vector3 refEdge;
        Vector3 moveExtents = moveBounds.extents;
        if (axis == Vector3.right)
        {
            if (Vector3.Dot(dir, Vector3.right) > 0)
                refEdge = new Vector3(refBounds.max.x, refBounds.center.y, refBounds.center.z) + Vector3.right * (moveExtents.x + spacing);
            else
                refEdge = new Vector3(refBounds.min.x, refBounds.center.y, refBounds.center.z) + Vector3.left * (moveExtents.x + spacing);
        }
        else if (axis == Vector3.up)
        {
            if (Vector3.Dot(dir, Vector3.up) > 0)
                refEdge = new Vector3(refBounds.center.x, refBounds.max.y, refBounds.center.z) + Vector3.up * (moveExtents.y + spacing);
            else
                refEdge = new Vector3(refBounds.center.x, refBounds.min.y, refBounds.center.z) + Vector3.down * (moveExtents.y + spacing);
        }
        else
        {
            if (Vector3.Dot(dir, Vector3.forward) > 0)
                refEdge = new Vector3(refBounds.center.x, refBounds.center.y, refBounds.max.z) + Vector3.forward * (moveExtents.z + spacing);
            else
                refEdge = new Vector3(refBounds.center.x, refBounds.center.y, refBounds.min.z) + Vector3.back * (moveExtents.z + spacing);
        }

        Vector3 desiredPos = refEdge;
        if (smooth) StartCoroutine(MoveObjectSmoothly(objToMove, desiredPos, duration));
        else objToMove.transform.position = desiredPos;
    }

    public void MoveObjectOnTopOf(GameObject objToMove, GameObject targetObj, bool smooth = true, float duration = 0.4f, Action onComplete = null)
    {
        if (objToMove == null || targetObj == null) return;
        Bounds targetBounds = GetWorldBounds(targetObj);
        Vector3 placePos = targetBounds.center + Vector3.up * 0.001f; // tiny lift to avoid z-fighting
        if (smooth)
        {
            StartCoroutine(MoveObjectSmoothly(objToMove, placePos, duration, () =>
            {
                CreatePictureBookContainer(objToMove, targetObj);
                onComplete?.Invoke();
            }));
        }
        else
        {
            objToMove.transform.position = placePos;
            CreatePictureBookContainer(objToMove, targetObj);
            onComplete?.Invoke();
        }
    }

    public GameObject CreatePictureBookContainer(GameObject childA, GameObject childB)
    {
        if (childA == null || childB == null) return null;
        GameObject container;
        if (pictureBlockPrefab != null)
        {
            container = Instantiate(pictureBlockPrefab, transform);
            container.name = "PictureBook";
            container.transform.position = (GetWorldBounds(childA).center + GetWorldBounds(childB).center) / 2f;
        }
        else
        {
            container = new GameObject("PictureBook");
            container.transform.SetParent(transform, true);
            container.transform.position = (GetWorldBounds(childA).center + GetWorldBounds(childB).center) / 2f;
        }

        childA.transform.SetParent(container.transform, true);
        childB.transform.SetParent(container.transform, true);


        PictureBlock pb = container.GetComponent<PictureBlock>();

        return container;
    }

    public void RegisterPlacedPicture(GameObject block)
    {
        if (block != null)
            _lastPlacedBlock = block;
    }

    public GameObject GetMostRecentPictureBlock()
    {
        return _lastPlacedBlock;
    }
}