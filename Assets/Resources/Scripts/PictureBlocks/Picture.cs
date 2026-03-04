using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SimpleJSON;

public class Picture : MonoBehaviour
{
    private static GameObject avatarPrefab = null;

    private GameObject imageHolder = null;

    private bool setUp = false;
    private string termWordSense = null;
    private string imageWordSense = null;
    private float width = 0;
    private float height = 0;

    private const float INSCRIPTION_FRACTION = 1 / 7.0f;
    private const float INSCRIPTION_PADDED_SIZE = 1.1f;

    public bool IsSetUp()
    {
        return setUp;
    }

    public void Setup(JSONNode description)
    {
        if (setUp) { Reset(); }
        setUp = true;
        width = description["width"];
        height = description["height"];
        imageWordSense = description["img-word-sense"];
        if (null == imageWordSense) { imageWordSense = description["word-sense"]; }
        termWordSense = description["term-word-sense"];
        if (null == termWordSense) { termWordSense = imageWordSense; }
        if ("image" == description["type"])
        {
            imageHolder = CreateImageForWordSense(termWordSense, imageWordSense, width, height);
        }
        else
        {
            imageHolder = CreateAvatar(termWordSense, description["avatar"], width, height);
        }
        ZSorting.SetSortingLayer(gameObject, "default");
        SetupActiveTouchArea();
    }

    public JSONNode Serialize()
    {
        GameObject image = transform.Find("Image").gameObject;
        AvatarControl avatar = image.GetComponent<AvatarControl>();
        JSONNode description = new JSONObject();
        if (null != avatar)
        {
            description["type"] = "avatar";
            description["avatar"] = JSONNode.Parse(avatar.GetDescription());
        }
        else
        {
            description["type"] = "image";
        }
        description["width"] = width;
        description["height"] = height;
        description["img-word-sense"] = imageWordSense;
        description["term-word-sense"] = termWordSense;
        return description;
    }

    public void Setup(string termWordSense, float width, float height, string sortingLayer)
    {
        if (setUp) { Reset(); }
        setUp = true;
        this.width = width;
        this.height = height;
        this.termWordSense = termWordSense;
        this.imageWordSense = GameObject.FindWithTag("StageObject").GetComponent<Vocab>().GetIconicImageable(termWordSense);
        if (Vocab.IsInNameSense(imageWordSense))
        {
            imageHolder = CreateAvatar(termWordSense, imageWordSense, width, height);
        }
        else
        {
            imageHolder = CreateImageForWordSense(termWordSense, imageWordSense, width, height);
        }
        ZSorting.SetSortingLayer(gameObject, sortingLayer);
        SetupActiveTouchArea();
    }

    public void UpdateAvatar(JSONNode avatar)
    {
        string sortingLayer = ZSorting.GetSortingLayer(gameObject);
        Reset();
        imageHolder = CreateAvatar(termWordSense, avatar, width, height);
        ZSorting.SetSortingLayer(gameObject, sortingLayer);
        SetupActiveTouchArea();
    }

    public string GetTermWordSense()
    {
        return termWordSense;
    }

    public string GetImageWordSense()
    {
        return imageWordSense;
    }

    public GameObject GetImageHolder()
    {
        return imageHolder;
    }

    public bool IsAvatar()
    {
        GameObject image = transform.Find("Image").gameObject;
        return null != image.GetComponent<AvatarControl>();
    }

    private void Reset()
    {
        Transform image = transform.Find("Image");
        if (null != image) { Destroy(image.gameObject); }
        Transform inscription = transform.Find("Inscription");
        if (null != inscription) { Destroy(inscription.gameObject); }
    }

