using System.Collections.Generic;
using System.Collections;
using UnityEngine;

public class CoCreateButton : MonoBehaviour, ITappable
{
    [SerializeField] private string seedWord = "cat";
    public GameObject gameObject => base.gameObject;

    private AssociationsPanel assocPanel;
    private Coroutine activeRoutine;
    private bool tapEnabled = false;

    void Start()
    {
        assocPanel = GameObject.FindWithTag("AssociationsPanel")?.GetComponent<AssociationsPanel>();
        if (assocPanel == null)
            Debug.LogWarning("[CoCreateButton] AssociationsPanel not found. Using fallback seedWord only.");
    }

    public void OnTap(TouchInfo touchInfo)
    {
        if (!tapEnabled)
            return;
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
            //Vector3 canvasTarget = FindFreeCanvasSpot(picture);
            // PlacementUtil.RelativePlacement placement = PlacementUtil.GetRandomPlacement();
            // Debug.Log($"[CoCreateButton] Directional placement chosen: {placement}");
            
            StartCoroutine(jibo.GetRobotCollaborativeBehavior(chosenWord, (placement, chosenScale, related_object) =>
            {
                // Compute target positions
                Vector3 jiboTarget = picture.transform.position + new Vector3(0f, 0.2f, 0f); // jump on top
                // find the previously placed picture (fallback to random free-spot behavior if none)
                GameObject prev = GameObject.FindObjectOfType<Composition>()?.GetMostRecentPictureBlock();

                Vector3 canvasTarget = prev != null
                    ? ComputeDirectionalCanvasSpot(prev, picture, placement)
                    : FindFreeCanvasSpot(picture);
                if (float.IsNaN(canvasTarget.x))
                {
                    Debug.LogWarning("[CoCreateButton] No free canvas spot — canceling spawn.");
                    Destroy(picture);
                    return;
                }
            canvasTarget = ClampToCanvasBounds(canvasTarget);

            activeRoutine = StartCoroutine(JumpOntoBoxAndMovePicture(
                jibo, picture, canvasTarget,
                wiggleCycles: 2, slideDuration: 1.0f, scaleBy: chosenScale));
            }));
        }
    }

    private Vector3 ComputeDirectionalCanvasSpot(GameObject prev, GameObject picture, PlacementUtil.RelativePlacement placement)
    {
        Composition comp = GameObject.FindObjectOfType<Composition>();
        if (comp == null)
        {
            Debug.LogWarning("[CoCreateButton] Composition not found, using prev position.");
            return prev.transform.position;
        }

        // Bounds
        Bounds prevBounds = comp.GetWorldBounds(prev);
        Bounds picBounds = comp.GetWorldBounds(picture);

        float padding = 0.2f;

        float stepX = picBounds.size.x + padding;
        float stepY = picBounds.size.y + padding;

        // Start from prev
        Vector3 basePos = prevBounds.center;

        Vector3 candidate = basePos;

        switch (placement)
        {
            case PlacementUtil.RelativePlacement.Left:
                candidate.x -= stepX;
                break;

            case PlacementUtil.RelativePlacement.Right:
                candidate.x += stepX;
                break;

            case PlacementUtil.RelativePlacement.Up:
                candidate.y += stepY;
                break;

            case PlacementUtil.RelativePlacement.Down:
                candidate.y -= stepY;
                break;

                //case RelativePlacement.TopRight:
                //    candidate.x += stepX;
                //    candidate.y += stepY;
                //    break;

                //case RelativePlacement.TopLeft:
                //    candidate.x -= stepX;
                //    candidate.y += stepY;
                //    break;

                //case RelativePlacement.BottomRight:
                //    candidate.x += stepX;
                //    candidate.y -= stepY;
                //    break;

                //case RelativePlacement.BottomLeft:
                //    candidate.x -= stepX;
                //    candidate.y -= stepY;
                //    break;
        }

        candidate = SnapToCanvasGrid(candidate, stepX, stepY);

        // Clamp to canvas bounds (-6..6, -4..4)
        candidate = ClampToCanvasBounds(candidate);

        // Check overlap with existing pictures
        List<GameObject> allPics = comp.GetAllPictureBlockGameObjects();
        allPics.Remove(picture);
        allPics.Remove(prev); // allow touching prev only by edge

        Bounds candidateBounds = new Bounds(candidate, picBounds.size);

        foreach (GameObject other in allPics)
        {
            Bounds otherBounds = comp.GetWorldBounds(other);
            if (candidateBounds.Intersects(otherBounds))
            {
                Debug.Log($"[CoCreateButton] Direction {placement} is blocked by overlap, falling back to FindFreeCanvasSpot()");
                return FindFreeCanvasSpot(picture); // graceful fallback
            }
        }

        Debug.Log($"[CoCreateButton] Directional placement '{placement}' resolved at: {candidate}");
        return candidate;
    }

    private Vector3 SnapToCanvasGrid(Vector3 pos, float stepX, float stepY)
    {
        float minX = -6f, minY = -4f;

        float snappedX = minX + Mathf.Round((pos.x - minX) / stepX) * stepX;
        float snappedY = minY + Mathf.Round((pos.y - minY) / stepY) * stepY;

        return new Vector3(snappedX, snappedY, pos.z);
    }

    private Vector3 ClampToCanvasBounds(Vector3 pos)
    {
        float minX = -6f, maxX = 6f;
        float minY = -4f, maxY = 4f;

        pos.x = Mathf.Clamp(pos.x, minX, maxX);
        pos.y = Mathf.Clamp(pos.y, minY, maxY);
        return pos;
    }


    private System.Collections.IEnumerator JumpOntoBoxAndMovePicture(
        VirtualJiboPartner jibo, GameObject picture, Vector3 canvasTarget,
        int wiggleCycles = 2, float slideDuration = 1.0f, float scaleBy = 1.0f)
    {
        if (jibo == null || picture == null)
            yield break;

        // STEP 1: Jump up onto the picture
        float boxHeight = 2.5f;
        float halfHeight = boxHeight * 0.5f;
        Vector3 jumpTarget = picture.transform.position + new Vector3(0f, halfHeight, 0f);

        yield return jibo.AnimateHappyWiggleJumpToTarget(
            jumpTarget,
            wiggleCycles,
            jumpHeight: halfHeight
        );

        // STEP 2: Slide both Jibo and the picture to the canvas target
        Vector3 startJiboPos = jibo.transform.position;
        Vector3 startPicPos = picture.transform.position;
        Vector3 startScale = picture.transform.localScale; // store original scale
        Vector3 targetScale = startScale * scaleBy; // double size

        double startTime = TimeKeeper.time;

        while (TimeKeeper.time - startTime < slideDuration)
        {
            float t = (float)((TimeKeeper.time - startTime) / slideDuration);
            float easedT = Easing.EaseInOut(t);

            jibo.transform.position = Vector3.Lerp(startJiboPos, canvasTarget, easedT);
            picture.transform.position = Vector3.Lerp(startPicPos, canvasTarget, easedT);
            picture.transform.localScale = Vector3.Lerp(startScale, targetScale, easedT);

            yield return null;
        }

        // Snap to final position and scale
        jibo.transform.position = canvasTarget;
        picture.transform.position = canvasTarget;
        picture.transform.localScale = targetScale;

        Composition comp = GameObject.FindObjectOfType<Composition>();
        if (comp != null)
        {
            comp.RegisterPlacedPicture(picture);
        }
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

        Debug.LogWarning("[CoCreateButton] Canvas is full.");
        return new Vector3(float.NaN, float.NaN, float.NaN);
    }

    public void SetSeedWord(string newWord)
    {
        seedWord = newWord;
        Debug.Log("[CoCreateButton] Seed word set to: " + seedWord);
    }

    public IEnumerator TriggerCoCreateAndWait()
    {
        // This runs the normal spawn logic
        SpawnPicture();

        // If a Jibo movement coroutine is running, wait for it
        if (activeRoutine != null)
            yield return activeRoutine;
    }
}
