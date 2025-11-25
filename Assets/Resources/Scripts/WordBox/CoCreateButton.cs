using System.Collections.Generic;
using UnityEngine;

public class CoCreateButton : MonoBehaviour, ITappable
{
    [SerializeField] private string seedWord = "cat";
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

        Vector3 spawnPos = transform.position;
        spawnPos.z = -0.1f;

        GameObject picture = Instantiate(prefab, spawnPos, Quaternion.identity);
        picture.name = "PictureBlock_" + chosenWord;

        var behavior = GameObject.FindObjectOfType<VirtualJiboPartner>();
        if (behavior != null)
            behavior.AssignObjectOfInterest(picture);
            Debug.LogWarning("looking at image");

        GameObject compositionRoot = GameObject.FindWithTag("CompositionRoot");
        if (compositionRoot != null)
            picture.transform.SetParent(compositionRoot.transform, true);

        picture.transform.localScale = Vector3.one;

        PictureBlock pb = picture.GetComponent<PictureBlock>();
        if (pb != null)
            pb.Setup(spawnPos, chosenWord, "Default");

        Debug.Log("[CoCreateButton] Spawned picture: " + chosenWord);

        // Find Jibo in scene
        VirtualJiboPartner jibo = GameObject.FindObjectOfType<VirtualJiboPartner>();
        if (jibo != null)
        {
            // Compute target positions
            Vector3 jiboTarget = picture.transform.position + new Vector3(0f, 0.2f, 0f); // jump on top
            Vector3 canvasTarget = FindFreeCanvasSpot(picture);

            StartCoroutine(JumpOntoBox(jibo, picture, canvasTarget));
        }
    }
    private System.Collections.IEnumerator JumpOntoBox(
    VirtualJiboPartner jibo, GameObject picture, Vector3 canvasTarget,
    int wiggleCycles = 2, float slideDuration = 1.0f)
    {
        if (jibo == null || picture == null)
            yield break;

        // box_height
        float boxHeight = 2.5f;
        float halfHeight = boxHeight * 0.5f;

        Vector3 jumpTarget = picture.transform.position + new Vector3(0, halfHeight, 0);

        Debug.Log($"[JumpOntoBox] JumpTarget = {jumpTarget}, boxHeight = {boxHeight}");

        // Step 1: Jump up onto the box
        yield return jibo.AnimateHappyWiggleJumpToTarget(
            jumpTarget,
            wiggleCycles,
            jumpHeight: halfHeight
        );

        // Step 2: Slide both objects
        Vector3 startJiboPos = jibo.transform.position;
        Vector3 startPicPos = picture.transform.position;

        double startTime = TimeKeeper.time;

        while (TimeKeeper.time - startTime < slideDuration)
        {
            float t = (float)((TimeKeeper.time - startTime) / slideDuration);
            float easedT = Easing.EaseInOut(t);

            jibo.transform.position = Vector3.Lerp(startJiboPos, canvasTarget, easedT);
            picture.transform.position = Vector3.Lerp(startPicPos, canvasTarget, easedT);

            yield return null;
        }

        jibo.transform.position = canvasTarget;
        picture.transform.position = canvasTarget;
    }

    private Vector3 FindFreeCanvasSpot(GameObject picture)
    {
        Composition comp = GameObject.FindObjectOfType<Composition>();
        if (comp == null)
        {
            Debug.LogWarning("[CoCreateButton] Composition not found, returning current position.");
            return picture.transform.position;
        }

        // Get all existing picture blocks except the new one
        List<GameObject> allPics = comp.GetAllPictureBlockGameObjects();
        allPics.Remove(picture);

        // Base bounds of the picture
        Bounds pictureBounds = comp.GetWorldBounds(picture);

        float stepX = pictureBounds.size.x + 0.2f;
        float stepY = pictureBounds.size.y + 0.2f;

        // Candidate area
        float minX = -6f, maxX = 6f;
        float minY = -4f, maxY = 4f;

        List<(Vector3 pos, float score)> candidateSpots = new List<(Vector3, float)>();
        float bestScore = -1f;

        for (float x = minX; x <= maxX; x += stepX)
        {
            for (float y = minY; y <= maxY; y += stepY)
            {
                Vector3 candidate = new Vector3(x, y, picture.transform.position.z);

                // Skip the current position
                if (Vector3.Distance(candidate, picture.transform.position) < 0.01f)
                    continue;

                Bounds candidateBounds = new Bounds(candidate, pictureBounds.size);

                bool overlaps = false;
                foreach (GameObject other in allPics)
                {
                    Bounds otherBounds = comp.GetWorldBounds(other);
                    if (candidateBounds.Intersects(otherBounds))
                    {
                        overlaps = true;
                        break;
                    }
                }

                if (!overlaps)
                {
                    // Distance to closest existing picture (edge-to-edge)
                    float minDistance = float.MaxValue;
                    foreach (GameObject other in allPics)
                    {
                        Bounds otherBounds = comp.GetWorldBounds(other);
                        float dx = Mathf.Max(0f, Mathf.Max(otherBounds.min.x - candidateBounds.max.x, candidateBounds.min.x - otherBounds.max.x));
                        float dy = Mathf.Max(0f, Mathf.Max(otherBounds.min.y - candidateBounds.max.y, candidateBounds.min.y - otherBounds.max.y));
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        if (distance < minDistance) minDistance = distance;
                    }

                    // Score: higher if closer to other objects
                    float score = minDistance > 0 ? 1f / minDistance : 1f;

                    // Center bias: favor spots closer to (0,0)
                    float distanceToCenter = candidate.magnitude;
                    score += (1f / (distanceToCenter + 0.01f)) * 0.5f; // small weight

                    // Track best spots
                    if (score > bestScore)
                    {
                        bestScore = score;
                        candidateSpots.Clear();
                        candidateSpots.Add((candidate, score));
                    }
                    else if (Mathf.Approximately(score, bestScore))
                    {
                        candidateSpots.Add((candidate, score));
                    }
                }
            }
        }

        if (candidateSpots.Count > 0)
        {
            // Pick randomly among the top-scoring spots
            var chosen = candidateSpots[UnityEngine.Random.Range(0, candidateSpots.Count)].pos;
            Debug.Log("[CoCreateButton] Closest free canvas spot (center + near objects): " + chosen);
            return chosen;
        }

        Debug.LogWarning("[CoCreateButton] Canvas is full, returning current position.");
        return picture.transform.position;
    }
    public void SetSeedWord(string newWord)
    {
        seedWord = newWord;
    }
}