    private GameObject CreateImageForWordSense(string termWordSense, string imageWordSense, float width, float height)
    {
        if (imageWordSense.EndsWith(".noimg"))
        {
            // Check if Gemini has already generated an image for this word
            string subject = GetSubjectForGeneration(termWordSense);
            GeminiImageGenerator generator = GeminiImageGenerator.Instance;
            if (generator != null && generator.HasCached(subject))
            {
                imageHolder = CreateImageFromTexture(generator.GetCached(subject), width, height);
            }
            else
            {
                imageHolder = CreateDefaultImage(termWordSense, width, height);
                TryGenerateImage(termWordSense, width, height);
            }
        }
        else if (Vocab.IsInNameSense(imageWordSense))
        {
            imageHolder = CreateDefaultAvatar(termWordSense, width, height);
        }
        else
        {
            imageHolder = CreateImage($"Images/{imageWordSense}", width, height);
            if (null == imageHolder)
            {
                string subject = GetSubjectForGeneration(termWordSense);
                GeminiImageGenerator generator = GeminiImageGenerator.Instance;
                if (generator != null && generator.HasCached(subject))
                {
                    imageHolder = CreateImageFromTexture(generator.GetCached(subject), width, height);
                }
                else
                {
                    imageHolder = CreateDefaultImage(termWordSense, width, height);
                    TryGenerateImage(termWordSense, width, height);
                }
            }
        }
        return imageHolder;
    }

    private GameObject CreateDefaultImage(string termWordSense, float width, float height)
    {
        imageHolder = CreateImage("Visual/UI/default_object", width, height);
        Inscribe(imageHolder, Vocab.GetWord(termWordSense), width, height);
        return imageHolder;
    }

    private GameObject CreateDefaultAvatar(string termWordSense, float width, float height)
    {
        string imagePath = "guest.name" == termWordSense ? "Visual/UI/default_person_guest" : "Visual/UI/default_person";
        imageHolder = CreateImage(imagePath, width, height);
        Inscribe(imageHolder, Vocab.GetFullNameFromNameSense(termWordSense), width, height);
        return imageHolder;
    }

    private GameObject CreateImage(string spritePath, float width, float height)
    {
        GameObject imageObject = new GameObject();
        imageObject.name = "Image";
        imageObject.transform.SetParent(transform, false);
        SpriteRenderer spriteRenderer = imageObject.AddComponent<SpriteRenderer>();
        try
        {
            Sprite sprite = SpriteUtil.LoadInternalSprite(spritePath, 100);
            spriteRenderer.sprite = sprite;
            float targetScale = Mathf.Min(width / sprite.bounds.size.x,
                              height / sprite.bounds.size.y);
            imageObject.transform.localScale = new Vector3(targetScale, targetScale, targetScale);
            imageObject.transform.localPosition = new Vector3(0, 0, -0.1f);
            imageHolder = imageObject;
            return imageObject;
        }
        catch
        {
            Debug.Log("PROBLEM LOADING " + spritePath);
            Destroy(imageObject);
            return null;
        }
    }

    private GameObject CreateAvatar(string termWordSense, string imageWordSense, float width, float height)
    {
        JSONNode description = GameObject.FindWithTag("StageObject").GetComponent<Environment>().GetAvatar(imageWordSense);
        if (null != description)
        {
            return CreateAvatar(termWordSense, description, width, height);
        }
        else
        {
            return CreateDefaultAvatar(termWordSense, width, height);
        }
    }

    private GameObject CreateAvatar(string termWordSense, JSONNode description, float width, float height)
    {
        if (null == avatarPrefab) { avatarPrefab = Resources.Load<GameObject>("Prefabs/Avatar"); }
        GameObject avatar = Instantiate(avatarPrefab);
        avatar.GetComponent<AvatarControl>().Setup(description);
        avatar.name = "Image";
        float targetScale = Mathf.Min(width / 1.8f,
                                      height / 1.8f);
        avatar.transform.localScale = new Vector3(targetScale, targetScale, targetScale);
        avatar.transform.SetParent(transform, false);
        avatar.transform.localPosition = new Vector3(0, 0, -0.1f);
        imageHolder = avatar;
        Inscribe(imageHolder, Vocab.GetFullNameFromNameSense(termWordSense), width, height);
        return avatar;
    }

