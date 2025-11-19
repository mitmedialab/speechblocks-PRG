using System;
using System.Collections.Generic;
using UnityEngine;


public class ResultBox : MonoBehaviour
{
    private GameObject editButton;
    private GameObject spawnedPictureBlock = null;
    private SpriteRenderer spriteRenderer = null;
    private Environment environment = null;
    private string wordSense = null;
    private List<Action> deploymentCallbacks = new List<Action>();

    void Start()
    {
        GameObject stageObject = GameObject.FindWithTag("StageObject");
        environment = stageObject.GetComponent<Environment>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        editButton = transform.Find("edit-button").gameObject;
        UpdateEditButton();
    }

    public string GetWordSense()
    {
        return wordSense;
    }

    public void AddDeploymentCallback(Action callback)
    {
        deploymentCallbacks.Add(callback);
    }

    public void OnDeploy()
    {
        StartCoroutine(OnDeployCoroutine());
    }


    private System.Collections.IEnumerator OnDeployCoroutine()
    {
        // Run deployment callbacks
        foreach (Action callback in deploymentCallbacks)
        {
            try { callback(); }
            catch (Exception e) { ExceptionUtil.OnException(e); }
        }

        string currentWord = wordSense;

        // Reset UI state
        wordSense = null;
        UpdateEditButton();

        // Wait 5 seconds
        yield return new WaitForSeconds(3f);

        // Spawn a picture block
        SpawnRandomPictureInScene(currentWord);
    }

    private void SpawnRandomPictureInScene(string seedWord)
    {
        GameObject prefab = Resources.Load<GameObject>("Prefabs/PictureBlock");
        if (prefab == null)
        {
            Debug.LogWarning("Could not find PictureBlock prefab in Resources/Prefabs/");
            return;
        }

        // Get suggestions from AssociationsPanel
        AssociationsPanel assocPanel = GameObject.FindWithTag("AssociationsPanel")?.GetComponent<AssociationsPanel>();
        List<WordSuggestion> suggestions = new List<WordSuggestion>();
        if (!string.IsNullOrEmpty(seedWord) && assocPanel != null)
        {
            suggestions = assocPanel.GetSuggestionsForWord(seedWord);
            suggestions.RemoveAll(s => s.GetWordSense() == seedWord); // avoid spawning the same word
        }

        // Choose a wordSense based on suggestions or fallback
        string chosenWordSense = (suggestions.Count > 0)
            ? suggestions[UnityEngine.Random.Range(0, suggestions.Count)].GetWordSense()
            : new string[] { "cat", "sun", "tree", "car", "book" }[UnityEngine.Random.Range(0, 5)];

        Debug.Log("[ResultBox] Spawning: " + chosenWordSense);

        // Instantiate PictureBlock
        GameObject pictureObj = Instantiate(prefab);
        pictureObj.name = $"PictureBlock_{chosenWordSense}";

        GameObject compositionRoot = GameObject.FindWithTag("CompositionRoot");
        if (compositionRoot != null)
            pictureObj.transform.SetParent(compositionRoot.transform, false);

        float range = 1.0f;
        pictureObj.transform.localPosition = new Vector3(
            UnityEngine.Random.Range(-range, range),
            UnityEngine.Random.Range(-range, range),
            0f
        );

        // Setup PictureBlock
        PictureBlock pb = pictureObj.GetComponent<PictureBlock>();
        if (pb != null)
        {
            pb.Setup(pictureObj.transform.position, chosenWordSense, "Default");

            // Scale the PictureBlock to match ResultBox size
            if (spriteRenderer != null)
            {
                float scale = pb.transform.localScale.x;
                float targetWidth = spriteRenderer.size.x;
                scale = 0.95f * Mathf.Min(
                    spriteRenderer.size.y * scale / pb.GetHeight(),
                    targetWidth * scale / pb.GetWidth()
                );
                pb.transform.localScale = new Vector3(scale, scale, 1);
            }

            // Ensure Z is slightly in front
            pb.transform.localPosition = new Vector3(pb.transform.localPosition.x, pb.transform.localPosition.y, -0.1f);
        }

        spawnedPictureBlock = pictureObj;

        if (pb != null) Logging.LogBirth(pb.gameObject, "res-box-spawn");

        // Let Jibo react: jump on top of the new block
        VirtualJiboPartner jibo = GameObject.FindObjectOfType<VirtualJiboPartner>();
        if (jibo != null)
        {
            // Make Jibo smaller
            jibo.transform.localScale = Vector3.one * 0.5f;

            // Store starting position
            Vector3 originalPos = jibo.transform.position;

            // Target position: slightly above the new block
            Vector3 jiboTarget = pictureObj.transform.position + new Vector3(0f, pb.GetHeight() / 2f + 0.1f, 0f);

            // First jump: on top of the new block
            StartCoroutine(jibo.AnimateHappyWiggleJumpToTarget(jiboTarget, wiggleCycles: 2, jumpHeight: 0.3f));


        }
    }

    public GameObject GetSpawnedPictureBlock()
    {
        return spawnedPictureBlock;
    }

    public void Refresh(string wordSense)
    {
        Clear();
        this.wordSense = wordSense;
        SpawnPictureBlock();
        UpdateEditButton();
    }

    public void Clear()
    {
        wordSense = null;
        ClearSpawnPlace();
        UpdateEditButton();
    }

    public void Edit()
    {
        AvatarPicker avatarPicker = GameObject.FindWithTag("AvatarPicker").GetComponent<AvatarPicker>();
        if (avatarPicker.IsRetracted()) { avatarPicker.Deploy(wordSense, deployInstantly: false); }
    }

    private void SpawnPictureBlock()
    {
        spawnedPictureBlock = (GameObject)Instantiate(Resources.Load("Prefabs/PictureBlock"));
        PictureBlock thePB = spawnedPictureBlock.GetComponent<PictureBlock>();
        thePB.Setup(transform.position, wordSense: wordSense, sortingLayer: "word_drawer");
        thePB.transform.SetParent(transform, false);
        float scale = thePB.transform.localScale.x;
        float targetWidth = spriteRenderer.size.x;
        scale = 0.95f * Mathf.Min(spriteRenderer.size.y * scale / thePB.GetHeight(), targetWidth * scale / thePB.GetWidth());
        thePB.transform.localScale = new Vector3(scale, scale, 1);
        thePB.transform.localPosition = new Vector3(0, 0, -0.1f);
        DeploymentMonitor deploymentMonitor = spawnedPictureBlock.AddComponent<DeploymentMonitor>();
        deploymentMonitor.AddCallback(OnDeploy);
        Logging.LogBirth(thePB.gameObject, "res-box");
        environment.GetRoboPartner().SuggestObjectsOfInterest(new List<GameObject>() { spawnedPictureBlock });
    }

    private void ClearSpawnPlace()
    {
        if (spawnedPictureBlock != null)
        {
            Logging.LogDeath(spawnedPictureBlock, "spawn-place-clear");
            Destroy(spawnedPictureBlock);
            spawnedPictureBlock = null;
        }
    }

    private void UpdateEditButton() {
        if (Vocab.IsInNameSense(wordSense))
        {
            editButton.SetActive(true);
        }
        else
        {
            editButton.SetActive(false);
        }
    }
}