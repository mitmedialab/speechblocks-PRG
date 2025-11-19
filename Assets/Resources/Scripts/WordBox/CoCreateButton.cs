using System.Collections.Generic;
using UnityEngine;


public class CoCreateButton : MonoBehaviour, ITappable
{
    [SerializeField] private string seedWord = "cat";  // default fallback
    public GameObject gameObject => base.gameObject;

    private AssociationsPanel assocPanel;

    void Start()
    {
        assocPanel = GameObject.FindWithTag("AssociationsPanel")?.GetComponent<AssociationsPanel>();
        if (assocPanel == null)
            Debug.LogWarning("[CoCreateButton] AssociationsPanel not found. Using fallback seedWord only.");
    }

    public void OnTap(TouchInfo touchInfo)
    {
        SpawnPicture();
    }

    private void SpawnPicture()
    {
        GameObject prefab = Resources.Load<GameObject>("Prefabs/PictureBlock");
        if (prefab == null)
        {
            Debug.LogWarning("[CoCreateButton] PictureBlock prefab not found!");
            return;
        }

        string chosenWord = seedWord;
        if (assocPanel != null)
        {
            List<WordSuggestion> suggestions = assocPanel.GetSuggestionsForWord(seedWord);
            suggestions.RemoveAll(s => s.GetWordSense() == seedWord);
            if (suggestions.Count > 0)
                chosenWord = suggestions[Random.Range(0, suggestions.Count)].GetWordSense();
        }

        // Spawn position: same as button, but z in front
        Vector3 spawnPos = transform.position;
        spawnPos.z = -0.1f;  // slightly in front of everything

        GameObject picture = Instantiate(prefab, spawnPos, Quaternion.identity);
        picture.name = "PictureBlock_" + chosenWord;

        // Parent to composition root if exists
        GameObject compositionRoot = GameObject.FindWithTag("CompositionRoot");
        if (compositionRoot != null)
            picture.transform.SetParent(compositionRoot.transform, true);

        // Make it really large
        picture.transform.localScale = Vector3.one;

    
        // Setup PictureBlock script if it exists
        PictureBlock pb = picture.GetComponent<PictureBlock>();
        if (pb != null)
        {
            pb.Setup(spawnPos, chosenWord, "Default");
        }

        Debug.Log("[CoCreateButton] Spawned picture: " + chosenWord);
    }

    public void SetSeedWord(string newWord)
    {
        seedWord = newWord;
    }
}