    private void Inscribe(GameObject image, string word, float width, float height)
    {
        float yOffset = 0.5f * INSCRIPTION_PADDED_SIZE * width * INSCRIPTION_FRACTION;
        image.transform.localPosition = new Vector3(0, yOffset, -0.1f);
        float targetScale = image.transform.localScale.y * (1 - INSCRIPTION_PADDED_SIZE * INSCRIPTION_FRACTION);
        image.transform.localScale = new Vector3(targetScale, targetScale, targetScale);
        GameObject inscriptionObject = Inscription.Create(word, width, height * INSCRIPTION_FRACTION, Color.black, "default");
        inscriptionObject.transform.SetParent(transform, false);
        float inscriptionY = -0.5f * height * (1 - INSCRIPTION_FRACTION);
        inscriptionObject.transform.localPosition = new Vector3(0, inscriptionY, 0);
        inscriptionObject.name = "Inscription";
    }

    private void SetupActiveTouchArea()
    {
        ActiveTouchArea activeTouchArea = gameObject.GetComponent<ActiveTouchArea>();
        if (null == activeTouchArea) return;
        Transform imageTransform = GetImageHolder().transform;
        if (null != imageTransform.GetComponent<AvatarControl>())
        {
            GameObject[] activeZoneHolders = { imageTransform.Find("Skin").gameObject, imageTransform.Find("Hair").gameObject };
            activeTouchArea.Setup(activeZoneHolders);
        }
        else
        {
            activeTouchArea.Setup(imageTransform.gameObject);
        }
    }

    private string GetSubjectForGeneration(string wordSense)
    {
        try
        {
            string word = Vocab.GetWord(wordSense);
            if (!string.IsNullOrEmpty(word)) return word;
        }
        catch { }
        return wordSense;
    }

    private void TryGenerateImage(string termWordSense, float width, float height)
    {
        GeminiImageGenerator generator = GeminiImageGenerator.Instance;
        if (generator == null || !generator.IsAvailable()) return;
        string subject = GetSubjectForGeneration(termWordSense);
        StartCoroutine(GenerateAndReplaceImage(subject, width, height));
    }

    private IEnumerator GenerateAndReplaceImage(string subject, float width, float height)
    {
        Texture2D generatedTexture = null;
        yield return GeminiImageGenerator.Instance.GenerateImage(subject, tex => generatedTexture = tex);

        if (generatedTexture != null && imageHolder != null)
        {
            // Remember current sorting layer/order before replacing
            string sortingLayer = ZSorting.GetSortingLayer(gameObject);
            int sortingOrder = ZSorting.GetSortingOrder(gameObject);

            // Remove the old default image and inscription
            Transform oldImage = transform.Find("Image");
            if (oldImage != null) Destroy(oldImage.gameObject);
            Transform inscription = transform.Find("Inscription");
            if (inscription != null) Destroy(inscription.gameObject);

            // Create new image from the generated texture
            imageHolder = CreateImageFromTexture(generatedTexture, width, height);

            // Re-apply sorting layer/order so the new sprite is visible
            ZSorting.SetSortingLayer(gameObject, sortingLayer);
            ZSorting.SetSortingOrder(gameObject, sortingOrder);

            Debug.Log("[Picture] Replaced default image with Gemini-generated image for: " + subject);
        }
    }

    private GameObject CreateImageFromTexture(Texture2D texture, float width, float height)
    {
        GameObject imageObject = new GameObject();
        imageObject.name = "Image";
        imageObject.transform.SetParent(transform, false);
        SpriteRenderer spriteRenderer = imageObject.AddComponent<SpriteRenderer>();
        float ppu = Mathf.Max(texture.width / width, texture.height / height);
        Sprite sprite = Sprite.Create(
            texture,
            new Rect(0, 0, texture.width, texture.height),
            new Vector2(0.5f, 0.5f),
            ppu
        );
        spriteRenderer.sprite = sprite;
        float targetScale = Mathf.Min(width / sprite.bounds.size.x, height / sprite.bounds.size.y);
        imageObject.transform.localScale = new Vector3(targetScale, targetScale, targetScale);
        imageObject.transform.localPosition = new Vector3(0, 0, -0.1f);
        return imageObject;
    }
}
